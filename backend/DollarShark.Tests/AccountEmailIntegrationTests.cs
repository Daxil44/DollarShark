using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DollarShark.Api.Email;
using DollarShark.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace DollarShark.Tests;

public sealed class AccountEmailIntegrationTests
{
    private const string Password = "StrongPass123!";
    private const string NewPassword = "EvenStronger456?";

    [Fact]
    public async Task RegistrationRequiresEmailConfirmationBeforeSignIn()
    {
        await using var factory = new EmailApiFactory();
        using var client = factory.CreateClient();

        var registered = await Post(client, "/api/auth/register", new { email = "new@example.test", password = Password });
        Assert.Equal(HttpStatusCode.Accepted, registered.StatusCode);
        Assert.False(registered.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(value => value.Contains("DollarShark.Session")));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);

        var blocked = await Post(client, "/api/auth/login", new { email = "new@example.test", password = Password });
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Contains("email_not_confirmed", await blocked.Content.ReadAsStringAsync());

        var email = await factory.Emails.WaitFor("new@example.test", "Confirm");
        var (userId, token) = ReadLink(email, "/confirm-email");
        Assert.DoesNotContain("@", userId);

        var confirmed = await Post(client, "/api/auth/confirm-email", new { userId, token });
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);

        var login = await Post(client, "/api/auth/login", new { email = "new@example.test", password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task UnconfirmedStatusIsOnlyRevealedAfterACorrectPassword()
    {
        await using var factory = new EmailApiFactory();
        using var client = factory.CreateClient();
        await Post(client, "/api/auth/register", new { email = "quiet@example.test", password = Password });

        var wrongPassword = await Post(client, "/api/auth/login", new { email = "quiet@example.test", password = "WrongPassword123!" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.DoesNotContain("confirm", await wrongPassword.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConfirmationRejectsTamperedTokensAndResendSendsANewLink()
    {
        await using var factory = new EmailApiFactory();
        using var client = factory.CreateClient();
        await Post(client, "/api/auth/register", new { email = "resend@example.test", password = Password });
        var (userId, _) = ReadLink(await factory.Emails.WaitFor("resend@example.test", "Confirm"), "/confirm-email");

        var tampered = await Post(client, "/api/auth/confirm-email", new { userId, token = "bm90LWEtdG9rZW4" });
        Assert.Equal(HttpStatusCode.BadRequest, tampered.StatusCode);

        var resent = await Post(client, "/api/auth/resend-confirmation", new { email = "resend@example.test" });
        Assert.Equal(HttpStatusCode.Accepted, resent.StatusCode);
        await factory.Emails.WaitForCount("resend@example.test", 2);

        var unknown = await Post(client, "/api/auth/resend-confirmation", new { email = "nobody@example.test" });
        Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
    }

    [Fact]
    public async Task ForgotAndResetPasswordWorkWithoutRevealingWhetherAnAccountExists()
    {
        await using var factory = new EmailApiFactory();
        using var client = factory.CreateClient();
        await Post(client, "/api/auth/register", new { email = "reset@example.test", password = Password });

        var unknown = await Post(client, "/api/auth/forgot-password", new { email = "missing@example.test" });
        var known = await Post(client, "/api/auth/forgot-password", new { email = "reset@example.test" });
        Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, known.StatusCode);
        Assert.Equal(await unknown.Content.ReadAsStringAsync(), await known.Content.ReadAsStringAsync());

        var (userId, token) = ReadLink(await factory.Emails.WaitFor("reset@example.test", "Reset"), "/reset-password");
        Assert.DoesNotContain(factory.Emails.Sent, message => message.To == "missing@example.test");

        var weak = await Post(client, "/api/auth/reset-password", new { userId, token, password = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);

        var reset = await Post(client, "/api/auth/reset-password", new { userId, token, password = NewPassword });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        // The reset token is single-use (the security stamp changed), the old password no longer works,
        // and the reset proved inbox ownership so the account can sign in without a separate confirmation.
        var reused = await Post(client, "/api/auth/reset-password", new { userId, token, password = "AnotherOne789#" });
        Assert.Equal(HttpStatusCode.BadRequest, reused.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(client, "/api/auth/login", new { email = "reset@example.test", password = Password })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Post(client, "/api/auth/login", new { email = "reset@example.test", password = NewPassword })).StatusCode);
    }

    [Fact]
    public async Task EmailSendingEndpointsAreRateLimited()
    {
        await using var factory = new EmailApiFactory(permitLimit: 3);
        using var client = factory.CreateClient();
        for (var attempt = 0; attempt < 3; attempt++)
            Assert.Equal(HttpStatusCode.Accepted, (await Post(client, "/api/auth/forgot-password", new { email = "flood@example.test" })).StatusCode);

        var limited = await Post(client, "/api/auth/forgot-password", new { email = "flood@example.test" });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Contains("Too many requests", await limited.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SignInAttemptsAreRateLimitedPerClientEvenWithTheRightPassword()
    {
        await using var factory = new EmailApiFactory(loginPermitLimit: 3);
        using var client = factory.CreateClient();
        await Post(client, "/api/auth/register", new { email = "limited@example.test", password = Password });
        var (userId, token) = ReadLink(await factory.Emails.WaitFor("limited@example.test", "Confirm"), "/confirm-email");
        await Post(client, "/api/auth/confirm-email", new { userId, token });

        // Guesses spread across different accounts: lockout alone would never trigger here.
        foreach (var email in new[] { "a@example.test", "b@example.test", "c@example.test" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await Post(client, "/api/auth/login", new { email, password = "Guess123!abc" })).StatusCode);

        var limited = await Post(client, "/api/auth/login", new { email = "limited@example.test", password = Password });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(int.Parse(limited.Headers.GetValues("Retry-After").Single(), System.Globalization.CultureInfo.InvariantCulture) > 0);
        Assert.Contains("Please wait about", await limited.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);

        // Other endpoints are not affected by the sign-in limit.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/csrf")).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await Post(client, "/api/auth/forgot-password", new { email = "limited@example.test" })).StatusCode);
    }

    [Fact]
    public async Task LogEmailProviderIsRefusedOutsideDevelopment()
    {
        await using var factory = new EmailApiFactory(environment: "Production");
        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains("Log email provider", exception.ToString());
    }

    private static (string UserId, string Token) ReadLink(EmailMessage email, string path)
    {
        var start = email.TextBody.IndexOf("http", StringComparison.Ordinal);
        var end = email.TextBody.IndexOfAny([' ', '\n', '\r'], start);
        var uri = new Uri(email.TextBody[start..(end < 0 ? email.TextBody.Length : end)]);
        Assert.Equal(path, uri.AbsolutePath);
        Assert.Equal("https://app.example.test", uri.GetLeftPart(UriPartial.Authority));
        var query = QueryHelpers.ParseQuery(uri.Query);
        Assert.Contains(path, email.HtmlBody);
        return (query["userId"].ToString(), query["token"].ToString());
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, string path, object body)
    {
        var csrfResponse = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", csrfResponse.GetProperty("requestToken").GetString());
        return await client.SendAsync(request);
    }

    private sealed class CapturingEmailSender : IEmailSender
    {
        public ConcurrentQueue<EmailMessage> Sent { get; } = new();

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            Sent.Enqueue(message);
            return Task.CompletedTask;
        }

        public async Task<EmailMessage> WaitFor(string to, string subjectContains)
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                var match = Sent.LastOrDefault(message => message.To == to && message.Subject.Contains(subjectContains, StringComparison.OrdinalIgnoreCase));
                if (match is not null) return match;
                await Task.Delay(50);
            }
            throw new TimeoutException($"No \"{subjectContains}\" email was sent to {to}.");
        }

        public async Task WaitForCount(string to, int count)
        {
            for (var attempt = 0; attempt < 100 && Sent.Count(message => message.To == to) < count; attempt++)
                await Task.Delay(50);
            Assert.Equal(count, Sent.Count(message => message.To == to));
        }
    }

    private sealed class EmailApiFactory(string environment = "Development", int permitLimit = 1000, int loginPermitLimit = 1000) : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection connection = OpenConnection();
        private int initialized;
        public CapturingEmailSender Emails { get; } = new();

        private static SqliteConnection OpenConnection()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();
            return connection;
        }

        public new HttpClient CreateClient()
        {
            var client = base.CreateClient();
            if (Interlocked.Exchange(ref initialized, 1) == 0)
            {
                using var scope = Services.CreateScope();
                scope.ServiceProvider.GetRequiredService<FinanceDbContext>().Database.EnsureCreated();
            }
            return client;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.UseSetting("FrontendOrigin", "https://app.example.test");
            builder.UseSetting("ViteOrigin", "https://app.example.test");
            builder.UseSetting("Email:Provider", "Log");
            builder.UseSetting("RateLimiting:AuthEmail:PermitLimit", permitLimit.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.UseSetting("RateLimiting:Login:PermitLimit", loginPermitLimit.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.ConfigureServices(services =>
            {
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.RemoveAll<IDbContextOptionsConfiguration<FinanceDbContext>>();
                services.RemoveAll<DbContextOptions<FinanceDbContext>>();
                services.RemoveAll<FinanceDbContext>();
                services.AddDbContext<FinanceDbContext>(options => options.UseSqlite(connection));
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(Emails);
            });
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
