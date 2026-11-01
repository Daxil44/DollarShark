using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DollarShark.Infrastructure.Identity;
using DollarShark.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Data.Sqlite;

namespace DollarShark.Tests;

public sealed class AuthenticationIntegrationTests
{
    [Fact]
    public async Task RegistrationLoginMeAndLogoutUseAnAuthenticatedCookieSession()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var csrf = await GetCsrfToken(client);

        var registered = await Post(client, "/api/auth/register", new { email = "user@example.test", password = Password }, csrf);
        Assert.True(registered.StatusCode == HttpStatusCode.OK, await registered.Content.ReadAsStringAsync());
        Assert.Contains(registered.Headers.GetValues("Set-Cookie"), value => value.Contains("DollarShark.Session", StringComparison.Ordinal));
        Assert.Contains("HttpOnly", string.Join(';', registered.Headers.GetValues("Set-Cookie")), StringComparison.OrdinalIgnoreCase);

        var profile = await client.GetFromJsonAsync<UserProfile>("/api/auth/me");
        Assert.Equal("user@example.test", profile!.Email);

        csrf = await GetCsrfToken(client);
        var logout = await Post(client, "/api/auth/logout", new { }, csrf);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);

        var loginCsrf = await GetCsrfToken(client);
        var login = await Post(client, "/api/auth/login", new { email = "user@example.test", password = Password }, loginCsrf);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task RegistrationAndLoginRejectInvalidCredentialsWithoutEmailEnumerationDetails()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var csrf = await GetCsrfToken(client);

        var invalid = await Post(client, "/api/auth/register", new { email = "not-an-email", password = "short" }, csrf);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var created = await Post(client, "/api/auth/register", new { email = "duplicate@example.test", password = Password }, csrf);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        csrf = await GetCsrfToken(client);
        await Post(client, "/api/auth/logout", new { }, csrf);
        csrf = await GetCsrfToken(client);

        var duplicate = await Post(client, "/api/auth/register", new { email = "duplicate@example.test", password = Password }, csrf);
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        var duplicateBody = await duplicate.Content.ReadAsStringAsync();
        Assert.DoesNotContain("already", duplicateBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("exists", duplicateBody, StringComparison.OrdinalIgnoreCase);

        var invalidLogin = await Post(client, "/api/auth/login", new { email = "duplicate@example.test", password = "WrongPassword123!" }, csrf);
        Assert.Equal(HttpStatusCode.Unauthorized, invalidLogin.StatusCode);
        Assert.Contains("incorrect", await invalidLogin.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnonymousRequestsCannotReadOrWriteFinancialData()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/accounts")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/categories")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/transactions")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/dashboard/overview")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/accounts", new { name = "x" })).StatusCode);
    }

    [Fact]
    public async Task MutatingRequestsRequireCsrfTokenAndCookieSettingsAreHttpOnlyAndSameSiteLax()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var csrf = await GetCsrfToken(client);
        await Post(client, "/api/auth/register", new { email = "csrf@example.test", password = Password }, csrf);
        csrf = await GetCsrfToken(client);

        var missingCsrf = await client.PostAsJsonAsync("/api/accounts", AccountBody("No token"));
        Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);

        var allowed = await Post(client, "/api/accounts", AccountBody("With token"), csrf);
        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);

        var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);
        Assert.True(options.Cookie.HttpOnly);
        Assert.Equal(Microsoft.AspNetCore.Http.SameSiteMode.Lax, options.Cookie.SameSite);
        Assert.Equal(Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest, options.Cookie.SecurePolicy);
    }

    [Fact]
    public async Task ProductionSessionCookieRequiresHttps()
    {
        await using var factory = new ApiFactory("Production");
        var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);
        Assert.True(options.Cookie.HttpOnly);
        Assert.Equal(Microsoft.AspNetCore.Http.SameSiteMode.Lax, options.Cookie.SameSite);
        Assert.Equal(Microsoft.AspNetCore.Http.CookieSecurePolicy.Always, options.Cookie.SecurePolicy);
    }

    [Fact]
    public async Task UsersCannotSeeMutateOrReferenceAnotherUsersFinancialRecords()
    {
        await using var factory = new ApiFactory();
        using var userA = factory.CreateClient();
        using var userB = factory.CreateClient();
        var csrfA = await Register(userA, "a@example.test");
        var csrfB = await Register(userB, "b@example.test");

        var accountA = await CreateAccount(userA, csrfA, "A account");
        var categoryA = await CreateCategory(userA, csrfA, "A private");
        var accountB = await CreateAccount(userB, csrfB, "B account");
        var categoryB = await CreateCategory(userB, csrfB, "B private");
        var transactionB = await CreateTransaction(userB, csrfB, accountB.Id, categoryB.Id);
        var accountBUsd = await CreateAccount(userB, csrfB, "B USD", "USD");
        await CreateTransaction(userB, csrfB, accountB.Id, null, "2026-10-01", 100m, "Income", "October salary");
        await CreateTransaction(userB, csrfB, accountB.Id, null, "2026-10-24", 5m, "Refund", "October refund");
        await CreateTransaction(userB, csrfB, accountB.Id, null, "2026-10-12", 200m, "Transfer", "Transfer in");
        await CreateTransaction(userB, csrfB, accountB.Id, null, "2026-10-13", -200m, "Transfer", "Transfer out");
        await CreateTransaction(userB, csrfB, accountB.Id, null, "2026-10-20", -7m, "Expense", "October coffee");
        await CreateTransaction(userB, csrfB, accountB.Id, null, "2026-09-07", 70m, "Income", "September salary");
        await CreateTransaction(userB, csrfB, accountB.Id, null, "2026-09-30", -10m, "Expense", "September expense");
        await CreateTransaction(userB, csrfB, accountBUsd.Id, null, "2026-10-24", 30m, "Income", "USD income");
        await CreateTransaction(userB, csrfB, accountBUsd.Id, null, "2026-10-23", 1.25m, "Refund", "USD refund");

        var dashboardAResponse = await userA.GetAsync("/api/dashboard/overview?from=2026-10-01&to=2026-10-24");
        Assert.True(dashboardAResponse.IsSuccessStatusCode, await dashboardAResponse.Content.ReadAsStringAsync());
        using var dashboardA = JsonDocument.Parse(await dashboardAResponse.Content.ReadAsStringAsync());
        var dashboardACurrency = Assert.Single(dashboardA.RootElement.GetProperty("currencies").EnumerateArray());
        Assert.Equal("CAD", dashboardACurrency.GetProperty("currency").GetString());
        Assert.Equal(0, dashboardACurrency.GetProperty("transactionCount").GetInt32());
        Assert.Empty(dashboardA.RootElement.GetProperty("recentTransactions").EnumerateArray());

        using var dashboardB = JsonDocument.Parse(await (await userB.GetAsync(
            "/api/dashboard/overview?from=2026-10-01&to=2026-10-24&recentLimit=3")).Content.ReadAsStringAsync());
        var dashboardRoot = dashboardB.RootElement;
        var currencyTotals = dashboardRoot.GetProperty("currencies").EnumerateArray().ToArray();
        Assert.Equal(2, currencyTotals.Length);
        var cad = Assert.Single(currencyTotals, item => item.GetProperty("currency").GetString() == "CAD");
        Assert.Equal(105m, cad.GetProperty("income").GetDecimal());
        Assert.Equal(19.5m, cad.GetProperty("expenses").GetDecimal());
        Assert.Equal(85.5m, cad.GetProperty("netCashFlow").GetDecimal());
        Assert.Equal(70m, cad.GetProperty("previousIncome").GetDecimal());
        Assert.Equal(10m, cad.GetProperty("previousExpenses").GetDecimal());
        Assert.Equal(60m, cad.GetProperty("previousNetCashFlow").GetDecimal());
        var usd = Assert.Single(currencyTotals, item => item.GetProperty("currency").GetString() == "USD");
        Assert.Equal(31.25m, usd.GetProperty("income").GetDecimal());
        Assert.Equal(0m, usd.GetProperty("expenses").GetDecimal());
        Assert.Equal(31.25m, usd.GetProperty("netCashFlow").GetDecimal());
        Assert.Equal(0m, usd.GetProperty("previousIncome").GetDecimal());
        var categoryExpenses = dashboardRoot.GetProperty("categoryExpenses").EnumerateArray().ToArray();
        Assert.Equal(2, categoryExpenses.Length);
        var uncategorized = Assert.Single(categoryExpenses, item => item.GetProperty("categoryName").GetString() == "Uncategorized");
        Assert.Equal("CAD", uncategorized.GetProperty("currency").GetString());
        Assert.Equal(7m, uncategorized.GetProperty("amount").GetDecimal());
        var categorized = Assert.Single(categoryExpenses, item => item.GetProperty("categoryName").GetString() == "B private");
        Assert.Equal(12.5m, categorized.GetProperty("amount").GetDecimal());
        var monthlyTotals = dashboardRoot.GetProperty("monthlyTotals").EnumerateArray().ToArray();
        var cadOctober = Assert.Single(monthlyTotals, item => item.GetProperty("currency").GetString() == "CAD");
        Assert.Equal("2026-10", cadOctober.GetProperty("month").GetString());
        Assert.Equal(105m, cadOctober.GetProperty("income").GetDecimal());
        Assert.Equal(19.5m, cadOctober.GetProperty("expenses").GetDecimal());
        var usdOctober = Assert.Single(monthlyTotals, item => item.GetProperty("currency").GetString() == "USD");
        Assert.Equal(31.25m, usdOctober.GetProperty("income").GetDecimal());
        Assert.Equal(0m, usdOctober.GetProperty("expenses").GetDecimal());
        var recent = dashboardRoot.GetProperty("recentTransactions").EnumerateArray().ToArray();
        Assert.Equal(3, recent.Length);
        Assert.Equal("2026-10-24", recent[0].GetProperty("transactionDate").GetString());
        Assert.Equal("2026-10-24", recent[1].GetProperty("transactionDate").GetString());
        Assert.Equal("2026-10-23", recent[2].GetProperty("transactionDate").GetString());
        using var repeatedDashboard = JsonDocument.Parse(await (await userB.GetAsync(
            "/api/dashboard/overview?from=2026-10-01&to=2026-10-24&recentLimit=3")).Content.ReadAsStringAsync());
        var repeatedIds = repeatedDashboard.RootElement.GetProperty("recentTransactions").EnumerateArray()
            .Select(item => item.GetProperty("id").GetString()).ToArray();
        Assert.Equal(recent.Select(item => item.GetProperty("id").GetString()), repeatedIds);
        using var defaultDashboard = JsonDocument.Parse(await (await userB.GetAsync("/api/dashboard/overview?recentLimit=1"))
            .Content.ReadAsStringAsync());
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        Assert.Equal(new DateOnly(today.Year, today.Month, 1).ToString("yyyy-MM-dd"),
            defaultDashboard.RootElement.GetProperty("periodStart").GetString());
        Assert.Equal(new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month)).ToString("yyyy-MM-dd"),
            defaultDashboard.RootElement.GetProperty("periodEnd").GetString());
        Assert.Equal(HttpStatusCode.BadRequest,
            (await userB.GetAsync("/api/dashboard/overview?from=2026-10-01")).StatusCode);

        var accountsA = await userA.GetFromJsonAsync<AccountDto[]>("/api/accounts");
        Assert.Single(accountsA!);
        Assert.Equal(accountA.Id, accountsA![0].Id);

        var categoryListResponse = await userA.GetAsync("/api/categories");
        Assert.True(categoryListResponse.IsSuccessStatusCode, await categoryListResponse.Content.ReadAsStringAsync());
        var categoriesA = await categoryListResponse.Content.ReadFromJsonAsync<CategoryDto[]>();
        Assert.DoesNotContain(categoriesA!, category => category.Id == categoryB.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await userA.GetAsync($"/api/categories/{categoryB.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Put(userA, $"/api/categories/{categoryB.Id}", new { name = "Takeover", parentCategoryId = (Guid?)null }, csrfA)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Delete(userA, $"/api/categories/{categoryB.Id}", csrfA)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await userA.GetAsync($"/api/transactions/{transactionB.Id}")).StatusCode);

        var attemptedUpdate = await Put(userA, $"/api/transactions/{transactionB.Id}", TransactionBody(accountA.Id, categoryA.Id), csrfA);
        Assert.Equal(HttpStatusCode.NotFound, attemptedUpdate.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Delete(userA, $"/api/transactions/{transactionB.Id}", csrfA)).StatusCode);

        var foreignAccount = await Post(userA, "/api/transactions", TransactionBody(accountB.Id, categoryA.Id), csrfA);
        Assert.Equal(HttpStatusCode.BadRequest, foreignAccount.StatusCode);
        var foreignCategory = await Post(userA, "/api/transactions", TransactionBody(accountA.Id, categoryB.Id), csrfA);
        Assert.Equal(HttpStatusCode.BadRequest, foreignCategory.StatusCode);

        var overpost = new { name = "Ownership test", institution = "Bank", accountType = "Savings", currency = "CAD", lastFourDigits = (string?)null, userId = "submitted-owner-id" };
        Assert.Equal(HttpStatusCode.Created, (await Post(userA, "/api/accounts", overpost, csrfA)).StatusCode);
        var accountsB = await userB.GetFromJsonAsync<AccountDto[]>("/api/accounts");
        Assert.Equal(2, accountsB!.Length);
        Assert.Contains(accountsB, account => account.Id == accountB.Id);
        Assert.Contains(accountsB, account => account.Id == accountBUsd.Id);
    }

    private const string Password = "StrongPass123!";

    private static object AccountBody(string name, string currency = "CAD") => new
    {
        name,
        institution = "Test Bank",
        accountType = "Chequing",
        currency,
        lastFourDigits = (string?)null
    };

    private static object TransactionBody(Guid accountId, Guid? categoryId, string date = "2026-10-20",
        decimal amount = -12.5m, string type = "Expense", string description = "Test purchase") => new
    {
        transactionDate = date,
        description,
        amount,
        transactionType = type,
        financialAccountId = accountId,
        categoryId
    };

    private static async Task<string> Register(HttpClient client, string email)
    {
        var token = await GetCsrfToken(client);
        var result = await Post(client, "/api/auth/register", new { email, password = Password }, token);
        if (result.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Registration returned {(int)result.StatusCode}: {await result.Content.ReadAsStringAsync()}");
        return await GetCsrfToken(client);
    }

    private static async Task<AccountDto> CreateAccount(HttpClient client, string csrf, string name, string currency = "CAD")
    {
        var result = await Post(client, "/api/accounts", AccountBody(name, currency), csrf);
        if (!result.IsSuccessStatusCode) throw new InvalidOperationException($"Create account returned {(int)result.StatusCode}: {await result.Content.ReadAsStringAsync()}");
        return (await result.Content.ReadFromJsonAsync<AccountDto>())!;
    }

    private static async Task<CategoryDto> CreateCategory(HttpClient client, string csrf, string name)
    {
        var result = await Post(client, "/api/categories", new { name, parentCategoryId = (Guid?)null }, csrf);
        result.EnsureSuccessStatusCode();
        return (await result.Content.ReadFromJsonAsync<CategoryDto>())!;
    }

    private static async Task<TransactionDto> CreateTransaction(HttpClient client, string csrf, Guid accountId, Guid? categoryId,
        string date = "2026-10-20", decimal amount = -12.5m, string type = "Expense", string description = "Test purchase")
    {
        var result = await Post(client, "/api/transactions", TransactionBody(accountId, categoryId, date, amount, type, description), csrf);
        result.EnsureSuccessStatusCode();
        return (await result.Content.ReadFromJsonAsync<TransactionDto>())!;
    }

    private static async Task<string> GetCsrfToken(HttpClient client)
    {
        var response = await client.GetAsync("/api/auth/csrf");
        var content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"CSRF endpoint returned {(int)response.StatusCode}: {content}");
        return JsonDocument.Parse(content).RootElement.GetProperty("requestToken").GetString()!;
    }

    private static Task<HttpResponseMessage> Post(HttpClient client, string path, object body, string csrf) =>
        Send(client, HttpMethod.Post, path, body, csrf);

    private static Task<HttpResponseMessage> Put(HttpClient client, string path, object body, string csrf) =>
        Send(client, HttpMethod.Put, path, body, csrf);

    private static Task<HttpResponseMessage> Delete(HttpClient client, string path, string csrf) =>
        Send(client, HttpMethod.Delete, path, null, csrf);

    private static async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, object? body, string csrf)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private sealed record UserProfile(string Id, string Email);
    private sealed record AccountDto(Guid Id, string Name);
    private sealed record CategoryDto(Guid Id, string Name, bool IsSystem);
    private sealed record TransactionDto(Guid Id);

    private sealed class ApiFactory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        private readonly string environment;
        private int initialized;

        public ApiFactory(string environment = "Development")
        {
            this.environment = environment;
            connection.Open();
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
            if (environment == "Production")
                builder.UseSetting("FrontendOrigin", "https://app.example.test");
            builder.ConfigureServices(services =>
            {
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.RemoveAll<IDbContextOptionsConfiguration<FinanceDbContext>>();
                services.RemoveAll<DbContextOptions<FinanceDbContext>>();
                services.RemoveAll<FinanceDbContext>();
                services.AddDbContext<FinanceDbContext>(options => options.UseSqlite(connection));
            });
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
