using DollarShark.Application.Common;
using DollarShark.Domain;

namespace DollarShark.Application.Dashboard;

public sealed class DashboardQuery
{
    public Guid? AccountId { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public int RecentLimit { get; init; } = 8;
}

public sealed class AccountBalanceHistoryQuery
{
    public Guid AccountId { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public string Interval { get; init; } = "Daily";
}

public sealed record DashboardBalanceProfile(Guid Id, string Name, string AccountType, string Currency,
    AccountClassification Classification, decimal? OpeningBalance, DateOnly? OpeningBalanceDate);
public sealed record DashboardDailyBalanceMovement(DateOnly Date, decimal Amount);
public sealed record AccountBalancePoint(DateOnly Date, decimal Balance);
public sealed record AccountBalanceHistory(
    Guid AccountId, string AccountName, string AccountType, string Classification, string Currency,
    decimal? OpeningBalance, DateOnly? OpeningBalanceDate, bool IsAvailable, string? Message,
    DateOnly PeriodStart, DateOnly PeriodEnd, string Interval, IReadOnlyList<AccountBalancePoint> Points);

public sealed record DashboardTotals(string Currency, decimal Income, decimal Expenses, decimal NetCashFlow,
    decimal MoneyReceived, decimal MoneySpent, int TransactionCount);
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
    decimal PreviousNetCashFlow,
    decimal MoneyReceived,
    decimal MoneySpent,
    int TransactionCount);
public sealed record DashboardCategoryExpense(string Currency, string CategoryName, decimal Amount);
public sealed record DashboardMonthlyTotals(string Currency, string Month, decimal Income, decimal Expenses);
public sealed record DashboardMonthlyActivity(string Currency, string Month, int TransactionCount,
    int IncomeCount, int ExpenseCount, int RefundCount, int TransferCount);
public sealed record DashboardOverview(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateOnly PreviousPeriodStart,
    DateOnly PreviousPeriodEnd,
    IReadOnlyList<DashboardCurrencySummary> Currencies,
    IReadOnlyList<DashboardRecentTransaction> RecentTransactions,
    IReadOnlyList<DashboardCategoryExpense> CategoryExpenses,
    IReadOnlyList<DashboardMonthlyTotals> MonthlyTotals,
    IReadOnlyList<DashboardMonthlyActivity> MonthlyActivity,
    Guid? AccountId);

public interface IDashboardRepository
{
    Task<string?> GetActiveAccountCurrencyAsync(Guid accountId, CancellationToken cancellationToken);
    Task<IReadOnlyList<DashboardTotals>> GetTotalsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken, Guid? accountId = null);
    Task<IReadOnlyList<DashboardRecentTransaction>> GetRecentTransactionsAsync(DateOnly from, DateOnly to, int limit, CancellationToken cancellationToken, Guid? accountId = null);
    Task<IReadOnlyList<DashboardCategoryExpense>> GetCategoryExpensesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken, Guid? accountId = null);
    Task<IReadOnlyList<DashboardMonthlyTotals>> GetMonthlyTotalsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken, Guid? accountId = null);
    Task<IReadOnlyList<DashboardMonthlyActivity>> GetMonthlyActivityAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken, Guid? accountId = null);
    Task<DashboardBalanceProfile?> GetBalanceProfileAsync(Guid accountId, CancellationToken cancellationToken);
    Task<IReadOnlyList<DashboardDailyBalanceMovement>> GetDailyBalanceMovementsAsync(Guid accountId,
        DateOnly openingBalanceDate, DateOnly to, AccountClassification classification, CancellationToken cancellationToken);
}

public interface IDashboardService
{
    Task<UseCaseResult<DashboardOverview>> GetOverviewAsync(DashboardQuery query, CancellationToken cancellationToken);
    Task<UseCaseResult<AccountBalanceHistory>> GetAccountBalanceHistoryAsync(AccountBalanceHistoryQuery query, CancellationToken cancellationToken);
}

public sealed class DashboardService(IDashboardRepository repository) : IDashboardService
{
    private const int MaxRecentLimit = 20;
    private const int MaxDailyHistoryDays = 366;
    private const int MaxWeeklyHistoryDays = 3653;
    private const int MaxMonthlyHistoryDays = 36525;

    public async Task<UseCaseResult<DashboardOverview>> GetOverviewAsync(DashboardQuery query, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (query.From.HasValue != query.To.HasValue)
        {
            errors["dateRange"] = ["Provide both from and to dates, or omit both to use the current calendar month."];
        }
        if (query.AccountId == Guid.Empty)
            errors["accountId"] = ["Choose a valid account."];
        if (query.From is DateOnly suppliedFrom && suppliedFrom.Year < 1900)
            errors["from"] = ["The start date must be in year 1900 or later."];
        if (query.From is DateOnly start && query.To is DateOnly end && start > end)
            errors["to"] = ["The end date must be on or after the start date."];
        if (query.RecentLimit is < 1 or > MaxRecentLimit)
            errors["recentLimit"] = [$"Recent transaction limit must be between 1 and {MaxRecentLimit}."];
        if (errors.Count > 0) return UseCaseResult<DashboardOverview>.Invalid(errors);

        var accountCurrency = query.AccountId is Guid selectedAccountId
            ? await repository.GetActiveAccountCurrencyAsync(selectedAccountId, cancellationToken)
            : null;
        if (query.AccountId.HasValue && accountCurrency is null)
            return UseCaseResult<DashboardOverview>.NotFound("Account was not found.");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var periodStart = query.From ?? new DateOnly(today.Year, today.Month, 1);
        var periodEnd = query.To ?? new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
        var (previousStart, previousEnd) = PreviousPeriod(periodStart, periodEnd);

        var currentTotals = await repository.GetTotalsAsync(periodStart, periodEnd, cancellationToken, query.AccountId);
        var previousTotals = await repository.GetTotalsAsync(previousStart, previousEnd, cancellationToken, query.AccountId);
        var recent = await repository.GetRecentTransactionsAsync(periodStart, periodEnd, query.RecentLimit, cancellationToken, query.AccountId);
        var categoryExpenses = await repository.GetCategoryExpensesAsync(periodStart, periodEnd, cancellationToken, query.AccountId);
        var monthlyTotals = await repository.GetMonthlyTotalsAsync(periodStart, periodEnd, cancellationToken, query.AccountId);
        var monthlyActivity = await repository.GetMonthlyActivityAsync(periodStart, periodEnd, cancellationToken, query.AccountId);
        var currentByCurrency = currentTotals.ToDictionary(x => x.Currency, StringComparer.OrdinalIgnoreCase);
        var previousByCurrency = previousTotals.ToDictionary(x => x.Currency, StringComparer.OrdinalIgnoreCase);
        var currencies = currentByCurrency.Keys.Concat(previousByCurrency.Keys)
            .Concat(accountCurrency is null ? Array.Empty<string>() : [accountCurrency])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(currency => currency, StringComparer.OrdinalIgnoreCase)
            .Select(currency =>
            {
                currentByCurrency.TryGetValue(currency, out var current);
                previousByCurrency.TryGetValue(currency, out var previous);
                return new DashboardCurrencySummary(currency,
                    current?.Income ?? 0m, current?.Expenses ?? 0m, current?.NetCashFlow ?? 0m,
                    previous?.Income ?? 0m, previous?.Expenses ?? 0m, previous?.NetCashFlow ?? 0m,
                    current?.MoneyReceived ?? 0m, current?.MoneySpent ?? 0m, current?.TransactionCount ?? 0);
            })
            .ToArray();

        return UseCaseResult<DashboardOverview>.Ok(new DashboardOverview(
            periodStart, periodEnd, previousStart, previousEnd, currencies, recent, categoryExpenses, monthlyTotals,
            monthlyActivity, query.AccountId));
    }

    public async Task<UseCaseResult<AccountBalanceHistory>> GetAccountBalanceHistoryAsync(
        AccountBalanceHistoryQuery query, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (query.AccountId == Guid.Empty) errors["accountId"] = ["Choose a valid account."];
        if (query.From.HasValue != query.To.HasValue)
            errors["dateRange"] = ["Provide both from and to dates, or omit both to use the current month."];
        if (query.From is DateOnly from && from.Year < 1900) errors["from"] = ["The start date must be in year 1900 or later."];
        if (query.To is DateOnly to && to.Year < 1900) errors["to"] = ["The end date must be in year 1900 or later."];
        if (query.From is DateOnly rangeFrom && query.To is DateOnly rangeTo && rangeFrom > rangeTo)
            errors["to"] = ["The end date must be on or after the start date."];
        var interval = ParseInterval(query.Interval);
        if (interval is null) errors["interval"] = ["Choose Daily, Weekly, or Monthly history."];
        if (errors.Count > 0) return UseCaseResult<AccountBalanceHistory>.Invalid(errors);
        var historyInterval = interval!;

        var profile = await repository.GetBalanceProfileAsync(query.AccountId, cancellationToken);
        if (profile is null) return UseCaseResult<AccountBalanceHistory>.NotFound("Account was not found.");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var requestedFrom = query.From ?? new DateOnly(today.Year, today.Month, 1);
        var requestedTo = query.To ?? today;
        if (!profile.OpeningBalance.HasValue || !profile.OpeningBalanceDate.HasValue)
            return UseCaseResult<AccountBalanceHistory>.Ok(new AccountBalanceHistory(
                profile.Id, profile.Name, profile.AccountType, profile.Classification.ToString(), profile.Currency,
                null, null, false, "Set an opening balance and date to view historical balances.",
                requestedFrom, requestedTo, historyInterval, []));

        var openingDate = profile.OpeningBalanceDate.Value;
        var historyStart = requestedFrom < openingDate ? openingDate : requestedFrom;
        if (requestedTo < openingDate)
            return UseCaseResult<AccountBalanceHistory>.Ok(new AccountBalanceHistory(
                profile.Id, profile.Name, profile.AccountType, profile.Classification.ToString(), profile.Currency,
                profile.OpeningBalance, openingDate, true, "The selected period is before the opening balance date.",
                requestedFrom, requestedTo, historyInterval, []));

        var durationDays = requestedTo.DayNumber - historyStart.DayNumber + 1;
        var maxDays = historyInterval switch
        {
            "Daily" => MaxDailyHistoryDays,
            "Weekly" => MaxWeeklyHistoryDays,
            _ => MaxMonthlyHistoryDays
        };
        if (durationDays > maxDays)
            return UseCaseResult<AccountBalanceHistory>.Invalid(new Dictionary<string, string[]>
            {
                ["interval"] = [$"This date range is too long for {historyInterval.ToLowerInvariant()} points. Choose a less frequent interval."]
            });

        var movements = await repository.GetDailyBalanceMovementsAsync(
            profile.Id, openingDate, requestedTo, profile.Classification, cancellationToken);
        var balance = profile.OpeningBalance.Value;
        var movementIndex = 0;
        while (movementIndex < movements.Count && movements[movementIndex].Date <= historyStart)
            balance += movements[movementIndex++].Amount;

        var points = new List<AccountBalancePoint> { new(historyStart, balance) };
        var cursor = historyStart;
        while (cursor < requestedTo)
        {
            var segmentEnd = NextHistoryPoint(cursor, requestedTo, historyInterval);
            while (movementIndex < movements.Count && movements[movementIndex].Date <= segmentEnd)
                balance += movements[movementIndex++].Amount;
            points.Add(new AccountBalancePoint(segmentEnd, balance));
            cursor = segmentEnd;
        }
        var note = requestedFrom < openingDate ? "History starts on the opening balance date." : null;
        return UseCaseResult<AccountBalanceHistory>.Ok(new AccountBalanceHistory(
            profile.Id, profile.Name, profile.AccountType, profile.Classification.ToString(), profile.Currency,
            profile.OpeningBalance, openingDate, true, note, historyStart, requestedTo, historyInterval, points));
    }

    private static string? ParseInterval(string? value) =>
        new[] { "Daily", "Weekly", "Monthly" }.FirstOrDefault(x => string.Equals(x, value?.Trim(), StringComparison.OrdinalIgnoreCase));

    private static DateOnly NextHistoryPoint(DateOnly current, DateOnly end, string interval)
    {
        var remainingDays = end.DayNumber - current.DayNumber;
        if (interval == "Daily") return current.AddDays(1);
        if (interval == "Weekly") return current.AddDays(Math.Min(7, remainingDays));
        if (current.Year == DateOnly.MaxValue.Year && current.Month == 12) return end;
        var nextMonth = new DateOnly(current.Year, current.Month, 1).AddMonths(1);
        var monthEnd = nextMonth.AddDays(-1);
        if (monthEnd <= current)
        {
            if (nextMonth.Year == DateOnly.MaxValue.Year && nextMonth.Month == 12) return end;
            monthEnd = nextMonth.AddMonths(1).AddDays(-1);
        }
        return monthEnd > end ? end : monthEnd;
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
