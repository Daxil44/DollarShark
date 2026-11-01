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
        var dashboard = new DashboardService(dashboardRepository, accounts);
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

        var dashboard = new DashboardService(new DashboardRepository(db.Context, owner), accounts);
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

        var dashboard = new DashboardService(new DashboardRepository(db.Context, currentUser), accounts);
        var result = await dashboard.GetOverviewAsync(new DashboardQuery { From = date, To = date }, Ct);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.AccountId);
        Assert.Equal(2, result.Value.Currencies.Count);
        Assert.Equal(500m, Assert.Single(result.Value.Currencies, item => item.Currency == "CAD").Income);
        Assert.Equal(25m, Assert.Single(result.Value.Currencies, item => item.Currency == "USD").Income);
        Assert.Equal(3, result.Value.RecentTransactions.Count);
    }

    [Fact]
    public async Task ConsolidatedOverviewReconcilesTransactionsAndBalancesWithoutCountingTransferSides()
    {
        await using var db = await TestDatabase.CreateAsync();
        var currentUser = new TestCurrentUser();
        var finance = new FinanceRepository(db.Context, currentUser);
        var accounts = new AccountService(finance);
        var transactions = new TransactionService(finance);
        var transfers = new TransferService(finance);
        var dashboard = new DashboardService(new DashboardRepository(db.Context, currentUser), accounts);
        var date = new DateOnly(2026, 10, 20);
        var openingDate = date.AddDays(-1);
        var chequing = (await accounts.CreateAsync(new AccountRequest("Everyday", "Bank", "Chequing", "CAD", null, 1000m, openingDate), Ct)).Value!;
        var savings = (await accounts.CreateAsync(new AccountRequest("Reserve", "Bank", "Savings", "CAD", null, 500m, openingDate), Ct)).Value!;
        var unconfiguredSavings = (await accounts.CreateAsync(new AccountRequest("Unknown reserve", "Bank", "Savings", "CAD", null), Ct)).Value!;
        var card = (await accounts.CreateAsync(new AccountRequest("Rewards", "Card Co", "Credit Card", "CAD", null, 300m, openingDate), Ct)).Value!;
        var investment = (await accounts.CreateAsync(new AccountRequest("Portfolio", "Broker", "Investment", "CAD", null, 200m, openingDate), Ct)).Value!;
        var usd = (await accounts.CreateAsync(new AccountRequest("USD savings", "Bank", "Savings", "USD", null, 50m, openingDate), Ct)).Value!;
        var inactive = (await accounts.CreateAsync(new AccountRequest("Closed", "Bank", "Chequing", "CAD", null, 5000m, openingDate), Ct)).Value!;
        await accounts.SetStatusAsync(inactive.Id, new AccountStatusRequest(false), Ct);
        var foreign = new FinancialAccount
        {
            UserId = "other-dashboard-user", Name = "Private", Institution = "Bank", AccountType = "Chequing",
            Currency = "CAD", OpeningBalance = 10000m, OpeningBalanceDate = openingDate
        };
        db.Context.FinancialAccounts.Add(foreign);
        await db.Context.SaveChangesAsync(Ct);

        async Task<TransactionDto> Add(Guid accountId, string description, decimal amount, string type) =>
            (await transactions.CreateAsync(new TransactionRequest(date, description, amount, type, accountId, null), Ct)).Value!;
        await Add(chequing.Id, "Salary", 1000m, "Income");
        await Add(chequing.Id, "Groceries", -200m, "Expense");
        var investmentOut = await Add(chequing.Id, "Investment contribution", -100m, "Transfer");
        var investmentIn = await Add(investment.Id, "Contribution received", 100m, "Transfer");
        var savingsOut = await Add(chequing.Id, "Savings transfer", -50m, "Transfer");
        var savingsIn = await Add(savings.Id, "Savings deposit", 50m, "Transfer");
        var cardOut = await Add(chequing.Id, "Card payment", -40m, "Transfer");
        var cardIn = await Add(card.Id, "Payment received", 40m, "Transfer");
        await Add(usd.Id, "USD pay", 75m, "Income");
        Assert.Equal(200, (await transfers.LinkAsync(new TransferLinkRequest(investmentOut.Id, investmentIn.Id), Ct)).StatusCode);
        Assert.Equal(200, (await transfers.LinkAsync(new TransferLinkRequest(savingsOut.Id, savingsIn.Id), Ct)).StatusCode);
        Assert.Equal(200, (await transfers.LinkAsync(new TransferLinkRequest(cardOut.Id, cardIn.Id), Ct)).StatusCode);

        var result = await dashboard.GetOverviewAsync(new DashboardQuery { From = date, To = date }, Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Currencies.Count);
        var cad = Assert.Single(result.Value.Currencies, x => x.Currency == "CAD");
        Assert.Equal(1000m, cad.Income);
        Assert.Equal(200m, cad.Expenses);
        Assert.Equal(800m, cad.NetCashFlow);
        Assert.Equal(75m, Assert.Single(result.Value.Currencies, x => x.Currency == "USD").Income);
        Assert.Equal(6, result.Value.Accounts.Count);
        Assert.DoesNotContain(result.Value.Accounts, x => x.Id == inactive.Id || x.Name == "Private");

        var cadBalances = Assert.Single(result.Value.BalanceTotals, x => x.Currency == "CAD");
        Assert.Null(cadBalances.CashBalance);
        Assert.Equal(3, cadBalances.CashAccountCount);
        Assert.Equal(2, cadBalances.CashAccountsWithBalances);
        Assert.Equal(260m, cadBalances.CreditCardLiabilities);
        Assert.Equal(300m, cadBalances.RecordedInvestmentBalances);
        var usdBalances = Assert.Single(result.Value.BalanceTotals, x => x.Currency == "USD");
        Assert.Equal(125m, usdBalances.CashBalance);

        var cashFlow = Assert.Single(result.Value.MonthlyCashFlow, x => x.Currency == "CAD");
        Assert.Equal(1000m, cashFlow.Income);
        Assert.Equal(200m, cashFlow.Expenses);
        Assert.Equal(800m, cashFlow.NetCashFlow);
        Assert.Equal(190m, cashFlow.OutgoingTransfers);
        Assert.Equal(90m, cashFlow.TransfersToOtherAccounts);
        Assert.Equal(100m, cashFlow.InvestmentContributions);
        Assert.Equal(610m, cashFlow.RemainingAfterExpensesAndTransfers);
        Assert.Equal(cashFlow.Income, cashFlow.Expenses + cashFlow.TransfersToOtherAccounts + cashFlow.InvestmentContributions + cashFlow.RemainingAfterExpensesAndTransfers);

        var accountOverview = await dashboard.GetOverviewAsync(new DashboardQuery { AccountId = chequing.Id, From = date, To = date }, Ct);
        var accountTotals = Assert.Single(accountOverview.Value!.Currencies);
        Assert.Equal(cad.Income, accountTotals.Income);
        Assert.Equal(cad.Expenses, accountTotals.Expenses);
        Assert.Equal(cad.NetCashFlow, accountTotals.NetCashFlow);
        Assert.Equal(5, result.Value.AccountBalanceTrends.Count);
        var listedChequing = Assert.Single((await accounts.ListAsync(false, Ct)).Value!, x => x.Id == chequing.Id);
        Assert.Equal(listedChequing.CalculatedBalance, Assert.Single(result.Value.AccountBalanceTrends, x => x.AccountId == chequing.Id).Points[^1].Balance);
        Assert.DoesNotContain(result.Value.AccountBalanceTrends, x => x.AccountId == unconfiguredSavings.Id);
    }

    [Fact]
    public async Task BalanceHistoryAppliesOpeningDateTransfersClassificationAndRequestedIntervals()
    {
        await using var db = await TestDatabase.CreateAsync();
        var currentUser = new TestCurrentUser();
        var finance = new FinanceRepository(db.Context, currentUser);
        var accounts = new AccountService(finance);
        var transactions = new TransactionService(finance);
        var transfers = new TransferService(finance);
        var dashboard = new DashboardService(new DashboardRepository(db.Context, currentUser), accounts);
        var openingDate = new DateOnly(2026, 1, 10);
        var endDate = new DateOnly(2026, 1, 20);
        var bank = (await accounts.CreateAsync(new AccountRequest("Daily", "Bank", "Chequing", "CAD", null, 1000m, openingDate), Ct)).Value!;
        var card = (await accounts.CreateAsync(new AccountRequest("Card", "Card Co", "Credit Card", "CAD", null, 500m, openingDate), Ct)).Value!;
        var usd = (await accounts.CreateAsync(new AccountRequest("USD", "Broker", "Investment", "USD", null, 50m, openingDate), Ct)).Value!;

        async Task<TransactionDto> Add(Guid accountId, DateOnly date, string description, decimal amount, string type) =>
            (await transactions.CreateAsync(new TransactionRequest(date, description, amount, type, accountId, null), Ct)).Value!;
        await Add(bank.Id, openingDate.AddDays(-2), "Before opening", 100m, "Income");
        await Add(bank.Id, openingDate, "On opening date", 100m, "Income");
        await Add(bank.Id, new DateOnly(2026, 1, 12), "Salary", 200m, "Income");
        await Add(bank.Id, new DateOnly(2026, 1, 14), "Purchase", -40m, "Expense");
        var outgoing = await Add(bank.Id, new DateOnly(2026, 1, 15), "Card payment", -100m, "Transfer");
        var incoming = await Add(card.Id, new DateOnly(2026, 1, 15), "Payment received", 100m, "Transfer");
        await Add(card.Id, new DateOnly(2026, 1, 16), "Card purchase", -40m, "Expense");
        await Add(card.Id, new DateOnly(2026, 1, 18), "Card refund", 10m, "Refund");
        await Add(bank.Id, new DateOnly(2026, 1, 18), "Refund", 10m, "Refund");
        await Add(usd.Id, new DateOnly(2026, 1, 18), "Other currency", 900m, "Income");
        Assert.Equal(200, (await transfers.LinkAsync(new TransferLinkRequest(outgoing.Id, incoming.Id), Ct)).StatusCode);

        var daily = (await dashboard.GetAccountBalanceHistoryAsync(new AccountBalanceHistoryQuery
        {
            AccountId = bank.Id, From = openingDate, To = endDate, Interval = "Daily"
        }, Ct)).Value!;
        Assert.True(daily.IsAvailable);
        Assert.Equal("CAD", daily.Currency);
        Assert.Equal(11, daily.Points.Count);
        Assert.Equal(1000m, daily.Points[0].Balance);
        Assert.Equal(1200m, Assert.Single(daily.Points, point => point.Date == new DateOnly(2026, 1, 12)).Balance);
        Assert.Equal(1060m, Assert.Single(daily.Points, point => point.Date == new DateOnly(2026, 1, 15)).Balance);
        Assert.Equal(1070m, daily.Points[^1].Balance);

        var weekly = (await dashboard.GetAccountBalanceHistoryAsync(new AccountBalanceHistoryQuery
        {
            AccountId = bank.Id, From = openingDate, To = endDate, Interval = "Weekly"
        }, Ct)).Value!;
        Assert.Equal(3, weekly.Points.Count);
        Assert.Equal(new DateOnly(2026, 1, 17), weekly.Points[1].Date);
        Assert.Equal(1060m, weekly.Points[1].Balance);
        var monthly = (await dashboard.GetAccountBalanceHistoryAsync(new AccountBalanceHistoryQuery
        {
            AccountId = bank.Id, From = openingDate, To = endDate, Interval = "Monthly"
        }, Ct)).Value!;
        Assert.Equal(2, monthly.Points.Count);
        Assert.Equal(1070m, monthly.Points[^1].Balance);

        var cardHistory = (await dashboard.GetAccountBalanceHistoryAsync(new AccountBalanceHistoryQuery
        {
            AccountId = card.Id, From = openingDate, To = endDate, Interval = "Daily"
        }, Ct)).Value!;
        Assert.Equal("Liability", cardHistory.Classification);
        Assert.Equal(400m, Assert.Single(cardHistory.Points, point => point.Date == new DateOnly(2026, 1, 15)).Balance);
        Assert.Equal(440m, Assert.Single(cardHistory.Points, point => point.Date == new DateOnly(2026, 1, 16)).Balance);
        Assert.Equal(430m, cardHistory.Points[^1].Balance);
    }

    [Fact]
    public async Task BalanceHistoryHandlesMissingOpeningEmptyRangesAndOwnership()
    {
        await using var db = await TestDatabase.CreateAsync();
        var currentUser = new TestCurrentUser();
        var accounts = new AccountService(new FinanceRepository(db.Context, currentUser));
        var unconfigured = (await accounts.CreateAsync(new AccountRequest("Unknown", "Bank", "Savings", "CAD", null), Ct)).Value!;
        var configuredEmpty = (await accounts.CreateAsync(new AccountRequest("Empty", "Bank", "Chequing", "CAD", null, 0m, new DateOnly(2026, 1, 10)), Ct)).Value!;
        var foreign = new FinancialAccount
        {
            UserId = "other-dashboard-user", Name = "Private", Institution = "Bank", AccountType = "Chequing", Currency = "CAD"
        };
        db.Context.FinancialAccounts.Add(foreign);
        await db.Context.SaveChangesAsync(Ct);
        var dashboard = new DashboardService(new DashboardRepository(db.Context, currentUser), accounts);

        var missing = (await dashboard.GetAccountBalanceHistoryAsync(new AccountBalanceHistoryQuery
        {
            AccountId = unconfigured.Id, From = new DateOnly(2026, 1, 1), To = new DateOnly(2026, 1, 5)
        }, Ct)).Value!;
        Assert.False(missing.IsAvailable);
        Assert.Empty(missing.Points);
        var beforeOpening = (await dashboard.GetAccountBalanceHistoryAsync(new AccountBalanceHistoryQuery
        {
            AccountId = configuredEmpty.Id, From = new DateOnly(2026, 1, 1), To = new DateOnly(2026, 1, 5)
        }, Ct)).Value!;
        Assert.True(beforeOpening.IsAvailable);
        Assert.Empty(beforeOpening.Points);
        var empty = (await dashboard.GetAccountBalanceHistoryAsync(new AccountBalanceHistoryQuery
        {
            AccountId = configuredEmpty.Id, From = new DateOnly(2026, 1, 10), To = new DateOnly(2026, 1, 12), Interval = "Daily"
        }, Ct)).Value!;
        Assert.Equal(3, empty.Points.Count);
        Assert.All(empty.Points, point => Assert.Equal(0m, point.Balance));
        Assert.Equal(404, (await dashboard.GetAccountBalanceHistoryAsync(new AccountBalanceHistoryQuery { AccountId = foreign.Id }, Ct)).StatusCode);
        Assert.Equal(400, (await dashboard.GetAccountBalanceHistoryAsync(new AccountBalanceHistoryQuery
        {
            AccountId = configuredEmpty.Id, From = new DateOnly(2026, 2, 1), To = new DateOnly(2026, 1, 1)
        }, Ct)).StatusCode);
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
