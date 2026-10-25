using DollarShark.Application.Accounts;
using DollarShark.Application.Categories;
using DollarShark.Application.Transactions;
using DollarShark.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DollarShark.Tests;

public sealed class ManagementIntegrationTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task AccountCreationValidatesFieldsAndDeactivationPreservesAccount()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new AccountService(new FinanceRepository(db.Context));

        var invalid = await service.CreateAsync(new AccountRequest("", "", "Investment", "CAD", "123"), Ct);
        Assert.Equal(400, invalid.StatusCode);
        Assert.NotNull(invalid.Errors);

        var created = await service.CreateAsync(new AccountRequest("Daily", "North Bank", "Chequing", "cad", "1234"), Ct);
        Assert.Equal(201, created.StatusCode);
        Assert.NotNull(created.Value);
        Assert.Equal("CAD", created.Value.Currency);

        var deactivated = await service.SetStatusAsync(created.Value.Id, new AccountStatusRequest(false), Ct);
        Assert.False(deactivated.Value!.IsActive);
        Assert.Empty((await service.ListAsync(false, Ct)).Value!);
        Assert.Single((await service.ListAsync(true, Ct)).Value!);
    }

    [Fact]
    public async Task CategoryCreateValidatesNamesAndRejectsDuplicates()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new CategoryService(new FinanceRepository(db.Context));

        var invalid = await service.CreateAsync(new CategoryRequest("  ", null), Ct);
        Assert.Equal(400, invalid.StatusCode);

        var created = await service.CreateAsync(new CategoryRequest("Pets", null), Ct);
        Assert.Equal(201, created.StatusCode);
        var duplicate = await service.CreateAsync(new CategoryRequest("Pets", null), Ct);
        Assert.Equal(400, duplicate.StatusCode);
    }

    [Fact]
    public async Task TransactionRequiresExistingReferencesAndAmountTypeAgreement()
    {
        await using var db = await TestDatabase.CreateAsync();
        var repo = new FinanceRepository(db.Context);
        var accounts = new AccountService(repo);
        var categories = new CategoryService(repo);
        var transactions = new TransactionService(repo);

        var account = (await accounts.CreateAsync(new AccountRequest("Daily", "North Bank", "Chequing", "CAD", null), Ct)).Value!;
        var category = (await categories.CreateAsync(new CategoryRequest("Pets", null), Ct)).Value!;
        var missingAccount = await transactions.CreateAsync(
            new TransactionRequest(new DateOnly(2026, 2, 2), "Food", -12m, "Expense", Guid.NewGuid(), category.Id), Ct);
        Assert.Equal(400, missingAccount.StatusCode);

        var wrongSign = await transactions.CreateAsync(
            new TransactionRequest(new DateOnly(2026, 2, 2), "Salary", -12m, "Income", account.Id, category.Id), Ct);
        Assert.Equal(400, wrongSign.StatusCode);

        var created = await transactions.CreateAsync(
            new TransactionRequest(new DateOnly(2026, 2, 2), "Food", -12.34m, "Expense", account.Id, category.Id), Ct);
        Assert.Equal(201, created.StatusCode);
        Assert.Equal("CAD", created.Value!.Currency);
        Assert.Equal(category.Id, created.Value.CategoryId);

        var missingCategory = await transactions.CreateAsync(
            new TransactionRequest(new DateOnly(2026, 2, 2), "Food", -12m, "Expense", account.Id, Guid.NewGuid()), Ct);
        Assert.Equal(400, missingCategory.StatusCode);
    }

    [Fact]
    public async Task TransactionFiltersSearchSortAndPaginateInRelationalStore()
    {
        await using var db = await TestDatabase.CreateAsync();
        var repo = new FinanceRepository(db.Context);
        var accounts = new AccountService(repo);
        var transactions = new TransactionService(repo);
        var account = (await accounts.CreateAsync(new AccountRequest("Daily", "North Bank", "Chequing", "CAD", null), Ct)).Value!;

        await transactions.CreateAsync(new TransactionRequest(new DateOnly(2026, 1, 2), "Coffee beans", -10m, "Expense", account.Id, null), Ct);
        await transactions.CreateAsync(new TransactionRequest(new DateOnly(2026, 1, 3), "Coffee shop", -15m, "Expense", account.Id, null), Ct);
        await transactions.CreateAsync(new TransactionRequest(new DateOnly(2025, 1, 3), "Bus pass", -8m, "Expense", account.Id, null), Ct);

        var firstPage = await transactions.ListAsync(new TransactionListQuery
        {
            Year = 2026, Month = 1, Search = "Coffee", SortBy = "amount", SortDirection = "asc", Page = 1, PageSize = 1
        }, Ct);
        Assert.True(firstPage.IsSuccess);
        Assert.Equal(2, firstPage.Value!.TotalCount);
        Assert.Equal(2, firstPage.Value.TotalPages);
        Assert.Equal(-15m, Assert.Single(firstPage.Value.Items).Amount);

        var secondPage = await transactions.ListAsync(new TransactionListQuery
        {
            Year = 2026, Month = 1, Search = "Coffee", SortBy = "amount", SortDirection = "asc", Page = 2, PageSize = 1
        }, Ct);
        Assert.Equal(-10m, Assert.Single(secondPage.Value!.Items).Amount);

        var exactDate = await transactions.ListAsync(new TransactionListQuery
        {
            From = new DateOnly(2026, 1, 2), To = new DateOnly(2026, 1, 2)
        }, Ct);
        Assert.Equal("Coffee beans", Assert.Single(exactDate.Value!.Items).Description);
    }

    [Fact]
    public async Task InvalidTransactionDateRangeAndPaginationAreRejected()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new TransactionService(new FinanceRepository(db.Context));

        var dateRange = await service.ListAsync(new TransactionListQuery
        {
            From = new DateOnly(2026, 2, 1), To = new DateOnly(2026, 1, 1)
        }, Ct);
        Assert.Equal(400, dateRange.StatusCode);

        var pagination = await service.ListAsync(new TransactionListQuery { Page = 0, PageSize = 101 }, Ct);
        Assert.Equal(400, pagination.StatusCode);
        Assert.Contains("page", pagination.Errors!.Keys);
        Assert.Contains("pageSize", pagination.Errors.Keys);
    }

    [Fact]
    public async Task CategoryCannotBeDeletedWhileInUseButCanAfterTransactionDeletion()
    {
        await using var db = await TestDatabase.CreateAsync();
        var repo = new FinanceRepository(db.Context);
        var accounts = new AccountService(repo);
        var categories = new CategoryService(repo);
        var transactions = new TransactionService(repo);
        var account = (await accounts.CreateAsync(new AccountRequest("Daily", "North Bank", "Chequing", "CAD", null), Ct)).Value!;
        var category = (await categories.CreateAsync(new CategoryRequest("Pets", null), Ct)).Value!;
        var transaction = (await transactions.CreateAsync(
            new TransactionRequest(new DateOnly(2026, 2, 2), "Pet food", -20m, "Expense", account.Id, category.Id), Ct)).Value!;

        Assert.Equal(409, (await categories.DeleteAsync(category.Id, Ct)).StatusCode);
        Assert.True((await transactions.DeleteAsync(transaction.Id, Ct)).IsSuccess);
        Assert.True((await categories.DeleteAsync(category.Id, Ct)).IsSuccess);
        Assert.Equal(404, (await transactions.GetAsync(transaction.Id, Ct)).StatusCode);
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public FinanceDbContext Context { get; }

        private TestDatabase(SqliteConnection connection, FinanceDbContext context)
        {
            this.connection = connection;
            Context = context;
        }

        public static async Task<TestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<FinanceDbContext>().UseSqlite(connection).Options;
            var context = new FinanceDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new TestDatabase(connection, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}


