using DollarShark.Application.Common;

namespace DollarShark.Application.Dashboard;

public sealed class DashboardQuery
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public int RecentLimit { get; init; } = 8;
}

public sealed record DashboardTotals(string Currency, decimal Income, decimal Expenses, decimal NetCashFlow);
public sealed record DashboardRecentTransaction(
    Guid Id, DateOnly TransactionDate, string Description, decimal Amount, string Currency,
    string TransactionType, string AccountName, string? CategoryName);
public sealed record DashboardCurrencySummary(
    string Currency,
    decimal Income,
    decimal Expenses,
    decimal NetCashFlow,
    decimal PreviousIncome,
    decimal PreviousExpenses,
    decimal PreviousNetCashFlow);
public sealed record DashboardOverview(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateOnly PreviousPeriodStart,
    DateOnly PreviousPeriodEnd,
    IReadOnlyList<DashboardCurrencySummary> Currencies,
    IReadOnlyList<DashboardRecentTransaction> RecentTransactions);

public interface IDashboardRepository
{
    Task<IReadOnlyList<DashboardTotals>> GetTotalsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);
    Task<IReadOnlyList<DashboardRecentTransaction>> GetRecentTransactionsAsync(DateOnly from, DateOnly to, int limit, CancellationToken cancellationToken);
}

public interface IDashboardService
{
    Task<UseCaseResult<DashboardOverview>> GetOverviewAsync(DashboardQuery query, CancellationToken cancellationToken);
}

public sealed class DashboardService(IDashboardRepository repository) : IDashboardService
{
    private const int MaxRecentLimit = 20;

    public async Task<UseCaseResult<DashboardOverview>> GetOverviewAsync(DashboardQuery query, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (query.From.HasValue != query.To.HasValue)
        {
            errors["dateRange"] = ["Provide both from and to dates, or omit both to use the current calendar month."];
        }
        if (query.From is DateOnly suppliedFrom && suppliedFrom.Year < 1900)
            errors["from"] = ["The start date must be in year 1900 or later."];
        if (query.From is DateOnly start && query.To is DateOnly end && start > end)
            errors["to"] = ["The end date must be on or after the start date."];
        if (query.RecentLimit is < 1 or > MaxRecentLimit)
            errors["recentLimit"] = [$"Recent transaction limit must be between 1 and {MaxRecentLimit}."];
        if (errors.Count > 0) return UseCaseResult<DashboardOverview>.Invalid(errors);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var periodStart = query.From ?? new DateOnly(today.Year, today.Month, 1);
        var periodEnd = query.To ?? new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
        var (previousStart, previousEnd) = PreviousPeriod(periodStart, periodEnd);

        var currentTotals = await repository.GetTotalsAsync(periodStart, periodEnd, cancellationToken);
        var previousTotals = await repository.GetTotalsAsync(previousStart, previousEnd, cancellationToken);
        var recent = await repository.GetRecentTransactionsAsync(periodStart, periodEnd, query.RecentLimit, cancellationToken);
        var currentByCurrency = currentTotals.ToDictionary(x => x.Currency, StringComparer.OrdinalIgnoreCase);
        var previousByCurrency = previousTotals.ToDictionary(x => x.Currency, StringComparer.OrdinalIgnoreCase);
        var currencies = currentByCurrency.Keys.Concat(previousByCurrency.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(currency => currency, StringComparer.OrdinalIgnoreCase)
            .Select(currency =>
            {
                currentByCurrency.TryGetValue(currency, out var current);
                previousByCurrency.TryGetValue(currency, out var previous);
                return new DashboardCurrencySummary(currency,
                    current?.Income ?? 0m, current?.Expenses ?? 0m, current?.NetCashFlow ?? 0m,
                    previous?.Income ?? 0m, previous?.Expenses ?? 0m, previous?.NetCashFlow ?? 0m);
            })
            .ToArray();

        return UseCaseResult<DashboardOverview>.Ok(new DashboardOverview(
            periodStart, periodEnd, previousStart, previousEnd, currencies, recent));
    }

    private static (DateOnly Start, DateOnly End) PreviousPeriod(DateOnly from, DateOnly to)
    {
        var previousEnd = from.AddDays(-1);
        if (from.Year == to.Year && from.Month == to.Month && from.Day == 1 && to.Day == DateTime.DaysInMonth(to.Year, to.Month))
        {
            var previousMonth = from.AddMonths(-1);
            return (new DateOnly(previousMonth.Year, previousMonth.Month, 1), previousEnd);
        }

        var lengthInDays = to.DayNumber - from.DayNumber + 1;
        return (previousEnd.AddDays(1 - lengthInDays), previousEnd);
    }
}
