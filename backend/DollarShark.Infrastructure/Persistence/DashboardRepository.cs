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
}
