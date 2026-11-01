using DollarShark.Application.Accounts;
using DollarShark.Application.Common;
using DollarShark.Application.Dashboard;
using DollarShark.Application.Transactions;
using DollarShark.Application.Transfers;
using DollarShark.Domain;
using DollarShark.Infrastructure.Identity;
using DollarShark.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DollarShark.Tests;

public sealed class DashboardAccountIntegrationTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task AccountOverviewScopesSummariesAndChartsAndKeepsLinkedTransfersVisibleAsActivity()
    {
        await using var db = await TestDatabase.CreateAsync();
        var currentUser = new TestCurrentUser();
        var finance = new FinanceRepository(db.Context, currentUser);
        var accounts = new AccountService(finance);
        var transactions = new TransactionService(finance);
        var dashboardRepository = new DashboardRepository(db.Context, currentUser);
        var dashboard = new DashboardService(dashboardRepository);
        var date = new DateOnly(2026, 10, 15);
        var openingDate = date.AddDays(-5);

        var bank = (await accounts.CreateAsync(new AccountRequest("Daily", "Bank", "Chequing", "CAD", null, 1000m, openingDate), Ct)).Value!;
        var card = (await accounts.CreateAsync(new AccountRequest("Card", "Card Co", "Credit Card", "CAD", null, 0m, openingDate), Ct)).Value!;
        var usd = (await accounts.CreateAsync(new AccountRequest("USD", "Broker", "Investment", "USD", null), Ct)).Value!;
        async Task<TransactionDto> Add(Guid accountId, string description, decimal amount, string type) =>
            (await transactions.CreateAsync(new TransactionRequest(date, description, amount, type, accountId, null), Ct)).Value!;

        await Add(bank.Id, "Salary", 300m, "Income");
        await Add(bank.Id, "Coffee", -50m, "Expense");
        await Add(bank.Id, "Refund", 10m, "Refund");
        await transactions.CreateAsync(new TransactionRequest(date.AddDays(-1), "Earlier income", 500m, "Income", bank.Id, null), Ct);
        var outgoing = await Add(bank.Id, "Card payment", -100m, "Transfer");
        var incoming = await Add(card.Id, "Payment received", 100m, "Transfer");
        await Add(card.Id, "Card purchase", -40m, "Expense");
        await Add(usd.Id, "USD deposit", 900m, "Income");
        Assert.Equal(200, (await new TransferService(finance).LinkAsync(new TransferLinkRequest(outgoing.Id, incoming.Id), Ct)).StatusCode);

        var result = await dashboard.GetOverviewAsync(new DashboardQuery { AccountId = bank.Id, From = date, To = date }, Ct);
        Assert.True(result.IsSuccess);
        var summary = Assert.Single(result.Value!.Currencies);
        Assert.Equal("CAD", summary.Currency);
        Assert.Equal(310m, summary.Income);
        Assert.Equal(50m, summary.Expenses);
        Assert.Equal(260m, summary.NetCashFlow);
        Assert.Equal(310m, summary.MoneyReceived);
        Assert.Equal(150m, summary.MoneySpent);
        Assert.Equal(4, summary.TransactionCount);
        Assert.All(result.Value.RecentTransactions, transaction => Assert.Equal("Daily", transaction.AccountName));
        Assert.Equal(4, Assert.Single(result.Value.MonthlyActivity).TransactionCount);
        Assert.Equal(1, Assert.Single(result.Value.MonthlyActivity).TransferCount);
        Assert.Equal(50m, Assert.Single(result.Value.CategoryExpenses).Amount);
        Assert.Equal(bank.Id, result.Value.AccountId);

        var accountList = (await accounts.ListAsync(false, Ct)).Value!;
        Assert.Equal(1660m, Assert.Single(accountList, account => account.Id == bank.Id).CalculatedBalance);
        var accountTransactions = await transactions.ListAsync(new TransactionListQuery { AccountId = bank.Id, From = date, To = date }, Ct);
        Assert.Equal(4, accountTransactions.Value!.TotalCount);
        Assert.Contains(accountTransactions.Value.Items, transaction => transaction.TransactionType == "Transfer" && transaction.LinkedTransferCounterpart is not null);
    }

    [Fact]
    public async Task AccountOverviewRejectsForeignAndInactiveAccountsAndReturnsZeroCurrencySummaryForEmptyAccount()
    {
        await using var db = await TestDatabase.CreateAsync();
        var owner = new TestCurrentUser();
        var ownerRepository = new FinanceRepository(db.Context, owner);
        var accounts = new AccountService(ownerRepository);
        var empty = (await accounts.CreateAsync(new AccountRequest("Empty", "Bank", "Savings", "CAD", null), Ct)).Value!;
        var inactive = (await accounts.CreateAsync(new AccountRequest("Closed", "Bank", "Chequing", "CAD", null), Ct)).Value!;
        await accounts.SetStatusAsync(inactive.Id, new AccountStatusRequest(false), Ct);
        var foreign = new FinancialAccount
        {
            UserId = "other-dashboard-user", Name = "Private", Institution = "Bank", AccountType = "Chequing", Currency = "USD"
        };
        db.Context.FinancialAccounts.Add(foreign);
        await db.Context.SaveChangesAsync(Ct);

        var dashboard = new DashboardService(new DashboardRepository(db.Context, owner));
        var date = new DateOnly(2026, 10, 1);
        var emptyResult = await dashboard.GetOverviewAsync(new DashboardQuery { AccountId = empty.Id, From = date, To = date }, Ct);
        Assert.True(emptyResult.IsSuccess);
        var summary = Assert.Single(emptyResult.Value!.Currencies);
        Assert.Equal("CAD", summary.Currency);
        Assert.Equal(0m, summary.Income);
        Assert.Equal(0, summary.TransactionCount);
        Assert.Null(Assert.Single((await accounts.ListAsync(false, Ct)).Value!, account => account.Id == empty.Id).CalculatedBalance);
        Assert.Empty(emptyResult.Value.RecentTransactions);
        Assert.Empty(emptyResult.Value.MonthlyActivity);
        Assert.Empty(emptyResult.Value.CategoryExpenses);

        Assert.Equal(404, (await dashboard.GetOverviewAsync(new DashboardQuery { AccountId = foreign.Id }, Ct)).StatusCode);
        Assert.Equal(404, (await dashboard.GetOverviewAsync(new DashboardQuery { AccountId = inactive.Id }, Ct)).StatusCode);
    }

    [Fact]
    public async Task OverallOverviewKeepsCurrencySeparationAndTransferExclusion()
    {
        await using var db = await TestDatabase.CreateAsync();
        var currentUser = new TestCurrentUser();
        var finance = new FinanceRepository(db.Context, currentUser);
        var accounts = new AccountService(finance);
        var transactions = new TransactionService(finance);
        var cad = (await accounts.CreateAsync(new AccountRequest("CAD", "Bank", "Chequing", "CAD", null), Ct)).Value!;
        var usd = (await accounts.CreateAsync(new AccountRequest("USD", "Broker", "Investment", "USD", null), Ct)).Value!;
        var date = new DateOnly(2026, 10, 10);
        await transactions.CreateAsync(new TransactionRequest(date, "Pay", 500m, "Income", cad.Id, null), Ct);
        await transactions.CreateAsync(new TransactionRequest(date, "Move", -75m, "Transfer", cad.Id, null), Ct);
        await transactions.CreateAsync(new TransactionRequest(date, "Deposit", 25m, "Income", usd.Id, null), Ct);

        var dashboard = new DashboardService(new DashboardRepository(db.Context, currentUser));
        var result = await dashboard.GetOverviewAsync(new DashboardQuery { From = date, To = date }, Ct);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.AccountId);
        Assert.Equal(2, result.Value.Currencies.Count);
        Assert.Equal(500m, Assert.Single(result.Value.Currencies, item => item.Currency == "CAD").Income);
        Assert.Equal(25m, Assert.Single(result.Value.Currencies, item => item.Currency == "USD").Income);
        Assert.Equal(3, result.Value.RecentTransactions.Count);
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public FinanceDbContext Context { get; }
        private TestDatabase(SqliteConnection connection, FinanceDbContext context) { this.connection = connection; Context = context; }

        public static async Task<TestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<FinanceDbContext>().UseSqlite(connection).Options;
            var context = new FinanceDbContext(options);
            await context.Database.EnsureCreatedAsync();
            context.Users.Add(new ApplicationUser
            {
                Id = TestCurrentUser.Id, UserName = TestCurrentUser.Id, NormalizedUserName = TestCurrentUser.Id.ToUpperInvariant(),
                Email = "dashboard-test@example.test", NormalizedEmail = "DASHBOARD-TEST@EXAMPLE.TEST", EmailConfirmed = true
            });
            context.Users.Add(new ApplicationUser
            {
                Id = "other-dashboard-user", UserName = "other-dashboard-user", NormalizedUserName = "OTHER-DASHBOARD-USER",
                Email = "other-dashboard@example.test", NormalizedEmail = "OTHER-DASHBOARD@EXAMPLE.TEST", EmailConfirmed = true
            });
            await context.SaveChangesAsync();
            return new TestDatabase(connection, context);
        }

        public async ValueTask DisposeAsync() { await Context.DisposeAsync(); await connection.DisposeAsync(); }
    }

    private sealed class TestCurrentUser : ICurrentUserContext
    {
        public const string Id = "dashboard-integration-test-user";
        public string UserId => Id;
    }
}
