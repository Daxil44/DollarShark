using DollarShark.Application.Common;
using DollarShark.Application.Dashboard;
using DollarShark.Domain;
using Microsoft.EntityFrameworkCore;

namespace DollarShark.Infrastructure.Persistence;

public sealed class DashboardRepository(FinanceDbContext db, ICurrentUserContext currentUser) : IDashboardRepository
{
    private string UserId => string.IsNullOrWhiteSpace(currentUser.UserId)
        ? throw new InvalidOperationException("A signed-in user is required for dashboard data access.")
        : currentUser.UserId;

    public Task<string?> GetActiveAccountCurrencyAsync(Guid accountId, CancellationToken cancellationToken) =>
        db.FinancialAccounts.AsNoTracking()
            .Where(account => account.Id == accountId && account.UserId == UserId && account.IsActive)
            .Select(account => account.Currency)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<DashboardBalanceProfile?> GetBalanceProfileAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var userId = UserId;
        var account = await db.FinancialAccounts.AsNoTracking()
            .Where(x => x.Id == accountId && x.UserId == userId && x.IsActive)
            .Select(x => new { x.Id, x.Name, x.AccountType, x.Currency, x.OpeningBalance, x.OpeningBalanceDate })
            .SingleOrDefaultAsync(cancellationToken);
        if (account is null) return null;
        var classification = account.AccountType == "Credit Card" ? AccountClassification.Liability : AccountClassification.Asset;
        return new DashboardBalanceProfile(account.Id, account.Name, account.AccountType, account.Currency,
            classification, account.OpeningBalance, account.OpeningBalanceDate);
    }

    public async Task<IReadOnlyList<DashboardDailyBalanceMovement>> GetDailyBalanceMovementsAsync(
        Guid accountId, DateOnly openingBalanceDate, DateOnly to, AccountClassification classification,
        CancellationToken cancellationToken)
    {
        var userId = UserId;
        var accountMovements = db.Transactions.AsNoTracking()
            .Where(x => x.UserId == userId && x.FinancialAccountId == accountId &&
                x.TransactionDate > openingBalanceDate && x.TransactionDate <= to);
        var groupedMovements = await accountMovements
            .GroupBy(x => new
            {
                x.TransactionDate,
                x.TransactionType,
                IsLinkedTransfer = x.LinkedTransferTransactionId.HasValue,
                IsPositiveAmount = x.Amount > 0
            })
            .Select(group => new
            {
                group.Key.TransactionDate,
                group.Key.TransactionType,
                group.Key.IsLinkedTransfer,
                Amount = group.Sum(x => x.Amount)
            })
            .OrderBy(x => x.TransactionDate).ThenBy(x => x.TransactionType)
            .ToListAsync(cancellationToken);
        return groupedMovements
            .GroupBy(x => x.TransactionDate)
            .Select(group => new DashboardDailyBalanceMovement(group.Key, group.Sum(x =>
                new Transaction { Amount = x.Amount, TransactionType = x.TransactionType }
                    .GetAccountBalanceEffect(classification, x.IsLinkedTransfer))))
            .OrderBy(x => x.Date)
            .ToArray();
    }

    public async Task<IReadOnlyList<DashboardBalanceMovementAggregate>> GetBalanceMovementAggregatesAsync(
        IReadOnlyCollection<Guid> accountIds, DateOnly fromExclusive, DateOnly to, CancellationToken cancellationToken)
    {
        if (accountIds.Count == 0 || fromExclusive >= to) return [];
        var userId = UserId;
        var aggregates = await db.Transactions.AsNoTracking()
            .Where(x => x.UserId == userId && accountIds.Contains(x.FinancialAccountId) &&
                x.TransactionDate > fromExclusive && x.TransactionDate <= to)
            .GroupBy(x => new
            {
                x.FinancialAccountId,
                x.TransactionDate,
                x.TransactionType,
                IsLinkedTransfer = x.LinkedTransferTransactionId.HasValue,
                IsPositiveAmount = x.Amount > 0
            })
            .Select(group => new
            {
                group.Key.FinancialAccountId,
                group.Key.TransactionDate,
                group.Key.TransactionType,
                group.Key.IsLinkedTransfer,
                Amount = group.Sum(x => x.Amount)
            })
            .OrderBy(x => x.FinancialAccountId).ThenBy(x => x.TransactionDate).ThenBy(x => x.TransactionType)
            .ToListAsync(cancellationToken);
        return aggregates.Select(x => new DashboardBalanceMovementAggregate(x.FinancialAccountId,
            x.TransactionDate, x.TransactionType, x.IsLinkedTransfer, x.Amount)).ToArray();
    }

    public async Task<IReadOnlyList<DashboardTotals>> GetTotalsAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken, Guid? accountId = null)
    {
        var userId = UserId;
        var transactions = db.Transactions.AsNoTracking()
            .Where(transaction => transaction.UserId == userId && transaction.TransactionDate >= from && transaction.TransactionDate <= to)
            .AsQueryable();
        if (accountId is Guid id) transactions = transactions.Where(transaction => transaction.FinancialAccountId == id);
        var totals = await transactions
            .GroupBy(transaction => transaction.Currency)
            .Select(group => new
            {
                Currency = group.Key,
                Income = group.Sum(transaction => transaction.TransactionType != TransactionType.Transfer && transaction.Amount > 0
                    ? transaction.Amount : 0m),
                Expenses = -group.Sum(transaction => transaction.TransactionType != TransactionType.Transfer && transaction.Amount < 0
                    ? transaction.Amount : 0m),
                NetCashFlow = group.Sum(transaction => transaction.TransactionType != TransactionType.Transfer
                    ? transaction.Amount : 0m),
                MoneyReceived = group.Sum(transaction => transaction.Amount > 0 ? transaction.Amount : 0m),
                MoneySpent = -group.Sum(transaction => transaction.Amount < 0 ? transaction.Amount : 0m),
                TransactionCount = group.Count()
            })
            .OrderBy(row => row.Currency)
            .ToListAsync(cancellationToken);

        return totals.Select(row => new DashboardTotals(row.Currency, row.Income, row.Expenses, row.NetCashFlow,
            row.MoneyReceived, row.MoneySpent, row.TransactionCount)).ToArray();
    }

    public async Task<IReadOnlyList<DashboardRecentTransaction>> GetRecentTransactionsAsync(
        DateOnly from, DateOnly to, int limit, CancellationToken cancellationToken, Guid? accountId = null)
    {
        var userId = UserId;
        var transactions = db.Transactions.AsNoTracking()
            .Where(transaction => transaction.UserId == userId && transaction.TransactionDate >= from && transaction.TransactionDate <= to)
            .AsQueryable();
        if (accountId is Guid id) transactions = transactions.Where(transaction => transaction.FinancialAccountId == id);
        return await transactions
            .OrderByDescending(transaction => transaction.TransactionDate)
            .ThenByDescending(transaction => transaction.Id)
            .Take(limit)
            .Select(transaction => new DashboardRecentTransaction(
                transaction.Id,
                transaction.TransactionDate,
                transaction.Description,
                transaction.Amount,
                transaction.Currency,
                transaction.TransactionType.ToString(),
                transaction.FinancialAccount.Name,
                transaction.Category == null ? null : transaction.Category.Name))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DashboardCategoryExpense>> GetCategoryExpensesAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken, Guid? accountId = null)
    {
        var userId = UserId;
        var transactions = db.Transactions.AsNoTracking()
            .Where(x => x.UserId == userId && x.TransactionDate >= from && x.TransactionDate <= to && x.TransactionType != TransactionType.Transfer && x.Amount < 0)
            .AsQueryable();
        if (accountId is Guid id) transactions = transactions.Where(x => x.FinancialAccountId == id);
        var groupedExpenses = await transactions
            .GroupBy(x => new { x.Currency, x.CategoryId })
            .Select(group => new { group.Key.Currency, group.Key.CategoryId, Amount = -group.Sum(x => x.Amount) })
            .ToListAsync(cancellationToken);

        var categoryIds = groupedExpenses.Where(x => x.CategoryId.HasValue).Select(x => x.CategoryId!.Value).Distinct().ToArray();
        var categoryNames = categoryIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await db.Categories.AsNoTracking().Where(x => categoryIds.Contains(x.Id) && (x.IsSystem || x.UserId == userId))
                .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        return groupedExpenses.Select(x => new DashboardCategoryExpense(
                x.Currency,
                x.CategoryId.HasValue && categoryNames.TryGetValue(x.CategoryId.Value, out var categoryName) ? categoryName : "Uncategorized",
                x.Amount))
            .OrderByDescending(x => x.Amount).ThenBy(x => x.CategoryName).ToArray();
    }

    public async Task<IReadOnlyList<DashboardMonthlyTotals>> GetMonthlyTotalsAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken, Guid? accountId = null)
    {
        var userId = UserId;
        var transactions = db.Transactions.AsNoTracking()
            .Where(x => x.UserId == userId && x.TransactionDate >= from && x.TransactionDate <= to && x.TransactionType != TransactionType.Transfer)
            .AsQueryable();
        if (accountId is Guid id) transactions = transactions.Where(x => x.FinancialAccountId == id);
        var monthlyTotals = await transactions
            .GroupBy(x => new { x.TransactionDate.Year, x.TransactionDate.Month, x.Currency })
            .Select(group => new
            {
                group.Key.Year,
                group.Key.Month,
                group.Key.Currency,
                Income = group.Sum(x => x.Amount > 0 ? x.Amount : 0m),
                Expenses = -group.Sum(x => x.Amount < 0 ? x.Amount : 0m)
            })
            .OrderBy(x => x.Year).ThenBy(x => x.Month).ThenBy(x => x.Currency)
            .ToListAsync(cancellationToken);

        return monthlyTotals.Select(x => new DashboardMonthlyTotals(x.Currency, $"{x.Year:D4}-{x.Month:D2}", x.Income, x.Expenses)).ToArray();
    }

    public async Task<IReadOnlyList<DashboardMonthlyCashFlow>> GetMonthlyCashFlowAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken, Guid? accountId = null)
    {
        var userId = UserId;
        var transactions = db.Transactions.AsNoTracking()
            .Where(x => x.UserId == userId && x.TransactionDate >= from && x.TransactionDate <= to)
            .AsQueryable();
        if (accountId is Guid id) transactions = transactions.Where(x => x.FinancialAccountId == id);
        var grouped = await transactions
            .GroupBy(x => new { x.TransactionDate.Year, x.TransactionDate.Month, x.Currency })
            .Select(group => new
            {
                group.Key.Year,
                group.Key.Month,
                group.Key.Currency,
                Income = group.Sum(x => x.TransactionType != TransactionType.Transfer && x.Amount > 0 ? x.Amount : 0m),
                Expenses = -group.Sum(x => x.TransactionType != TransactionType.Transfer && x.Amount < 0 ? x.Amount : 0m),
                OutgoingTransfers = -group.Sum(x => x.TransactionType == TransactionType.Transfer && x.Amount < 0 ? x.Amount : 0m),
                InvestmentContributions = -group.Sum(x => x.TransactionType == TransactionType.Transfer && x.Amount < 0 &&
                    x.LinkedTransferTransaction != null && x.LinkedTransferTransaction.UserId == userId &&
                    x.LinkedTransferTransaction.FinancialAccount.AccountType == "Investment" ? x.Amount : 0m)
            })
            .OrderBy(x => x.Year).ThenBy(x => x.Month).ThenBy(x => x.Currency)
            .ToListAsync(cancellationToken);
        return grouped.Select(x => new DashboardMonthlyCashFlow(x.Currency, $"{x.Year:D4}-{x.Month:D2}",
            x.Income, x.Expenses, x.Income - x.Expenses, x.OutgoingTransfers,
            x.OutgoingTransfers - x.InvestmentContributions, x.InvestmentContributions,
            x.Income - x.Expenses - x.OutgoingTransfers)).ToArray();
    }

    public async Task<IReadOnlyList<DashboardMonthlyActivity>> GetMonthlyActivityAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken, Guid? accountId = null)
    {
        var userId = UserId;
        var transactions = db.Transactions.AsNoTracking()
            .Where(x => x.UserId == userId && x.TransactionDate >= from && x.TransactionDate <= to)
            .AsQueryable();
        if (accountId is Guid id) transactions = transactions.Where(x => x.FinancialAccountId == id);
        var activity = await transactions
            .GroupBy(x => new { x.TransactionDate.Year, x.TransactionDate.Month, x.Currency })
            .Select(group => new
            {
                group.Key.Year,
                group.Key.Month,
                group.Key.Currency,
                TransactionCount = group.Count(),
                IncomeCount = group.Count(x => x.TransactionType == TransactionType.Income),
                ExpenseCount = group.Count(x => x.TransactionType == TransactionType.Expense),
                RefundCount = group.Count(x => x.TransactionType == TransactionType.Refund),
                TransferCount = group.Count(x => x.TransactionType == TransactionType.Transfer)
            })
            .OrderBy(x => x.Year).ThenBy(x => x.Month).ThenBy(x => x.Currency)
            .ToListAsync(cancellationToken);
        return activity.Select(x => new DashboardMonthlyActivity(x.Currency, $"{x.Year:D4}-{x.Month:D2}",
            x.TransactionCount, x.IncomeCount, x.ExpenseCount, x.RefundCount, x.TransferCount)).ToArray();
    }
}
