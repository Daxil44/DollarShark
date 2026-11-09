using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using DollarShark.Api.Email;
using DollarShark.Infrastructure.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DollarShark.Api.Controllers;

public sealed class AccountAuthOptions
{
    /// <summary>When true (the default), new users must confirm their email before they can sign in.</summary>
    public bool RequireConfirmedEmail { get; set; } = true;
    /// <summary>Absolute frontend origin used to build links in account emails.</summary>
    public string FrontendBaseUrl { get; set; } = "http://localhost:5173";
}

[Route("api/auth")]
public sealed class AuthController(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signInManager,
    IAntiforgery antiforgery,
    EmailQueue emailQueue,
    IOptions<AccountAuthOptions> authOptions,
    IAuthenticationSchemeProvider schemes,
    ILogger<AuthController> logger) : ApiControllerBase
{
    public const string EmailRateLimitPolicy = "auth-email";
    public const string LoginRateLimitPolicy = "auth-login";
    public const string GoogleScheme = "Google";
    public const string GoogleEmailVerifiedClaim = "urn:google:email_verified";

    [HttpGet("providers")]
    [AllowAnonymous]
    public async Task<IActionResult> Providers() =>
        Ok(new { google = await schemes.GetSchemeAsync(GoogleScheme) is not null });

    [HttpGet("external/google")]
    [AllowAnonymous]
    public async Task<IActionResult> GoogleSignIn()
    {
        if (await schemes.GetSchemeAsync(GoogleScheme) is null) return NotFound();
        var properties = signInManager.ConfigureExternalAuthenticationProperties(GoogleScheme, Url.Action(nameof(GoogleCallback)));
        return Challenge(properties, GoogleScheme);
    }

    [HttpGet("external/google/callback")]
    [AllowAnonymous]
    public async Task<IActionResult> GoogleCallback()
    {
        var info = await signInManager.GetExternalLoginInfoAsync();
        if (info is null) return RedirectToFrontend("/login?error=google");
        try
        {
            // Returning user who already linked Google.
            var existing = await signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, isPersistent: false, bypassTwoFactor: true);
            if (existing.Succeeded) return RedirectToFrontend("/dashboard");
            if (existing.IsLockedOut || existing.IsNotAllowed) return RedirectToFrontend("/login?error=google");

            var email = info.Principal.FindFirstValue(ClaimTypes.Email)?.Trim();
            var verified = string.Equals(info.Principal.FindFirstValue(GoogleEmailVerifiedClaim), "true", StringComparison.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(email) || !verified) return RedirectToFrontend("/login?error=google");

            var user = await users.FindByEmailAsync(email);
            if (user is null)
            {
                user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
                if (!(await users.CreateAsync(user)).Succeeded) return RedirectToFrontend("/login?error=google");
            }
            else if (!user.EmailConfirmed)
            {
                // Someone may have pre-registered this address without owning it. Google proves ownership,
                // so drop any password set by that unconfirmed sign-up before linking.
                if (await users.HasPasswordAsync(user)) await users.RemovePasswordAsync(user);
                user.EmailConfirmed = true;
                await users.UpdateAsync(user);
                await users.UpdateSecurityStampAsync(user);
            }

            if (!(await users.AddLoginAsync(user, info)).Succeeded) return RedirectToFrontend("/login?error=google");
            await signInManager.SignInAsync(user, isPersistent: false);
            return RedirectToFrontend("/dashboard");
        }
        finally
        {
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        }
    }

    private RedirectResult RedirectToFrontend(string path) => Redirect(authOptions.Value.FrontendBaseUrl.TrimEnd('/') + path);
    private const string EmailNotConfirmedCode = "email_not_confirmed";

    [HttpGet("csrf")]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public IActionResult CsrfToken()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new { requestToken = tokens.RequestToken });
    }

    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(EmailRateLimitPolicy)]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        var email = request.Email?.Trim();
        if (string.IsNullOrWhiteSpace(email) || !new EmailAddressAttribute().IsValid(email) || email.Length > 256 ||
            string.IsNullOrEmpty(request.Password) || request.Password.Length > 128)
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["registration"] = ["Enter a valid email address and a password that meets the password requirements."]
            }));

        var user = new ApplicationUser { UserName = email, Email = email };
        IdentityResult result;
        try
        {
            result = await users.CreateAsync(user, request.Password);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // A concurrent sign-up can race the Identity pre-check; the unique SQL index still protects data.
            return RegistrationFailure();
        }
        if (!result.Succeeded)
            return RegistrationFailure();

        await QueueConfirmationEmail(user);

        if (authOptions.Value.RequireConfirmedEmail)
            return Accepted(new RegistrationPending(true, email));

        await signInManager.SignInAsync(user, isPersistent: false);
        return Ok(ToProfile(user));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(LoginRateLimitPolicy)]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
            return InvalidCredentials();

        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null)
            return InvalidCredentials();

        // Verify the password (with lockout) before revealing anything about confirmation status,
        // so the "confirm your email" response cannot be used to discover registered addresses.
        var check = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!check.Succeeded)
            return InvalidCredentials();

        if (authOptions.Value.RequireConfirmedEmail && !await users.IsEmailConfirmedAsync(user))
        {
            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Confirm your email address before signing in. Check your inbox for the confirmation link.",
            };
            problem.Extensions["code"] = EmailNotConfirmedCode;
            return StatusCode(StatusCodes.Status403Forbidden, problem);
        }

        await signInManager.SignInAsync(user, isPersistent: false);
        return Ok(ToProfile(user));
    }

    [HttpPost("confirm-email")]
    [AllowAnonymous]
    public async Task<IActionResult> ConfirmEmail(ConfirmEmailRequest request)
    {
        var user = string.IsNullOrWhiteSpace(request.UserId) ? null : await users.FindByIdAsync(request.UserId);
        var token = DecodeToken(request.Token);
        if (user is null || token is null)
            return InvalidLink("This confirmation link is invalid or has expired. Request a new one from the sign-in page.");

        if (await users.IsEmailConfirmedAsync(user))
            return Ok(new { message = "Your email is already confirmed. You can sign in." });

        var result = await users.ConfirmEmailAsync(user, token);
        return result.Succeeded
            ? Ok(new { message = "Email confirmed. You can now sign in." })
            : InvalidLink("This confirmation link is invalid or has expired. Request a new one from the sign-in page.");
    }

    [HttpPost("resend-confirmation")]
    [AllowAnonymous]
    [EnableRateLimiting(EmailRateLimitPolicy)]
    public async Task<IActionResult> ResendConfirmation(EmailOnlyRequest request)
    {
        var user = string.IsNullOrWhiteSpace(request.Email) ? null : await users.FindByEmailAsync(request.Email.Trim());
        if (user is not null && !await users.IsEmailConfirmedAsync(user))
            await QueueConfirmationEmail(user);
        // Same response whether or not the account exists.
        return Accepted(new { message = "If that account needs confirmation, a new link is on its way." });
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting(EmailRateLimitPolicy)]
    public async Task<IActionResult> ForgotPassword(EmailOnlyRequest request)
    {
        var user = string.IsNullOrWhiteSpace(request.Email) ? null : await users.FindByEmailAsync(request.Email.Trim());
        if (user?.Email is not null)
        {
            var token = await users.GeneratePasswordResetTokenAsync(user);
            Queue(AccountEmails.PasswordReset(user.Email, BuildLink("/reset-password", user.Id, token)));
        }
        // Same response whether or not the account exists.
        return Accepted(new { message = "If an account exists for that email, a reset link is on its way." });
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting(EmailRateLimitPolicy)]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request)
    {
        const string invalidLink = "This reset link is invalid or has expired. Request a new one.";
        if (string.IsNullOrEmpty(request.Password) || request.Password.Length > 128)
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["password"] = ["Enter a new password that meets the password requirements."]
            }));

        var user = string.IsNullOrWhiteSpace(request.UserId) ? null : await users.FindByIdAsync(request.UserId);
        var token = DecodeToken(request.Token);
        if (user is null || token is null)
            return InvalidLink(invalidLink);

        var result = await users.ResetPasswordAsync(user, token, request.Password);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(error => error.Code == nameof(IdentityErrorDescriber.InvalidToken)))
                return InvalidLink(invalidLink);
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["password"] = result.Errors.Select(error => error.Description).ToArray()
            }));
        }

        // Receiving the reset email proves the user controls the inbox, and a reset should clear a lockout.
        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            await users.UpdateAsync(user);
        }
        await users.ResetAccessFailedCountAsync(user);
        await users.SetLockoutEndDateAsync(user, null);
        return Ok(new { message = "Your password has been reset. You can now sign in." });
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        var user = await users.GetUserAsync(User);
        return user is null ? Unauthorized() : Ok(ToProfile(user));
    }

    /// <summary>
    /// Permanently deletes the signed-in user's account and all their data. Requires typing DELETE plus the
    /// current password (or, for Google-only accounts, the account email) so a stolen session alone can't do it.
    /// </summary>
    [HttpPost("delete-account")]
    [Authorize]
    [EnableRateLimiting(LoginRateLimitPolicy)]
    public async Task<IActionResult> DeleteAccount(DeleteAccountRequest request, [FromServices] AccountDeletionService deletion, CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Unauthorized();

        if (!string.Equals(request.Confirmation?.Trim(), "DELETE", StringComparison.Ordinal))
            return Invalid("confirmation", "Type DELETE (in capital letters) to confirm.");

        if (await users.HasPasswordAsync(user))
        {
            var check = string.IsNullOrEmpty(request.Password)
                ? Microsoft.AspNetCore.Identity.SignInResult.Failed
                : await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
            if (check.IsLockedOut) return Invalid("password", "Too many wrong passwords. Try again in 15 minutes.");
            if (!check.Succeeded) return Invalid("password", "Password is incorrect.");
        }
        else if (!string.Equals(request.ConfirmEmail?.Trim(), user.Email, StringComparison.OrdinalIgnoreCase))
        {
            return Invalid("confirmEmail", "Type your account email address to confirm.");
        }

        var result = await deletion.DeleteAsync(user, cancellationToken);
        if (!result.Succeeded)
            return Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Your account could not be deleted. Nothing was removed; please try again.");

        await signInManager.SignOutAsync();
        return NoContent();
    }

    private BadRequestObjectResult Invalid(string field, string message) =>
        BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { [field] = [message] }));

    private async Task QueueConfirmationEmail(ApplicationUser user)
    {
        if (user.Email is null) return;
        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        Queue(AccountEmails.Confirmation(user.Email, BuildLink("/confirm-email", user.Id, token)));
    }

    private void Queue(EmailMessage message)
    {
        if (!emailQueue.TryEnqueue(message))
            logger.LogError("Email queue is full; dropped \"{Subject}\".", message.Subject);
    }

    private string BuildLink(string path, string userId, string token)
    {
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var baseUrl = authOptions.Value.FrontendBaseUrl.TrimEnd('/');
        return QueryHelpers.AddQueryString($"{baseUrl}{path}", new Dictionary<string, string?> { ["userId"] = userId, ["token"] = encodedToken });
    }

    private static string? DecodeToken(string? encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded) || encoded.Length > 4096) return null;
        try
        {
            return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encoded));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static UserProfile ToProfile(ApplicationUser user) => new(user.Id, user.Email ?? string.Empty, user.PasswordHash is not null);

    private IActionResult InvalidCredentials() =>
        Unauthorized(new ProblemDetails { Status = StatusCodes.Status401Unauthorized, Title = "Email or password is incorrect." });

    private IActionResult InvalidLink(string message) =>
        BadRequest(new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = message });

    private IActionResult RegistrationFailure() => BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
    {
        ["registration"] = ["Registration could not be completed. Check the email and password requirements."]
    }));
}

public sealed record RegisterRequest(string? Email, string? Password);
public sealed record LoginRequest(string? Email, string? Password);
public sealed record EmailOnlyRequest(string? Email);
public sealed record ConfirmEmailRequest(string? UserId, string? Token);
public sealed record ResetPasswordRequest(string? UserId, string? Token, string? Password);
public sealed record RegistrationPending(bool RequiresEmailConfirmation, string Email);
public sealed record UserProfile(string Id, string Email, bool HasPassword = true);
public sealed record DeleteAccountRequest(string? Password, string? ConfirmEmail, string? Confirmation);
