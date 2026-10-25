using System.ComponentModel.DataAnnotations;
using DollarShark.Infrastructure.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DollarShark.Api.Controllers;

[Route("api/auth")]
public sealed class AuthController(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signInManager,
    IAntiforgery antiforgery) : ApiControllerBase
{
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

        await signInManager.SignInAsync(user, isPersistent: false);
        return Ok(ToProfile(user));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
            return Unauthorized(new ProblemDetails { Status = StatusCodes.Status401Unauthorized, Title = "Email or password is incorrect." });

        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            return Unauthorized(new ProblemDetails { Status = StatusCodes.Status401Unauthorized, Title = "Email or password is incorrect." });
        }

        var result = await signInManager.PasswordSignInAsync(user, request.Password, isPersistent: false, lockoutOnFailure: true);
        if (!result.Succeeded)
            return Unauthorized(new ProblemDetails { Status = StatusCodes.Status401Unauthorized, Title = "Email or password is incorrect." });
        return Ok(ToProfile(user));
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

    private static UserProfile ToProfile(ApplicationUser user) => new(user.Id, user.Email ?? string.Empty);

    private IActionResult RegistrationFailure() => BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
    {
        ["registration"] = ["Registration could not be completed. Check the email and password requirements."]
    }));
}

public sealed record RegisterRequest(string? Email, string? Password);
public sealed record LoginRequest(string? Email, string? Password);
public sealed record UserProfile(string Id, string Email);
