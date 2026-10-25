using DollarShark.Application.Common;
using DollarShark.Domain;

namespace DollarShark.Application.Transactions;

public sealed record TransactionRequest(
    DateOnly TransactionDate,
    string? Description,
    decimal Amount,
    string? TransactionType,
    Guid FinancialAccountId,
    Guid? CategoryId);

public sealed record TransactionDto(
    Guid Id, DateOnly TransactionDate, string Description, decimal Amount, string TransactionType,
    string Currency, Guid FinancialAccountId, string AccountName, Guid? CategoryId, string? CategoryName, DateTime CreatedAtUtc);

public sealed record TransactionPage(
    IReadOnlyList<TransactionDto> Items, int Page, int PageSize, int TotalCount, int TotalPages);

public sealed class TransactionListQuery
{
    public int? Year { get; init; }
    public int? Month { get; init; }
    public Guid? AccountId { get; init; }
    public Guid? CategoryId { get; init; }
    public string? TransactionType { get; init; }
    public string? Search { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public string? SortBy { get; init; }
    public string? SortDirection { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
}

public sealed record TransactionFilter(
    int? Year, int? Month, Guid? AccountId, Guid? CategoryId, TransactionType? Type,
    string? Search, DateOnly? From, DateOnly? To, string SortBy, bool Descending, int Page, int PageSize);

public interface ITransactionRepository
{
    Task<TransactionPage> ListAsync(TransactionFilter filter, CancellationToken cancellationToken);
    Task<TransactionDto?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Transaction?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> AccountExistsAsync(Guid id, CancellationToken cancellationToken);
    Task<string?> GetAccountCurrencyAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> CategoryExistsAsync(Guid id, CancellationToken cancellationToken);
    Task AddAsync(Transaction transaction, CancellationToken cancellationToken);
    void Delete(Transaction transaction);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface ITransactionService
{
    Task<UseCaseResult<TransactionPage>> ListAsync(TransactionListQuery query, CancellationToken cancellationToken);
    Task<UseCaseResult<TransactionDto>> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<UseCaseResult<TransactionDto>> CreateAsync(TransactionRequest request, CancellationToken cancellationToken);
    Task<UseCaseResult<TransactionDto>> UpdateAsync(Guid id, TransactionRequest request, CancellationToken cancellationToken);
    Task<UseCaseResult<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken);
}

public sealed class TransactionService(ITransactionRepository repository) : ITransactionService
{
    private static readonly HashSet<string> SortFields = new(StringComparer.OrdinalIgnoreCase)
        { "date", "amount", "description", "type" };

    public async Task<UseCaseResult<TransactionPage>> ListAsync(TransactionListQuery query, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (query.Page < 1) errors["page"] = ["Page must be at least 1."];
        if (query.PageSize is < 1 or > 100) errors["pageSize"] = ["Page size must be between 1 and 100."];
        if (query.Year is < 1900 or > 9999) errors["year"] = ["Year must be between 1900 and 9999."];
        if (query.Month is < 1 or > 12) errors["month"] = ["Month must be between 1 and 12."];
        if (query.From is DateOnly from && query.To is DateOnly to && from > to)
            errors["to"] = ["The end date must be on or after the start date."];
        if (query.AccountId == Guid.Empty) errors["accountId"] = ["Account ID must be a valid ID."];
        if (query.CategoryId == Guid.Empty) errors["categoryId"] = ["Category ID must be a valid ID."];
        var type = ParseType(query.TransactionType);
        if (!string.IsNullOrWhiteSpace(query.TransactionType) && type is null)
            errors["transactionType"] = ["Transaction type must be Expense, Income, Refund, or Transfer."];
        var sortBy = string.IsNullOrWhiteSpace(query.SortBy) ? "date" : query.SortBy.Trim().ToLowerInvariant();
        if (!SortFields.Contains(sortBy)) errors["sortBy"] = ["Sort by date, amount, description, or type."];
        var direction = string.IsNullOrWhiteSpace(query.SortDirection) ? "desc" : query.SortDirection.Trim();
        if (!direction.Equals("asc", StringComparison.OrdinalIgnoreCase) &&
            !direction.Equals("desc", StringComparison.OrdinalIgnoreCase))
            errors["sortDirection"] = ["Sort direction must be asc or desc."];
        if (query.Search?.Length > 100) errors["search"] = ["Search must be at most 100 characters."];
        if (errors.Count > 0) return UseCaseResult<TransactionPage>.Invalid(errors);

        if (query.AccountId is Guid accountId && !await repository.AccountExistsAsync(accountId, cancellationToken))
            return UseCaseResult<TransactionPage>.Invalid(Error("accountId", "Account was not found."));
        if (query.CategoryId is Guid categoryId && !await repository.CategoryExistsAsync(categoryId, cancellationToken))
            return UseCaseResult<TransactionPage>.Invalid(Error("categoryId", "Category was not found."));

        var filter = new TransactionFilter(query.Year, query.Month, query.AccountId, query.CategoryId, type,
            string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(), query.From, query.To,
            sortBy, direction.Equals("desc", StringComparison.OrdinalIgnoreCase), query.Page, query.PageSize);
        return UseCaseResult<TransactionPage>.Ok(await repository.ListAsync(filter, cancellationToken));
    }

    public async Task<UseCaseResult<TransactionDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty) return UseCaseResult<TransactionDto>.Invalid(Error("id", "A valid transaction ID is required."));
        var transaction = await repository.GetAsync(id, cancellationToken);
        return transaction is null ? UseCaseResult<TransactionDto>.NotFound("Transaction was not found.") : UseCaseResult<TransactionDto>.Ok(transaction);
    }

    public async Task<UseCaseResult<TransactionDto>> CreateAsync(TransactionRequest request, CancellationToken cancellationToken)
    {
        var validation = await ValidateAsync(request, cancellationToken);
        if (validation.Errors is not null) return UseCaseResult<TransactionDto>.Invalid(validation.Errors);
        var transaction = new Transaction
        {
            TransactionDate = request.TransactionDate,
            Description = validation.Description!,
            Amount = request.Amount,
            TransactionType = validation.Type!.Value,
            FinancialAccountId = request.FinancialAccountId,
            CategoryId = request.CategoryId,
            Currency = validation.Currency!
        };
        await repository.AddAsync(transaction, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        var created = await repository.GetAsync(transaction.Id, cancellationToken) ?? throw new InvalidOperationException("Saved transaction could not be reloaded.");
        return UseCaseResult<TransactionDto>.Created(created);
    }

    public async Task<UseCaseResult<TransactionDto>> UpdateAsync(Guid id, TransactionRequest request, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty) return UseCaseResult<TransactionDto>.Invalid(Error("id", "A valid transaction ID is required."));
        var transaction = await repository.FindAsync(id, cancellationToken);
        if (transaction is null) return UseCaseResult<TransactionDto>.NotFound("Transaction was not found.");
        var validation = await ValidateAsync(request, cancellationToken);
        if (validation.Errors is not null) return UseCaseResult<TransactionDto>.Invalid(validation.Errors);
        transaction.TransactionDate = request.TransactionDate;
        transaction.Description = validation.Description!;
        transaction.Amount = request.Amount;
        transaction.TransactionType = validation.Type!.Value;
        transaction.FinancialAccountId = request.FinancialAccountId;
        transaction.CategoryId = request.CategoryId;
        transaction.Currency = validation.Currency!;
        await repository.SaveChangesAsync(cancellationToken);
        var updated = await repository.GetAsync(id, cancellationToken) ?? throw new InvalidOperationException("Saved transaction could not be reloaded.");
        return UseCaseResult<TransactionDto>.Ok(updated);
    }

    public async Task<UseCaseResult<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty) return UseCaseResult<bool>.Invalid(Error("id", "A valid transaction ID is required."));
        var transaction = await repository.FindAsync(id, cancellationToken);
        if (transaction is null) return UseCaseResult<bool>.NotFound("Transaction was not found.");
        repository.Delete(transaction);
        await repository.SaveChangesAsync(cancellationToken);
        return UseCaseResult<bool>.NoContent(true);
    }

    private async Task<(string? Description, TransactionType? Type, string? Currency,
        IReadOnlyDictionary<string, string[]>? Errors)> ValidateAsync(TransactionRequest request, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var description = request.Description?.Trim();
        var type = ParseType(request.TransactionType);
        if (request.TransactionDate == DateOnly.MinValue || request.TransactionDate.Year < 1900 ||
            request.TransactionDate > DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)))
            errors["transactionDate"] = ["Enter a valid transaction date no earlier than 1900 and no later than tomorrow."];
        if (string.IsNullOrWhiteSpace(description) || description.Length > 300)
            errors["description"] = ["Description is required and must be at most 300 characters."];
        if (type is null) errors["transactionType"] = ["Transaction type must be Expense, Income, Refund, or Transfer."];
        if (request.Amount == 0 || decimal.Abs(request.Amount) >= 1_000_000_000_000_000m || decimal.Round(request.Amount, 4) != request.Amount)
            errors["amount"] = ["Amount must be nonzero, fit decimal(19,4), and have at most four decimal places."];
        if (type == TransactionType.Expense && request.Amount >= 0)
            errors["amount"] = ["Expenses must have a negative amount."];
        if (type is TransactionType.Income or TransactionType.Refund && request.Amount <= 0)
            errors["amount"] = ["Income and refunds must have a positive amount."];
        if (request.FinancialAccountId == Guid.Empty)
            errors["financialAccountId"] = ["A financial account is required."];
        else if (!await repository.AccountExistsAsync(request.FinancialAccountId, cancellationToken))
            errors["financialAccountId"] = ["Financial account was not found."];
        if (request.CategoryId == Guid.Empty)
            errors["categoryId"] = ["Category ID must be a valid ID."];
        else if (request.CategoryId is Guid categoryId && !await repository.CategoryExistsAsync(categoryId, cancellationToken))
            errors["categoryId"] = ["Category was not found."];
        var currency = request.FinancialAccountId == Guid.Empty ? null :
            await repository.GetAccountCurrencyAsync(request.FinancialAccountId, cancellationToken);
        if (errors.Count > 0) return (description, type, null, errors);
        return (description, type, currency, null);
    }

    private static TransactionType? ParseType(string? value) =>
        Enum.TryParse<TransactionType>(value?.Trim(), true, out var type) && Enum.IsDefined(type) ? type : null;

    private static IReadOnlyDictionary<string, string[]> Error(string key, string message) =>
        new Dictionary<string, string[]> { [key] = [message] };
}

