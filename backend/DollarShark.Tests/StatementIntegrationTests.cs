using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DollarShark.Domain;
using DollarShark.Application.Statements;
using DollarShark.Infrastructure.Identity;
using DollarShark.Infrastructure.Persistence;
using DollarShark.Infrastructure.Statements;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Data.Sqlite;

namespace DollarShark.Tests;

public sealed class StatementIntegrationTests
{
    private const string Password = "StrongPass123!";
    private static readonly byte[] PdfBytes = Encoding.ASCII.GetBytes("%PDF-1.7\nDemo statement\n%%EOF\n");

    [Fact]
    public async Task AnonymousUsersCannotUploadOrListStatements()
    {
        await using var factory = new StatementApiFactory();
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/statements")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Upload(client, Guid.NewGuid(), "statement.pdf", PdfBytes, null)).StatusCode);
    }

    [Fact]
    public async Task ValidUploadStoresPrivateFileAndOnlySafeMetadata()
    {
        await using var factory = new StatementApiFactory();
        using var client = factory.CreateClient();
        var (csrf, accountId) = await RegisterAndCreateAccount(client, "upload@example.test");

        var response = await Upload(client, accountId, @"..\..\private-bank.pdf", PdfBytes, csrf, "text/plain");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var statement = (await response.Content.ReadFromJsonAsync<StatementDto>())!;
        Assert.Equal("private-bank.pdf", statement.OriginalFileName);
        Assert.Equal(accountId, statement.FinancialAccountId);
        Assert.Equal("Checking", statement.AccountName);
        Assert.Equal(PdfBytes.Length, statement.FileSizeBytes);
        Assert.Equal("application/pdf", statement.ContentType);
        Assert.Equal("Uploaded", statement.Status);
        Assert.DoesNotContain("StorageKey", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        var files = Directory.GetFiles(factory.StorageRoot);
        var storedFile = Assert.Single(files);
        Assert.Matches("^[0-9a-f]{32}\\.pdf$", Path.GetFileName(storedFile));
        Assert.NotEqual("private-bank.pdf", Path.GetFileName(storedFile));
        var webRoot = factory.Services.GetRequiredService<IWebHostEnvironment>().WebRootPath ?? Path.Combine(AppContext.BaseDirectory, "wwwroot");
        Assert.False(Path.GetFullPath(storedFile).StartsWith(Path.GetFullPath(webRoot), StringComparison.OrdinalIgnoreCase));

        using var scope = factory.Services.CreateScope();
        var saved = await scope.ServiceProvider.GetRequiredService<FinanceDbContext>().StatementImports.SingleAsync();
        Assert.Equal(Convert.ToHexString(SHA256.HashData(PdfBytes)).ToLowerInvariant(), saved.FileHash);
        Assert.True(await scope.ServiceProvider.GetRequiredService<FinanceDbContext>().Users.AnyAsync(user => user.Id == saved.UserId));
    }

    [Fact]
    public async Task UsersCannotUploadToAnotherUsersAccountOrReadOrDeleteTheirStatement()
    {
        await using var factory = new StatementApiFactory();
        using var userA = factory.CreateClient();
        using var userB = factory.CreateClient();
        var (csrfA, accountA) = await RegisterAndCreateAccount(userA, "statement-a@example.test");
        var (csrfB, accountB) = await RegisterAndCreateAccount(userB, "statement-b@example.test");

        var foreignUpload = await Upload(userA, accountB, "foreign.pdf", PdfBytes, csrfA);
        Assert.Equal(HttpStatusCode.NotFound, foreignUpload.StatusCode);

        var created = await Upload(userB, accountB, "owned.pdf", PdfBytes, csrfB);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var statement = (await created.Content.ReadFromJsonAsync<StatementDto>())!;

        Assert.Equal(HttpStatusCode.NotFound, (await userA.GetAsync($"/api/statements/{statement.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Delete(userA, statement.Id, csrfA)).StatusCode);
        var listA = await userA.GetFromJsonAsync<StatementPage>("/api/statements");
        Assert.Empty(listA!.Items);
        Assert.Equal(HttpStatusCode.OK, (await userB.GetAsync($"/api/statements/{statement.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await userA.GetAsync("/api/statements")).StatusCode);
        Assert.NotEqual(accountA, accountB);
    }

    [Fact]
    public async Task UploadRejectsUnsupportedExtensionOversizedFilesAndInvalidSignatures()
    {
        await using var factory = new StatementApiFactory(maxFileSizeBytes: 1_000);
        using var client = factory.CreateClient();
        var (csrf, accountId) = await RegisterAndCreateAccount(client, "validate@example.test");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType,
            (await Upload(client, accountId, "bank.txt", PdfBytes, csrf, "application/pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Upload(client, accountId, "fake.pdf", Encoding.UTF8.GetBytes("not a pdf"), csrf)).StatusCode);
        Assert.Equal((HttpStatusCode)413,
            (await Upload(client, accountId, "large.pdf", new byte[1_001], csrf)).StatusCode);

        var csv = Encoding.UTF8.GetBytes("date,description,amount\n2026-10-20,Coffee,-4.25\n");
        Assert.Equal(HttpStatusCode.Created, (await Upload(client, accountId, "statement.csv", csv, csrf)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Upload(client, accountId, "statement.xlsx", CreateMinimalXlsx(), csrf)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Upload(client, accountId, "bad.xlsx", PdfBytes, csrf)).StatusCode);
    }

    [Fact]
    public async Task UploadRequiresCsrfAndSupportsPaginatedStatementHistory()
    {
        await using var factory = new StatementApiFactory();
        using var client = factory.CreateClient();
        var (csrf, accountId) = await RegisterAndCreateAccount(client, "csrf-statement@example.test");

        Assert.Equal(HttpStatusCode.BadRequest, (await Upload(client, accountId, "no-token.pdf", PdfBytes, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Upload(client, accountId, "with-token.pdf", PdfBytes, csrf)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/statements?page=0&pageSize=200")).StatusCode);

        var page = await client.GetFromJsonAsync<StatementPage>("/api/statements?page=1&pageSize=1");
        Assert.Equal(1, page!.Page);
        Assert.Equal(1, page.PageSize);
        Assert.Equal(1, page.TotalCount);
        Assert.Single(page.Items);
    }

    [Fact]
    public async Task FailedFileDeletionIsRetryableAndDoesNotExposeTheStoredFile()
    {
        await using var factory = new StatementApiFactory();
        using var client = factory.CreateClient();
        var (csrf, accountId) = await RegisterAndCreateAccount(client, "delete-file@example.test");
        var created = await Upload(client, accountId, "retry.pdf", PdfBytes, csrf);
        var statement = (await created.Content.ReadFromJsonAsync<StatementDto>())!;
        var path = Assert.Single(Directory.GetFiles(factory.StorageRoot));

        factory.StorageSwitch.FailDelete = true;
        Assert.Equal(HttpStatusCode.InternalServerError, (await Delete(client, statement.Id, csrf)).StatusCode);
        Assert.True(File.Exists(path));
        var pending = await client.GetFromJsonAsync<StatementDto>($"/api/statements/{statement.Id}");
        Assert.Equal("Deleting", pending!.Status);

        factory.StorageSwitch.FailDelete = false;
        Assert.Equal(HttpStatusCode.NoContent, (await Delete(client, statement.Id, csrf)).StatusCode);
        Assert.False(File.Exists(path));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/statements/{statement.Id}")).StatusCode);
    }

    [Fact]
    public async Task DatabaseFailuresCleanUpUploadsAndAllowRetryAfterDeleteFailure()
    {
        await using var factory = new StatementApiFactory();
        using var client = factory.CreateClient();
        var (csrf, accountId) = await RegisterAndCreateAccount(client, "database-failure@example.test");

        factory.DbFailures.FailAdded = true;
        Assert.Equal(HttpStatusCode.InternalServerError,
            (await Upload(client, accountId, "failed-save.pdf", PdfBytes, csrf)).StatusCode);
        Assert.Empty(Directory.GetFiles(factory.StorageRoot));
        var empty = await client.GetFromJsonAsync<StatementPage>("/api/statements");
        Assert.Empty(empty!.Items);

        factory.DbFailures.FailAdded = false;
        var created = await Upload(client, accountId, "retry-save.pdf", PdfBytes, csrf);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var statement = (await created.Content.ReadFromJsonAsync<StatementDto>())!;
        var filePath = Assert.Single(Directory.GetFiles(factory.StorageRoot));

        factory.DbFailures.FailDeleted = true;
        Assert.Equal(HttpStatusCode.InternalServerError, (await Delete(client, statement.Id, csrf)).StatusCode);
        Assert.False(File.Exists(filePath));
        Assert.Equal("Deleting", (await client.GetFromJsonAsync<StatementDto>($"/api/statements/{statement.Id}"))!.Status);

        factory.DbFailures.FailDeleted = false;
        Assert.Equal(HttpStatusCode.NoContent, (await Delete(client, statement.Id, csrf)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/statements/{statement.Id}")).StatusCode);
    }

    private static async Task<(string Csrf, Guid AccountId)> RegisterAndCreateAccount(HttpClient client, string email)
    {
        var csrf = await GetCsrf(client);
        var registration = await PostJson(client, "/api/auth/register", new { email, password = Password }, csrf);
        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);
        csrf = await GetCsrf(client);
        var account = await PostJson(client, "/api/accounts", new
        {
            name = "Checking",
            institution = "Test Bank",
            accountType = "Chequing",
            currency = "CAD",
            lastFourDigits = ""
        }, csrf);
        Assert.Equal(HttpStatusCode.Created, account.StatusCode);
        var accountBody = await account.Content.ReadFromJsonAsync<AccountDto>();
        return (csrf, accountBody!.Id);
    }

    private static async Task<string> GetCsrf(HttpClient client)
    {
        var response = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        return response.GetProperty("requestToken").GetString()!;
    }

    private static async Task<HttpResponseMessage> PostJson(HttpClient client, string path, object body, string csrf)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> Upload(
        HttpClient client, Guid accountId, string fileName, byte[] bytes, string? csrf, string declaredContentType = "application/octet-stream")
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(accountId.ToString()), "financialAccountId");
        var filePart = new ByteArrayContent(bytes);
        filePart.Headers.ContentType = MediaTypeHeaderValue.Parse(declaredContentType);
        content.Add(filePart, "file", fileName);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/statements") { Content = content };
        if (csrf is not null) request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> Delete(HttpClient client, Guid id, string csrf)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/statements/{id}");
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await client.SendAsync(request);
    }

    private static byte[] CreateMinimalXlsx()
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var contentTypes = new StreamWriter(archive.CreateEntry("[Content_Types].xml").Open()))
                contentTypes.Write("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\" />");
            using (var workbook = new StreamWriter(archive.CreateEntry("xl/workbook.xml").Open()))
                workbook.Write("<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" />");
        }
        return output.ToArray();
    }

    private sealed record AccountDto(Guid Id);
    private sealed record StatementDto(
        Guid Id, Guid FinancialAccountId, string AccountName, string OriginalFileName,
        long FileSizeBytes, string ContentType, string Status, DateTime UploadedAtUtc, DateTime? ProcessedAtUtc);
    private sealed record StatementPage(IReadOnlyList<StatementDto> Items, int Page, int PageSize, int TotalCount, int TotalPages);

    private sealed class StatementApiFactory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        private int initialized;
        private readonly long maxFileSizeBytes;
        public string StorageRoot { get; } = Path.Combine(Path.GetTempPath(), "DollarShark-StatementTests", Guid.NewGuid().ToString("N"));
        public StatementStorageFailureSwitch StorageSwitch { get; } = new();
        public StatementDatabaseFailureSwitch DbFailures { get; } = new();

        public StatementApiFactory(long maxFileSizeBytes = 15 * 1024 * 1024)
        {
            this.maxFileSizeBytes = maxFileSizeBytes;
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
            builder.UseEnvironment("Development");
            builder.UseSetting("StatementStorage:RootPath", StorageRoot);
            builder.UseSetting("StatementStorage:MaxFileSizeBytes", maxFileSizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDbContextOptionsConfiguration<FinanceDbContext>>();
                services.RemoveAll<DbContextOptions<FinanceDbContext>>();
                services.RemoveAll<FinanceDbContext>();
                services.AddSingleton(DbFailures);
                services.AddSingleton<StatementDatabaseSaveInterceptor>();
                services.RemoveAll<StatementUploadOptions>();
                services.AddSingleton(new StatementUploadOptions { MaxFileSizeBytes = maxFileSizeBytes });
                services.Configure<StatementStorageOptions>(options => options.RootPath = StorageRoot);
                services.AddDbContext<FinanceDbContext>((provider, options) => options.UseSqlite(connection)
                    .AddInterceptors(provider.GetRequiredService<StatementDatabaseSaveInterceptor>()));
                services.AddSingleton(StorageSwitch);
                services.AddScoped<LocalPrivateStatementStorage>();
                services.RemoveAll<DollarShark.Application.Statements.IStatementStorage>();
                services.AddScoped<DollarShark.Application.Statements.IStatementStorage, SwitchableStatementStorage>();
            });
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            await connection.DisposeAsync();
            if (Directory.Exists(StorageRoot)) Directory.Delete(StorageRoot, recursive: true);
        }
    }

    private sealed class StatementStorageFailureSwitch { public bool FailDelete { get; set; } }
    private sealed class StatementDatabaseFailureSwitch
    {
        public bool FailAdded { get; set; }
        public bool FailDeleted { get; set; }
    }

    private sealed class SwitchableStatementStorage(
        LocalPrivateStatementStorage inner,
        StatementStorageFailureSwitch failureSwitch) : DollarShark.Application.Statements.IStatementStorage
    {
        public Task<DollarShark.Application.Statements.StatementStorageResult> StoreAsync(Stream content, string extension, long maxFileSizeBytes, CancellationToken cancellationToken) =>
            inner.StoreAsync(content, extension, maxFileSizeBytes, cancellationToken);

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
        {
            if (failureSwitch.FailDelete) throw new IOException("Injected file delete failure.");
            return inner.DeleteAsync(storageKey, cancellationToken);
        }
    }

    private sealed class StatementDatabaseSaveInterceptor(StatementDatabaseFailureSwitch failureSwitch) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var entries = eventData.Context?.ChangeTracker.Entries<StatementImport>();
            if (entries is not null && entries.Any(x => x.State == EntityState.Added) && failureSwitch.FailAdded)
                throw new DbUpdateException("Injected statement insert failure.");
            if (entries is not null && entries.Any(x => x.State == EntityState.Deleted) && failureSwitch.FailDeleted)
                throw new DbUpdateException("Injected statement delete failure.");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
