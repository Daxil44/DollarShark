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

    public async Task<IReadOnlyList<DashboardTotals>> GetTotalsAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var userId = UserId;
        var totals = await db.Transactions.AsNoTracking()
            .Where(transaction => transaction.UserId == userId && transaction.TransactionDate >= from && transaction.TransactionDate <= to)
            .GroupBy(transaction => transaction.Currency)
            .Select(group => new
            {
                Currency = group.Key,
                Income = group.Sum(transaction => transaction.TransactionType != TransactionType.Transfer && transaction.Amount > 0
                    ? transaction.Amount : 0m),
                Expenses = -group.Sum(transaction => transaction.TransactionType != TransactionType.Transfer && transaction.Amount < 0
                    ? transaction.Amount : 0m),
                NetCashFlow = group.Sum(transaction => transaction.TransactionType != TransactionType.Transfer
                    ? transaction.Amount : 0m)
            })
            .OrderBy(row => row.Currency)
            .ToListAsync(cancellationToken);

        return totals.Select(row => new DashboardTotals(row.Currency, row.Income, row.Expenses, row.NetCashFlow)).ToArray();
    }

    public async Task<IReadOnlyList<DashboardRecentTransaction>> GetRecentTransactionsAsync(
        DateOnly from, DateOnly to, int limit, CancellationToken cancellationToken)
    {
        var userId = UserId;
        return await db.Transactions.AsNoTracking()
            .Where(transaction => transaction.UserId == userId && transaction.TransactionDate >= from && transaction.TransactionDate <= to)
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
        DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var userId = UserId;
        var groupedExpenses = await db.Transactions.AsNoTracking()
            .Where(x => x.UserId == userId && x.TransactionDate >= from && x.TransactionDate <= to && x.TransactionType != TransactionType.Transfer && x.Amount < 0)
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
        DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var userId = UserId;
        var monthlyTotals = await db.Transactions.AsNoTracking()
            .Where(x => x.UserId == userId && x.TransactionDate >= from && x.TransactionDate <= to && x.TransactionType != TransactionType.Transfer)
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
}
