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

public sealed class TransferIntegrationTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task LinksAndUnlinksTransferPreservingAmountsAndApplyingAccountBalances()
    {
        await using var database = await TestDatabase.CreateAsync();
        var repository = new FinanceRepository(database.Context, new TestCurrentUser());
        var accounts = new AccountService(repository);
        var transactions = new TransactionService(repository);
        var transfers = new TransferService(repository);
        var dashboard = new DashboardRepository(database.Context, new TestCurrentUser());
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var openingDate = date.AddDays(-1);

        var chequing = (await accounts.CreateAsync(new AccountRequest("Chequing", "North Bank", "Chequing", "CAD", null, 5000m, openingDate), Ct)).Value!;
        var card = (await accounts.CreateAsync(new AccountRequest("Card", "Card Co", "Credit Card", "CAD", null, 5000m, openingDate), Ct)).Value!;
        var outgoing = await transactions.CreateAsync(new TransactionRequest(date, "Card payment", -1000m, "Transfer", chequing.Id, null), Ct);
        var incoming = await transactions.CreateAsync(new TransactionRequest(date, "Payment received", 1000m, "Transfer", card.Id, null), Ct);
        var ordinaryIncome = await transactions.CreateAsync(new TransactionRequest(date, "Salary", 50m, "Income", chequing.Id, null), Ct);
        var ordinaryExpense = await transactions.CreateAsync(new TransactionRequest(date, "Coffee", -20m, "Expense", chequing.Id, null), Ct);
        Assert.True(outgoing.IsSuccess);
        Assert.True(incoming.IsSuccess);

        var suggestions = await transfers.GetSuggestionsAsync(Ct);
        var suggestion = Assert.Single(suggestions.Value!);
        Assert.Equal(outgoing.Value!.Id, suggestion.Outgoing.Id);
        Assert.Equal(incoming.Value!.Id, suggestion.Incoming.Id);
        Assert.Null((await transactions.GetAsync(outgoing.Value.Id, Ct)).Value!.LinkedTransferTransactionId);

        var linked = await transfers.LinkAsync(new TransferLinkRequest(outgoing.Value.Id, incoming.Value!.Id), Ct);
        Assert.Equal(200, linked.StatusCode);
        Assert.Equal(outgoing.Value.Id, linked.Value!.Outgoing.Id);
        Assert.Equal(incoming.Value.Id, linked.Value.Incoming.Id);
        var outgoingAfterLink = (await transactions.GetAsync(outgoing.Value.Id, Ct)).Value!;
        var incomingAfterLink = (await transactions.GetAsync(incoming.Value.Id, Ct)).Value!;
        Assert.Equal(incoming.Value.Id, outgoingAfterLink.LinkedTransferTransactionId);
        Assert.Equal(outgoing.Value.Id, incomingAfterLink.LinkedTransferTransactionId);
        Assert.Equal(incoming.Value.Id, outgoingAfterLink.LinkedTransferCounterpart!.Id);
        Assert.Equal(-1000m, outgoingAfterLink.Amount);
        Assert.Equal(1000m, incomingAfterLink.Amount);

        var balances = (await accounts.ListAsync(false, Ct)).Value!;
        Assert.Equal(4030m, Assert.Single(balances, x => x.Id == chequing.Id).CalculatedBalance);
        Assert.Equal(4000m, Assert.Single(balances, x => x.Id == card.Id).CalculatedBalance);
        var totals = await dashboard.GetTotalsAsync(date, date, Ct);
        var total = Assert.Single(totals);
        Assert.Equal(50m, total.Income);
        Assert.Equal(20m, total.Expenses);
        Assert.Equal(30m, total.NetCashFlow);

        var linkedEdit = await transactions.UpdateAsync(outgoing.Value.Id,
            new TransactionRequest(date, "Changed", -999m, "Transfer", chequing.Id, null), Ct);
        Assert.Equal(409, linkedEdit.StatusCode);
        Assert.Equal(409, (await transactions.DeleteAsync(outgoing.Value.Id, Ct)).StatusCode);

        Assert.Equal(204, (await transfers.UnlinkAsync(incoming.Value.Id, Ct)).StatusCode);
        Assert.Null((await transactions.GetAsync(outgoing.Value.Id, Ct)).Value!.LinkedTransferTransactionId);
        Assert.Null((await transactions.GetAsync(incoming.Value.Id, Ct)).Value!.LinkedTransferTransactionId);
        Assert.Equal(-1000m, (await transactions.GetAsync(outgoing.Value.Id, Ct)).Value!.Amount);
        balances = (await accounts.ListAsync(false, Ct)).Value!;
        Assert.Equal(6000m, Assert.Single(balances, x => x.Id == card.Id).CalculatedBalance);
    }

    [Fact]
    public async Task TransferLinkingValidatesOwnershipAccountsCurrencyAmountsDirectionsAndExistingPairs()
    {
        await using var database = await TestDatabase.CreateAsync();
        var ownerRepository = new FinanceRepository(database.Context, new TestCurrentUser());
        var accounts = new AccountService(ownerRepository);
        var transactions = new TransactionService(ownerRepository);
        var transfers = new TransferService(ownerRepository);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var cadA = (await accounts.CreateAsync(new AccountRequest("A", "Bank", "Chequing", "CAD", null), Ct)).Value!;
        var cadB = (await accounts.CreateAsync(new AccountRequest("B", "Bank", "Savings", "CAD", null), Ct)).Value!;
        var usd = (await accounts.CreateAsync(new AccountRequest("USD", "Broker", "Investment", "USD", null), Ct)).Value!;

        async Task<TransactionDto> Add(Guid accountId, decimal amount, string type = "Transfer") =>
            (await transactions.CreateAsync(new TransactionRequest(today, "Move funds", amount, type, accountId, null), Ct)).Value!;

        var out100 = await Add(cadA.Id, -100m);
        var in100SameAccount = await Add(cadA.Id, 100m);
        var in100B = await Add(cadB.Id, 100m);
        var in90 = await Add(cadB.Id, 90m);
        var out100Other = await Add(cadB.Id, -100m);
        var out100Usd = await Add(usd.Id, -100m);
        var in100Usd = await Add(usd.Id, 100m);
        var in100Expense = await Add(cadB.Id, 100m, "Income");

        Assert.Equal(400, (await transfers.LinkAsync(new TransferLinkRequest(out100.Id, in100SameAccount.Id), Ct)).StatusCode);
        Assert.Equal(400, (await transfers.LinkAsync(new TransferLinkRequest(out100.Id, in90.Id), Ct)).StatusCode);
        Assert.Equal(400, (await transfers.LinkAsync(new TransferLinkRequest(out100.Id, out100Other.Id), Ct)).StatusCode);
        Assert.Equal(400, (await transfers.LinkAsync(new TransferLinkRequest(out100.Id, in100Usd.Id), Ct)).StatusCode);
        Assert.Equal(400, (await transfers.LinkAsync(new TransferLinkRequest(out100.Id, in100Expense.Id), Ct)).StatusCode);
        Assert.Equal(400, (await transfers.LinkAsync(new TransferLinkRequest(out100.Id, out100.Id), Ct)).StatusCode);
        Assert.Equal(400, (await transfers.LinkAsync(new TransferLinkRequest(out100.Id, in100SameAccount.Id), Ct)).StatusCode);
        Assert.Equal(400, (await transfers.LinkAsync(new TransferLinkRequest(out100.Id, in100Usd.Id), Ct)).StatusCode);
        Assert.Equal(200, (await transfers.LinkAsync(new TransferLinkRequest(out100.Id, in100B.Id), Ct)).StatusCode);
        var conflictingIncoming = await Add(cadB.Id, 100m);
        Assert.Equal(409, (await transfers.LinkAsync(new TransferLinkRequest(out100.Id, conflictingIncoming.Id), Ct)).StatusCode);

        var validOutgoing = await Add(cadA.Id, -250m);
        var validIncoming = await Add(cadB.Id, 250m);
        Assert.Equal(200, (await transfers.LinkAsync(new TransferLinkRequest(validOutgoing.Id, validIncoming.Id), Ct)).StatusCode);
        var anotherIncoming = await Add(cadB.Id, 250m);
        Assert.Equal(409, (await transfers.LinkAsync(new TransferLinkRequest(validOutgoing.Id, anotherIncoming.Id), Ct)).StatusCode);

        database.Context.Users.Add(new ApplicationUser
        {
            Id = "other-transfer-user", UserName = "other-transfer-user", NormalizedUserName = "OTHER-TRANSFER-USER",
            Email = "other-transfer-user@example.test", NormalizedEmail = "OTHER-TRANSFER-USER@EXAMPLE.TEST", EmailConfirmed = true
        });
        await database.Context.SaveChangesAsync();
        var otherUserTransfers = new TransferService(new FinanceRepository(database.Context, new TestCurrentUser("other-transfer-user")));
        Assert.Equal(404, (await otherUserTransfers.LinkAsync(new TransferLinkRequest(validOutgoing.Id, validIncoming.Id), Ct)).StatusCode);
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
                UserName = TestCurrentUser.Id,
                NormalizedUserName = TestCurrentUser.Id.ToUpperInvariant(),
                Email = "transfer-test@example.test",
                NormalizedEmail = "TRANSFER-TEST@EXAMPLE.TEST",
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

    private sealed class TestCurrentUser(string? userId = null) : ICurrentUserContext
    {
        public const string Id = "transfer-integration-test-user";
        public string UserId => userId ?? Id;
    }
}
