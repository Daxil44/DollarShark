using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using DollarShark.Api.Controllers;
using DollarShark.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DollarShark.Tests;

public sealed class GoogleSignInIntegrationTests
{
    private const string Password = "StrongPass123!";

    [Fact]
    public async Task GoogleIsAdvertisedAndChallengesOnlyWhenConfigured()
    {
        await using var configured = new GoogleApiFactory(configureGoogle: true);
        using var client = configured.CreateNoRedirectClient();
        Assert.True((await client.GetFromJsonAsync<JsonElement>("/api/auth/providers")).GetProperty("google").GetBoolean());

        var challenge = await client.GetAsync("/api/auth/external/google");
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        Assert.StartsWith("https://accounts.google.com/", challenge.Headers.Location!.ToString());
        Assert.Contains("signin-google", Uri.UnescapeDataString(challenge.Headers.Location!.ToString()));

        await using var unconfigured = new GoogleApiFactory(configureGoogle: false);
        using var plain = unconfigured.CreateNoRedirectClient();
        Assert.False((await plain.GetFromJsonAsync<JsonElement>("/api/auth/providers")).GetProperty("google").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await plain.GetAsync("/api/auth/external/google")).StatusCode);
    }

    [Fact]
    public async Task NewGoogleUserIsCreatedConfirmedAndSignedIn()
    {
        await using var factory = new GoogleApiFactory();
        using var client = factory.CreateNoRedirectClient();
        factory.Google.Identity = new("google-123", "new.google@example.test", true);

        var callback = await client.GetAsync("/api/auth/external/google/callback");
        Assert.Equal("https://app.example.test/dashboard", callback.Headers.Location!.ToString());
        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal("new.google@example.test", me.GetProperty("email").GetString());

        // Returning user: the stored Google login signs them straight in.
        using var second = factory.CreateNoRedirectClient();
        var again = await second.GetAsync("/api/auth/external/google/callback");
        Assert.Equal("https://app.example.test/dashboard", again.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task GoogleRemovesThePasswordOfAnUnconfirmedPreRegistration()
    {
        await using var factory = new GoogleApiFactory();
        using var attacker = factory.CreateNoRedirectClient();
        Assert.Equal(HttpStatusCode.Accepted, (await Post(attacker, "/api/auth/register", new { email = "victim@example.test", password = Password })).StatusCode);

        using var victim = factory.CreateNoRedirectClient();
        factory.Google.Identity = new("google-victim", "victim@example.test", true);
        var callback = await victim.GetAsync("/api/auth/external/google/callback");
        Assert.Equal("https://app.example.test/dashboard", callback.Headers.Location!.ToString());

        var attackerLogin = await Post(attacker, "/api/auth/login", new { email = "victim@example.test", password = Password });
        Assert.Equal(HttpStatusCode.Unauthorized, attackerLogin.StatusCode);
    }

    [Fact]
    public async Task UnverifiedGoogleEmailIsRejected()
    {
        await using var factory = new GoogleApiFactory();
        using var client = factory.CreateNoRedirectClient();
        factory.Google.Identity = new("google-unverified", "unverified@example.test", false);

        var callback = await client.GetAsync("/api/auth/external/google/callback");
        Assert.Equal("https://app.example.test/login?error=google", callback.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);

        factory.Google.Identity = null;
        var noCookie = await client.GetAsync("/api/auth/external/google/callback");
        Assert.Equal("https://app.example.test/login?error=google", noCookie.Headers.Location!.ToString());
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, string path, object body)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("requestToken").GetString());
        return await client.SendAsync(request);
    }

    public sealed record FakeGoogleIdentity(string Key, string Email, bool Verified);

    public sealed class FakeGoogleState
    {
        public FakeGoogleIdentity? Identity { get; set; }
    }

    /// <summary>Stands in for the external cookie Google's handler would have written after a real round trip.</summary>
    private sealed class FakeExternalCookieHandler(
        IOptionsMonitor<CookieAuthenticationOptions> options, ILoggerFactory logger, UrlEncoder encoder, FakeGoogleState state)
        : AuthenticationHandler<CookieAuthenticationOptions>(options, logger, encoder), IAuthenticationSignInHandler
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (state.Identity is not { } identity) return Task.FromResult(AuthenticateResult.NoResult());
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, identity.Key),
                new Claim(ClaimTypes.Email, identity.Email),
                new Claim(AuthController.GoogleEmailVerifiedClaim, identity.Verified ? "true" : "false"),
            ], AuthController.GoogleScheme));
            var properties = new AuthenticationProperties();
            properties.Items["LoginProvider"] = AuthController.GoogleScheme;
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, properties, Scheme.Name)));
        }

        public Task SignInAsync(ClaimsPrincipal user, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task SignOutAsync(AuthenticationProperties? properties) => Task.CompletedTask;
    }

    private sealed class GoogleApiFactory(bool configureGoogle = true) : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection connection = OpenConnection();
        private int initialized;
        public FakeGoogleState Google { get; } = new();

        private static SqliteConnection OpenConnection()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();
            return connection;
        }

        public HttpClient CreateNoRedirectClient()
        {
            var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            if (Interlocked.Exchange(ref initialized, 1) == 0)
            {
                using var scope = Services.CreateScope();
                scope.ServiceProvider.GetRequiredService<FinanceDbContext>().Database.EnsureCreated();
            }
            return client;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.UseSetting("ViteOrigin", "https://app.example.test");
            builder.UseSetting("RateLimiting:AuthEmail:PermitLimit", "1000");
            if (configureGoogle)
            {
                builder.UseSetting("Authentication:Google:ClientId", "test-client-id");
                builder.UseSetting("Authentication:Google:ClientSecret", "test-client-secret");
            }
            builder.ConfigureServices(services =>
            {
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.RemoveAll<IDbContextOptionsConfiguration<FinanceDbContext>>();
                services.RemoveAll<DbContextOptions<FinanceDbContext>>();
                services.RemoveAll<FinanceDbContext>();
                services.AddDbContext<FinanceDbContext>(options => options.UseSqlite(connection));
                services.AddSingleton(Google);
                services.PostConfigure<AuthenticationOptions>(options =>
                    options.SchemeMap[IdentityConstants.ExternalScheme].HandlerType = typeof(FakeExternalCookieHandler));
            });
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
