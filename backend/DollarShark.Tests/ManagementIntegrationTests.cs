using DollarShark.Application.Accounts;
using DollarShark.Application.Categories;
using DollarShark.Application.Common;
using DollarShark.Application.Transactions;
using DollarShark.Domain;
using DollarShark.Infrastructure.Identity;
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
        var service = new AccountService(new FinanceRepository(db.Context, new TestCurrentUser()));

        var invalid = await service.CreateAsync(new AccountRequest("", "", "Crypto", "CAD", "123"), Ct);
        Assert.Equal(400, invalid.StatusCode);
        Assert.NotNull(invalid.Errors);

        var created = await service.CreateAsync(new AccountRequest("Daily", "North Bank", "Chequing", "cad", "1234"), Ct);
        Assert.Equal(201, created.StatusCode);
        Assert.NotNull(created.Value);
        Assert.Equal("CAD", created.Value.Currency);
        Assert.Equal("Asset", created.Value.Classification);

        var savings = await service.CreateAsync(new AccountRequest("Reserve", "North Bank", "Savings", "CAD", null), Ct);
        var creditCard = await service.CreateAsync(new AccountRequest("Rewards", "Card Co", "Credit Card", "CAD", null), Ct);
        var investment = await service.CreateAsync(new AccountRequest("Portfolio", "Wealthwise", "Investment", "CAD", null), Ct);
        Assert.Equal(201, savings.StatusCode);
        Assert.Equal("Asset", savings.Value!.Classification);
        Assert.Equal(201, creditCard.StatusCode);
        Assert.Equal("Liability", creditCard.Value!.Classification);
        Assert.Equal(201, investment.StatusCode);
        Assert.Equal("Asset", investment.Value!.Classification);

        var investmentAccount = investment.Value!;
        var editedInvestment = await service.UpdateAsync(investmentAccount.Id,
            new AccountRequest("Long term portfolio", "Wealthwise", "Investment", "CAD", null), Ct);
        Assert.Equal(200, editedInvestment.StatusCode);
        Assert.Equal("Long term portfolio", editedInvestment.Value!.Name);
        Assert.Equal("Investment", editedInvestment.Value.AccountType);
        Assert.Equal("Asset", editedInvestment.Value.Classification);

        var deactivated = await service.SetStatusAsync(created.Value.Id, new AccountStatusRequest(false), Ct);
        Assert.False(deactivated.Value!.IsActive);
        var activeAccounts = (await service.ListAsync(false, Ct)).Value!;
        Assert.Equal(3, activeAccounts.Count);
        Assert.Equal("Liability", Assert.Single(activeAccounts, account => account.AccountType == "Credit Card").Classification);
        Assert.Equal("Asset", Assert.Single(activeAccounts, account => account.AccountType == "Investment").Classification);
        Assert.Equal(4, (await service.ListAsync(true, Ct)).Value!.Count);
    }

    [Fact]
    public async Task CategoryCreateValidatesNamesAndRejectsDuplicates()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new CategoryService(new FinanceRepository(db.Context, new TestCurrentUser()));

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
        var repo = new FinanceRepository(db.Context, new TestCurrentUser());
        var accounts = new AccountService(repo);
        var categories = new CategoryService(repo);
        var transactions = new TransactionService(repo);

        var account = (await accounts.CreateAsync(new AccountRequest("Daily", "North Bank", "Chequing", "CAD", null), Ct)).Value!;
        var card = (await accounts.CreateAsync(new AccountRequest("Rewards", "Card Co", "Credit Card", "CAD", null), Ct)).Value!;
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

        var cardPurchase = await transactions.CreateAsync(
            new TransactionRequest(new DateOnly(2026, 2, 3), "Card purchase", -42.50m, "Expense", card.Id, null), Ct);
        Assert.Equal(201, cardPurchase.StatusCode);
        Assert.Equal(-42.50m, cardPurchase.Value!.Amount);
        Assert.Equal(42.50m, new Transaction { Amount = cardPurchase.Value.Amount, TransactionType = TransactionType.Expense }
            .GetAccountBalanceEffect(AccountClassification.Liability));

        var missingCategory = await transactions.CreateAsync(
            new TransactionRequest(new DateOnly(2026, 2, 2), "Food", -12m, "Expense", account.Id, Guid.NewGuid()), Ct);
        Assert.Equal(400, missingCategory.StatusCode);
    }

    [Fact]
    public void TransactionBalanceEffectsKeepReportingAmountsAndAccountSideBalancesSeparate()
    {
        var salary = new Transaction { Amount = 2500m, TransactionType = TransactionType.Income };
        var purchase = new Transaction { Amount = -42.50m, TransactionType = TransactionType.Expense };
        var refund = new Transaction { Amount = 12m, TransactionType = TransactionType.Refund };
        var transferOut = new Transaction { Amount = -100m, TransactionType = TransactionType.Transfer };
        var transferIn = new Transaction { Amount = 100m, TransactionType = TransactionType.Transfer };

        Assert.Equal(2500m, salary.GetAccountBalanceEffect(AccountClassification.Asset));
        Assert.Equal(-2500m, salary.GetAccountBalanceEffect(AccountClassification.Liability));
        Assert.Equal(-42.50m, purchase.GetAccountBalanceEffect(AccountClassification.Asset));
        Assert.Equal(42.50m, purchase.GetAccountBalanceEffect(AccountClassification.Liability));
        Assert.Equal(12m, refund.GetAccountBalanceEffect(AccountClassification.Asset));
        Assert.Equal(-12m, refund.GetAccountBalanceEffect(AccountClassification.Liability));
        Assert.Equal(-100m, transferOut.GetAccountBalanceEffect(AccountClassification.Asset));
        Assert.Equal(100m, transferIn.GetAccountBalanceEffect(AccountClassification.Asset));
        Assert.Equal(-100m, transferOut.GetAccountBalanceEffect(AccountClassification.Liability));
        Assert.Equal(-42.50m, purchase.Amount);
    }

    [Fact]
    public async Task TransactionFiltersSearchSortAndPaginateInRelationalStore()
    {
        await using var db = await TestDatabase.CreateAsync();
        var repo = new FinanceRepository(db.Context, new TestCurrentUser());
        var accounts = new AccountService(repo);
        var transactions = new TransactionService(repo);
        var account = (await accounts.CreateAsync(new AccountRequest("Daily", "North Bank", "Chequing", "CAD", null), Ct)).Value!;

        await transactions.CreateAsync(new TransactionRequest(new DateOnly(2026, 1, 2), "Coffee beans", -10m, "Expense", account.Id, null,
            SubDescription: "Dark roast 1kg"), Ct);
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

        var subDescriptionSearch = await transactions.ListAsync(new TransactionListQuery { Search = "roast" }, Ct);
        var foundBySubDescription = Assert.Single(subDescriptionSearch.Value!.Items);
        Assert.Equal("Coffee beans", foundBySubDescription.Description);
        Assert.Equal("Dark roast 1kg", foundBySubDescription.SubDescription);
    }

    [Fact]
    public async Task InvalidTransactionDateRangeAndPaginationAreRejected()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new TransactionService(new FinanceRepository(db.Context, new TestCurrentUser()));

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
        var repo = new FinanceRepository(db.Context, new TestCurrentUser());
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
            context.Users.Add(new ApplicationUser
            {
                Id = TestCurrentUser.Id,
                UserName = "integration-test-user",
                NormalizedUserName = "INTEGRATION-TEST-USER",
                Email = "integration-test@example.test",
                NormalizedEmail = "INTEGRATION-TEST@EXAMPLE.TEST",
                EmailConfirmed = true
            });
            await context.SaveChangesAsync();
            return new TestDatabase(connection, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class TestCurrentUser : ICurrentUserContext
    {
        public const string Id = "integration-test-user";
        public string UserId => Id;
    }
}


