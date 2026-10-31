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
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
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
    public async Task AnonymousUsersCannotParseStatements()
    {
        await using var factory = new StatementApiFactory();
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsync($"/api/statements/{Guid.NewGuid()}/parse-csv", new StringContent(string.Empty))).StatusCode);
    }

    [Fact]
    public async Task CsvParsingIsOwnerScopedRepeatableAndDoesNotCreateTransactionsOrChangeStatus()
    {
        await using var factory = new StatementApiFactory();
        using var userA = factory.CreateClient();
        using var userB = factory.CreateClient();
        var (csrfA, accountA) = await RegisterAndCreateAccount(userA, "parse-a@example.test");
        var (csrfB, accountB) = await RegisterAndCreateAccount(userB, "parse-b@example.test");
        var csv = Encoding.UTF8.GetBytes("Date,Description,Amount,Currency\n2026-01-05,\"Coffee, shop\",-4.25,CAD\n");
        var createdCsv = await Upload(userB, accountB, "bank.csv", csv, csrfB);
        var csvStatement = (await createdCsv.Content.ReadFromJsonAsync<StatementDto>())!;
        var createdPdf = await Upload(userA, accountA, "bank.pdf", PdfBytes, csrfA);
        var pdfStatement = (await createdPdf.Content.ReadFromJsonAsync<StatementDto>())!;

        Assert.Equal(HttpStatusCode.NotFound, (await ParseCsv(userA, csvStatement.Id, csrfA)).StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await ParseCsv(userA, pdfStatement.Id, csrfA)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ParseCsv(userB, csvStatement.Id, null)).StatusCode);

        var repeated = await Task.WhenAll(ParseCsv(userB, csvStatement.Id, csrfB), ParseCsv(userB, csvStatement.Id, csrfB));
        Assert.All(repeated, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        using var previewJson = JsonDocument.Parse(await repeated[0].Content.ReadAsStringAsync());
        var preview = previewJson.RootElement;
        Assert.Equal(1, preview.GetProperty("validRows").GetInt32());
        Assert.Equal(0, preview.GetProperty("invalidRows").GetInt32());
        Assert.Equal("Coffee, shop", preview.GetProperty("candidates")[0].GetProperty("description").GetString());
        Assert.Equal(-4.25m, preview.GetProperty("candidates")[0].GetProperty("amount").GetDecimal());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        Assert.Equal(0, await db.Transactions.CountAsync());
        Assert.Equal(StatementImportStatus.Uploaded, (await db.StatementImports.SingleAsync(x => x.Id == csvStatement.Id)).Status);
    }

    [Fact]
    public async Task CsvParsingHonorsConfiguredFileSizeLimit()
    {
        await using var factory = new StatementApiFactory(maxFileSizeBytes: 1_000);
        using var client = factory.CreateClient();
        var (csrf, accountId) = await RegisterAndCreateAccount(client, "parse-size@example.test");
        var csv = Encoding.UTF8.GetBytes("Date,Description,Amount\n2026-01-05,Coffee,-4.25\n");
        var created = await Upload(client, accountId, "bank.csv", csv, csrf);
        var statement = (await created.Content.ReadFromJsonAsync<StatementDto>())!;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            var row = await db.StatementImports.SingleAsync(x => x.Id == statement.Id);
            row.FileSizeBytes = 1_001;
            await db.SaveChangesAsync();
        }

        Assert.Equal((HttpStatusCode)413, (await ParseCsv(client, statement.Id, csrf)).StatusCode);
    }

    [Fact]
    public async Task ConfirmationRevalidatesOwnerRowsCreatesTransactionsAndIsIdempotent()
    {
        await using var factory = new StatementApiFactory();
        using var owner = factory.CreateClient();
        using var other = factory.CreateClient();
        using var anonymous = factory.CreateClient();
        var (csrf, accountId) = await RegisterAndCreateAccount(owner, "import-owner@example.test");
        var (otherCsrf, otherAccountId) = await RegisterAndCreateAccount(other, "import-other@example.test");
        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var csv = Encoding.UTF8.GetBytes($"Date,Description,Sub-description,Amount,Currency,Reference\n{today},Coffee,Original cafe detail,-4.25,CAD,C1\n{today},Paycheck,,120.00,CAD,P1\n{today},Bus,Route 7,-3.00,CAD,B1\n");
        var uploaded = await Upload(owner, accountId, "bank.csv", csv, csrf);
        var statement = (await uploaded.Content.ReadFromJsonAsync<StatementDto>())!;

        var confirmBody = new { candidates = new[] {
            new { sourceRowNumber = 2, selected = true, transactionDate = today, description = "Coffee", subDescription = (string?)"Edited in preview", amount = -4.25m, transactionType = "Expense", categoryId = (Guid?)null },
            new { sourceRowNumber = 3, selected = true, transactionDate = today, description = "Paycheck", subDescription = (string?)null, amount = 120m, transactionType = "Income", categoryId = (Guid?)null },
            new { sourceRowNumber = 4, selected = false, transactionDate = today, description = "Bus", subDescription = (string?)"Route 7", amount = -3m, transactionType = "Expense", categoryId = (Guid?)null }
        }};
        Assert.Equal(HttpStatusCode.Unauthorized, (await ConfirmImport(anonymous, statement.Id, confirmBody, null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ParseCsv(other, statement.Id, otherCsrf)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ConfirmImport(other, statement.Id, confirmBody, otherCsrf)).StatusCode);

        var overviewBefore = await owner.GetFromJsonAsync<JsonElement>("/api/dashboard/overview");
        Assert.Empty(overviewBefore.GetProperty("currencies").EnumerateArray());
        var parsed = await ParseCsv(owner, statement.Id, csrf);
        Assert.True(parsed.StatusCode == HttpStatusCode.OK, await parsed.Content.ReadAsStringAsync());
        using (var previewJson = JsonDocument.Parse(await parsed.Content.ReadAsStringAsync()))
            Assert.Equal("Original cafe detail", previewJson.RootElement.GetProperty("candidates")[0].GetProperty("subDescription").GetString());
        using (var scope = factory.Services.CreateScope())
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<FinanceDbContext>().Transactions.CountAsync());

        var confirmed = await ConfirmImport(owner, statement.Id, confirmBody, csrf);
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        var result = await confirmed.Content.ReadFromJsonAsync<StatementImportConfirmationResult>();
        Assert.Equal(2, result!.CreatedCount);
        Assert.Equal(1, result.ExcludedCount);
        Assert.Equal(HttpStatusCode.Conflict, (await ConfirmImport(owner, statement.Id, confirmBody, csrf)).StatusCode);

        var transactions = await owner.GetFromJsonAsync<TransactionPageResponse>("/api/transactions");
        Assert.Equal(2, transactions!.TotalCount);
        Assert.Contains(transactions.Items, x => x.Amount == -4.25m && x.TransactionType == "Expense" && x.FinancialAccountId == accountId && x.Currency == "CAD");
        Assert.Equal("Edited in preview", transactions.Items.Single(x => x.Description == "Coffee").SubDescription);
        Assert.Null(transactions.Items.Single(x => x.Description == "Paycheck").SubDescription);
        Assert.Contains(transactions.Items, x => x.Amount == 120m && x.TransactionType == "Income");
        var overviewAfter = await owner.GetFromJsonAsync<JsonElement>("/api/dashboard/overview");
        Assert.Contains(overviewAfter.GetProperty("currencies").EnumerateArray(), item => item.GetProperty("expenses").GetDecimal() == 4.25m && item.GetProperty("income").GetDecimal() == 120m);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            Assert.Equal(StatementImportStatus.Processed, (await db.StatementImports.SingleAsync(x => x.Id == statement.Id)).Status);
            var savedTransactions = await db.Transactions.OrderBy(x => x.Description).ToListAsync();
            Assert.Equal("C1", savedTransactions.Single(x => x.Description == "Coffee").ExternalReference);
            Assert.Equal("Edited in preview", savedTransactions.Single(x => x.Description == "Coffee").SubDescription);
            Assert.All(savedTransactions, transaction => Assert.False(transaction.DuplicateWarningOverridden));
        }
        Assert.NotEqual(accountId, otherAccountId);
    }

    [Fact]
    public async Task ConfirmationRejectsEmptyTamperedAndInvalidRowsWithoutSaving()
    {
        await using var factory = new StatementApiFactory();
        using var client = factory.CreateClient();
        var (csrf, accountId) = await RegisterAndCreateAccount(client, "import-invalid@example.test");
        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var csv = Encoding.UTF8.GetBytes($"Date,Description,Amount\n{today},Coffee,-4.25\n");
        var upload = await Upload(client, accountId, "invalid-test.csv", csv, csrf);
        var statement = (await upload.Content.ReadFromJsonAsync<StatementDto>())!;

        Assert.Equal(HttpStatusCode.BadRequest, (await ConfirmImport(client, statement.Id, new { candidates = Array.Empty<object>() }, csrf)).StatusCode);
        var changedSign = new { candidates = new[] { new { sourceRowNumber = 2, selected = true, transactionDate = today, description = "Coffee", amount = 4.25m, transactionType = "Expense", categoryId = (Guid?)null } } };
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ConfirmImport(client, statement.Id, changedSign, csrf)).StatusCode);
        var unknownRow = new { candidates = new[] { new { sourceRowNumber = 42, selected = true, transactionDate = today, description = "Coffee", amount = -4.25m, transactionType = "Expense", categoryId = (Guid?)null } } };
        Assert.Equal(HttpStatusCode.BadRequest, (await ConfirmImport(client, statement.Id, unknownRow, csrf)).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        Assert.Empty(await db.Transactions.ToListAsync());
        Assert.Equal(StatementImportStatus.Uploaded, (await db.StatementImports.SingleAsync(x => x.Id == statement.Id)).Status);
    }

    [Fact]
    public async Task CreditCardConfirmationNormalizesByReviewedTypeAndPreservesTransfers()
    {
        await using var factory = new StatementApiFactory();
        using var client = factory.CreateClient();
        var (csrf, accountId) = await RegisterAndCreateAccount(client, "credit-card-signs@example.test");
        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var csv = Encoding.UTF8.GetBytes($"Date,Description,Amount\n{today},Purchase,42.50\n{today},Refund,-10.00\n{today},Card payment,-100.00\n");
        var upload = await Upload(client, accountId, "card.csv", csv, csrf, statementType: "CreditCard");
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var statement = (await upload.Content.ReadFromJsonAsync<StatementDto>())!;
        Assert.Equal("CreditCard", statement.StatementType);

        var body = new { candidates = new[] {
            new { sourceRowNumber = 2, selected = true, transactionDate = today, description = "Purchase", amount = 42.50m, transactionType = "Expense", categoryId = (Guid?)null },
            new { sourceRowNumber = 3, selected = true, transactionDate = today, description = "Refund", amount = -10m, transactionType = "Refund", categoryId = (Guid?)null },
            new { sourceRowNumber = 4, selected = true, transactionDate = today, description = "Card payment", amount = -100m, transactionType = "Transfer", categoryId = (Guid?)null }
        }};
        var confirmed = await ConfirmImport(client, statement.Id, body, csrf);
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        var transactions = await client.GetFromJsonAsync<TransactionPageResponse>("/api/transactions");
        Assert.Contains(transactions!.Items, x => x.Description == "Purchase" && x.Amount == -42.50m && x.TransactionType == "Expense");
        Assert.Contains(transactions.Items, x => x.Description == "Refund" && x.Amount == 10m && x.TransactionType == "Refund");
        Assert.Contains(transactions.Items, x => x.Description == "Card payment" && x.Amount == -100m && x.TransactionType == "Transfer");
    }

    [Fact]
    public async Task CreditCardDuplicatePreviewUsesNormalizedPurchaseAmount()
    {
        await using var factory = new StatementApiFactory();
        using var client = factory.CreateClient();
        var (csrf, accountId) = await RegisterAndCreateAccount(client, "credit-card-duplicate@example.test");
        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var existing = await PostJson(client, "/api/transactions", new { transactionDate = today, description = "Card purchase", amount = -42.50m, transactionType = "Expense", financialAccountId = accountId, categoryId = (Guid?)null, externalReference = (string?)null }, csrf);
        Assert.Equal(HttpStatusCode.Created, existing.StatusCode);
        var uploaded = await Upload(client, accountId, "card-repeat.csv", Encoding.UTF8.GetBytes($"Date,Description,Amount\n{today},Card purchase,42.50\n"), csrf, statementType: "CreditCard");
        var statement = (await uploaded.Content.ReadFromJsonAsync<StatementDto>())!;
        var parsed = await ParseCsv(client, statement.Id, csrf);
        Assert.Equal(HttpStatusCode.OK, parsed.StatusCode);
        using var json = JsonDocument.Parse(await parsed.Content.ReadAsStringAsync());
        Assert.Equal("LikelyDuplicate", json.RootElement.GetProperty("candidates")[0].GetProperty("duplicateCheck").GetProperty("classification").GetString());
    }

    [Fact]
    public async Task FailedTransactionBatchRollsBackEveryRowAndCanBeRetried()
    {
        await using var factory = new StatementApiFactory();
        using var client = factory.CreateClient();
        var (csrf, accountId) = await RegisterAndCreateAccount(client, "import-rollback@example.test");
        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var csv = Encoding.UTF8.GetBytes($"Date,Description,Amount\n{today},Coffee,-4.25\n{today},Lunch,-9.50\n");
        var upload = await Upload(client, accountId, "rollback.csv", csv, csrf);
        var statement = (await upload.Content.ReadFromJsonAsync<StatementDto>())!;
        var body = new { candidates = new[] {
            new { sourceRowNumber = 2, selected = true, transactionDate = today, description = "Coffee", amount = -4.25m, transactionType = "Expense", categoryId = (Guid?)null },
            new { sourceRowNumber = 3, selected = true, transactionDate = today, description = "Lunch", amount = -9.50m, transactionType = "Expense", categoryId = (Guid?)null }
        }};
        factory.DbFailures.FailTransactionSaveNumber = 2;
        Assert.Equal(HttpStatusCode.InternalServerError, (await ConfirmImport(client, statement.Id, body, csrf)).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            Assert.Empty(await db.Transactions.ToListAsync());
            Assert.Equal(StatementImportStatus.Uploaded, (await db.StatementImports.SingleAsync(x => x.Id == statement.Id)).Status);
        }
        factory.DbFailures.FailTransactionSaveNumber = 0;
        factory.DbFailures.TransactionSaveCalls = 0;
        Assert.Equal(HttpStatusCode.OK, (await ConfirmImport(client, statement.Id, body, csrf)).StatusCode);
    }

    [Fact]
    public async Task ForeignCategoryCannotBeUsedForStatementImport()
    {
        await using var factory = new StatementApiFactory();
        using var owner = factory.CreateClient();
        using var other = factory.CreateClient();
        var (csrf, accountId) = await RegisterAndCreateAccount(owner, "import-category-owner@example.test");
        await RegisterAndCreateAccount(other, "import-category-other@example.test");
        Guid foreignCategoryId;
        using (var scope = factory.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await manager.FindByEmailAsync("import-category-other@example.test");
            var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            var category = new Category { Name = "Private", UserId = user!.Id };
            db.Categories.Add(category); await db.SaveChangesAsync(); foreignCategoryId = category.Id;
        }
        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var statementResponse = await Upload(owner, accountId, "category.csv", Encoding.UTF8.GetBytes($"Date,Description,Amount\n{today},Coffee,-2\n"), csrf);
        var statement = (await statementResponse.Content.ReadFromJsonAsync<StatementDto>())!;
        var body = new { candidates = new[] { new { sourceRowNumber = 2, selected = true, transactionDate = today, description = "Coffee", amount = -2m, transactionType = "Expense", categoryId = foreignCategoryId } } };
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ConfirmImport(owner, statement.Id, body, csrf)).StatusCode);
        using var check = factory.Services.CreateScope();
        Assert.Empty(await check.ServiceProvider.GetRequiredService<FinanceDbContext>().Transactions.ToListAsync());
    }

    [Fact]
    public async Task DuplicatePreviewUsesReferenceAmountCurrencyDateAndNormalizedDescriptionWithinOwnerScope()
    {
        await using var factory = new StatementApiFactory();
        using var owner = factory.CreateClient();
        using var other = factory.CreateClient();
        var (csrf, ownerAccount) = await RegisterAndCreateAccount(owner, "duplicate-owner@example.test");
        var (otherCsrf, otherAccount) = await RegisterAndCreateAccount(other, "duplicate-other@example.test");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var date = today.ToString("yyyy-MM-dd");
        var seed = new
        {
            transactionDate = date, description = "Corner-Mart #7", amount = -12.50m,
            transactionType = "Expense", financialAccountId = ownerAccount, categoryId = (Guid?)null, externalReference = "Bank-Ref-001"
        };
        var ownerTransactionResponse = await PostJson(owner, "/api/transactions", seed, csrf);
        var otherTransactionResponse = await PostJson(other, "/api/transactions", seed with { financialAccountId = otherAccount }, otherCsrf);
        Assert.Equal(HttpStatusCode.Created, ownerTransactionResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, otherTransactionResponse.StatusCode);
        var ownerTransaction = await ownerTransactionResponse.Content.ReadFromJsonAsync<TransactionCreatedDto>();
        var otherTransaction = await otherTransactionResponse.Content.ReadFromJsonAsync<TransactionCreatedDto>();

        var rows = new[]
        {
            $"{date},\" corner mart, #7! \",-12.50,CAD,Bank-Ref-001",
            $"{today.AddDays(-2):yyyy-MM-dd},\" corner mart, #7! \",-12.50,CAD,Other-1",
            $"{today.AddDays(4):yyyy-MM-dd},\"Corner-Mart #7\",-12.50,CAD,Other-2",
            $"{date},\"Corner-Mart #7\",-12.50,USD,Other-3"
        };
        var csv = Encoding.UTF8.GetBytes("Date,Description,Amount,Currency,Reference\n" + string.Join('\n', rows) + "\n");
        var uploaded = await Upload(owner, ownerAccount, "duplicate-preview.csv", csv, csrf);
        var statement = (await uploaded.Content.ReadFromJsonAsync<StatementDto>())!;
        var parsed = await ParseCsv(owner, statement.Id, csrf);
        Assert.True(parsed.StatusCode == HttpStatusCode.OK, await parsed.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await parsed.Content.ReadAsStringAsync());
        var candidates = json.RootElement.GetProperty("candidates").EnumerateArray().ToArray();
        Assert.Equal("LikelyDuplicate", candidates[0].GetProperty("duplicateCheck").GetProperty("classification").GetString());
        Assert.Equal(ownerTransaction!.Id, candidates[0].GetProperty("duplicateCheck").GetProperty("existingTransactionId").GetGuid());
        Assert.NotEqual(otherTransaction!.Id, candidates[0].GetProperty("duplicateCheck").GetProperty("existingTransactionId").GetGuid());
        Assert.Equal("Same external transaction reference on this account.", candidates[0].GetProperty("duplicateCheck").GetProperty("reason").GetString());
        Assert.Equal("Corner-Mart #7", candidates[0].GetProperty("duplicateCheck").GetProperty("existingDescription").GetString());
        Assert.Equal("PossibleDuplicate", candidates[1].GetProperty("duplicateCheck").GetProperty("classification").GetString());
        Assert.Equal(ownerTransaction.Id, candidates[1].GetProperty("duplicateCheck").GetProperty("existingTransactionId").GetGuid());
        Assert.Contains("normalized description", candidates[1].GetProperty("duplicateCheck").GetProperty("reason").GetString());
        Assert.Contains("2 day(s) apart", candidates[1].GetProperty("duplicateCheck").GetProperty("reason").GetString());
        Assert.Equal("NoMatch", candidates[2].GetProperty("duplicateCheck").GetProperty("classification").GetString());
        Assert.Equal("NoMatch", candidates[3].GetProperty("duplicateCheck").GetProperty("classification").GetString());
    }

    [Fact]
    public async Task DuplicateCandidatesCanBeSkippedOverriddenOrLeftUnresolved()
    {
        await using var factory = new StatementApiFactory();
        using var client = factory.CreateClient();
        var (csrf, accountId) = await RegisterAndCreateAccount(client, "duplicate-decisions@example.test");
        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var existing = new { transactionDate = today, description = "Cafe North", amount = -8m, transactionType = "Expense", financialAccountId = accountId, categoryId = (Guid?)null, externalReference = (string?)null };
        Assert.Equal(HttpStatusCode.Created, (await PostJson(client, "/api/transactions", existing, csrf)).StatusCode);

        var cleanCsv = Encoding.UTF8.GetBytes($"Date,Description,Amount,Currency\n{today},\"Cafe North\",-8,CAD\n{today},Books,-20,CAD\n");
        var skipStatement = (await (await Upload(client, accountId, "skip.csv", cleanCsv, csrf)).Content.ReadFromJsonAsync<StatementDto>())!;
        var skipReview = await ReadDuplicateReview(client, skipStatement.Id, csrf);
        var skipBody = new { candidates = new[] {
            new { sourceRowNumber = 2, selected = false, transactionDate = today, description = "Cafe North", amount = -8m, transactionType = "Expense", categoryId = (Guid?)null, duplicateDecision = (string?)"Skip", reviewedDuplicateTransactionId = (Guid?)skipReview.Id, reviewedDuplicateClassification = (string?)skipReview.Classification },
            new { sourceRowNumber = 3, selected = true, transactionDate = today, description = "Books", amount = -20m, transactionType = "Expense", categoryId = (Guid?)null, duplicateDecision = (string?)null, reviewedDuplicateTransactionId = (Guid?)null, reviewedDuplicateClassification = (string?)"NoMatch" }
        }};
        var skipped = await ConfirmImport(client, skipStatement.Id, skipBody, csrf);
        Assert.Equal(HttpStatusCode.OK, skipped.StatusCode);
        var skippedResult = await skipped.Content.ReadFromJsonAsync<StatementImportConfirmationResult>();
        Assert.Equal(1, skippedResult!.CreatedCount);
        Assert.Equal(1, skippedResult.ExcludedCount);

        var overrideStatement = (await (await Upload(client, accountId, "override.csv", Encoding.UTF8.GetBytes($"Date,Description,Amount,Currency\n{today},\"Cafe North\",-8,CAD\n"), csrf)).Content.ReadFromJsonAsync<StatementDto>())!;
        var overrideReview = await ReadDuplicateReview(client, overrideStatement.Id, csrf);
        var overrideBody = new { candidates = new[] { new { sourceRowNumber = 2, selected = true, transactionDate = today, description = "Cafe North", amount = -8m, transactionType = "Expense", categoryId = (Guid?)null, duplicateDecision = "ImportAnyway", reviewedDuplicateTransactionId = overrideReview.Id, reviewedDuplicateClassification = overrideReview.Classification } } };
        Assert.Equal(HttpStatusCode.OK, (await ConfirmImport(client, overrideStatement.Id, overrideBody, csrf)).StatusCode);
        using (var overrideScope = factory.Services.CreateScope())
        {
            var overrideDb = overrideScope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            Assert.True(await overrideDb.Transactions.AnyAsync(x => x.Description == "Cafe North" && x.DuplicateWarningOverridden));
        }

        var skipAllStatement = (await (await Upload(client, accountId, "skip-all.csv", Encoding.UTF8.GetBytes($"Date,Description,Amount,Currency\n{today},\"Cafe North\",-8,CAD\n"), csrf)).Content.ReadFromJsonAsync<StatementDto>())!;
        var skipAllReview = await ReadDuplicateReview(client, skipAllStatement.Id, csrf);
        var skipAllBody = new { candidates = new[] { new { sourceRowNumber = 2, selected = false, transactionDate = today, description = "Cafe North", amount = -8m, transactionType = "Expense", categoryId = (Guid?)null, duplicateDecision = (string?)"Skip", reviewedDuplicateTransactionId = skipAllReview.Id, reviewedDuplicateClassification = (string?)skipAllReview.Classification } } };
        var finalizedSkip = await ConfirmImport(client, skipAllStatement.Id, skipAllBody, csrf);
        Assert.Equal(HttpStatusCode.OK, finalizedSkip.StatusCode);
        Assert.Equal(0, (await finalizedSkip.Content.ReadFromJsonAsync<StatementImportConfirmationResult>())!.CreatedCount);

        var unresolvedStatement = (await (await Upload(client, accountId, "unresolved.csv", Encoding.UTF8.GetBytes($"Date,Description,Amount,Currency\n{today},\"Cafe North\",-8,CAD\n{today},Groceries,-15,CAD\n"), csrf)).Content.ReadFromJsonAsync<StatementDto>())!;
        var unresolvedReview = await ReadDuplicateReview(client, unresolvedStatement.Id, csrf);
        var unresolvedBody = new { candidates = new[] {
            new { sourceRowNumber = 2, selected = false, transactionDate = today, description = "Cafe North", amount = -8m, transactionType = "Expense", categoryId = (Guid?)null, duplicateDecision = (string?)"Unresolved", reviewedDuplicateTransactionId = (Guid?)unresolvedReview.Id, reviewedDuplicateClassification = (string?)unresolvedReview.Classification },
            new { sourceRowNumber = 3, selected = true, transactionDate = today, description = "Groceries", amount = -15m, transactionType = "Expense", categoryId = (Guid?)null, duplicateDecision = (string?)null, reviewedDuplicateTransactionId = (Guid?)null, reviewedDuplicateClassification = (string?)"NoMatch" }
        } };
        var unresolved = await ConfirmImport(client, unresolvedStatement.Id, unresolvedBody, csrf);
        Assert.True(unresolved.StatusCode == HttpStatusCode.UnprocessableEntity,
            $"Expected 422; got {(int)unresolved.StatusCode}: {await unresolved.Content.ReadAsStringAsync()}");
        var tamperedStatement = (await (await Upload(client, accountId, "tampered-decision.csv", Encoding.UTF8.GetBytes($"Date,Description,Amount,Currency\n{today},Fresh Market,-10,CAD\n"), csrf)).Content.ReadFromJsonAsync<StatementDto>())!;
        var tamperedBody = new { candidates = new[] { new { sourceRowNumber = 2, selected = true, transactionDate = today, description = "Fresh Market", amount = -10m, transactionType = "Expense", categoryId = (Guid?)null, duplicateDecision = "ImportAnyway" } } };
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ConfirmImport(client, tamperedStatement.Id, tamperedBody, csrf)).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        Assert.Equal(3, await db.Transactions.CountAsync());
        Assert.Equal(StatementImportStatus.Uploaded, (await db.StatementImports.SingleAsync(x => x.Id == unresolvedStatement.Id)).Status);
    }

    [Fact]
    public async Task ConcurrentConfirmationCreatesTheBatchOnlyOnce()
    {
        await using var factory = new StatementApiFactory();
        using var client = factory.CreateClient();
        var (csrf, accountId) = await RegisterAndCreateAccount(client, "duplicate-concurrent@example.test");
        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var statement = (await (await Upload(client, accountId, "concurrent.csv", Encoding.UTF8.GetBytes($"Date,Description,Amount\n{today},Coffee,-3\n"), csrf)).Content.ReadFromJsonAsync<StatementDto>())!;
        var body = new { candidates = new[] { new { sourceRowNumber = 2, selected = true, transactionDate = today, description = "Coffee", amount = -3m, transactionType = "Expense", categoryId = (Guid?)null, duplicateDecision = (string?)null } } };

        var results = await Task.WhenAll(ConfirmImport(client, statement.Id, body, csrf), ConfirmImport(client, statement.Id, body, csrf));
        Assert.Single(results, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, response => response.StatusCode == HttpStatusCode.Conflict);
        using var scope = factory.Services.CreateScope();
        Assert.Single(await scope.ServiceProvider.GetRequiredService<FinanceDbContext>().Transactions.ToListAsync());
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

    private static async Task<HttpResponseMessage> PostJson(HttpClient client, string path, object body, string? csrf)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        if (csrf is not null) request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> Upload(
        HttpClient client, Guid accountId, string fileName, byte[] bytes, string? csrf, string declaredContentType = "application/octet-stream", string statementType = "Bank")
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(accountId.ToString()), "financialAccountId");
        content.Add(new StringContent(statementType), "statementType");
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

    private static async Task<HttpResponseMessage> ParseCsv(HttpClient client, Guid id, string? csrf)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/statements/{id}/parse-csv")
        {
            Content = new StringContent(string.Empty)
        };
        if (csrf is not null) request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> ConfirmImport(HttpClient client, Guid id, object body, string? csrf) =>
        await PostJson(client, $"/api/statements/{id}/confirm-import", body, csrf);

    private static async Task<DuplicateReviewInfo> ReadDuplicateReview(HttpClient client, Guid id, string csrf)
    {
        var response = await ParseCsv(client, id, csrf);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var duplicate = json.RootElement.GetProperty("candidates")[0].GetProperty("duplicateCheck");
        return new DuplicateReviewInfo(duplicate.GetProperty("existingTransactionId").GetGuid(), duplicate.GetProperty("classification").GetString()!);
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
        long FileSizeBytes, string ContentType, string Status, DateTime UploadedAtUtc, DateTime? ProcessedAtUtc,
        string StatementType = "Bank", int ImportedTransactionCount = 0);
    private sealed record StatementPage(IReadOnlyList<StatementDto> Items, int Page, int PageSize, int TotalCount, int TotalPages);
    private sealed record TransactionResponse(Guid Id, DateOnly TransactionDate, string Description, decimal Amount, string TransactionType, string Currency, Guid FinancialAccountId, string? SubDescription);
    private sealed record TransactionPageResponse(IReadOnlyList<TransactionResponse> Items, int Page, int PageSize, int TotalCount, int TotalPages);
    private sealed record TransactionCreatedDto(Guid Id);
    private sealed record DuplicateReviewInfo(Guid Id, string Classification);

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
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.UseSetting("StatementStorage:RootPath", StorageRoot);
            builder.UseSetting("StatementStorage:MaxFileSizeBytes", maxFileSizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.ConfigureServices(services =>
            {
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
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
        public int FailTransactionSaveNumber { get; set; }
        public int TransactionSaveCalls { get; set; }
    }

    private sealed class SwitchableStatementStorage(
        LocalPrivateStatementStorage inner,
        StatementStorageFailureSwitch failureSwitch) : DollarShark.Application.Statements.IStatementStorage
    {
        public Task<DollarShark.Application.Statements.StatementStorageResult> StoreAsync(Stream content, string extension, long maxFileSizeBytes, CancellationToken cancellationToken) =>
            inner.StoreAsync(content, extension, maxFileSizeBytes, cancellationToken);

        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken) =>
            inner.OpenReadAsync(storageKey, cancellationToken);

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
            var transactionEntries = eventData.Context?.ChangeTracker.Entries<Transaction>();
            if (transactionEntries is not null && transactionEntries.Any(x => x.State == EntityState.Added))
            {
                failureSwitch.TransactionSaveCalls++;
                if (failureSwitch.FailTransactionSaveNumber == failureSwitch.TransactionSaveCalls)
                    throw new DbUpdateException("Injected transaction insert failure.");
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
