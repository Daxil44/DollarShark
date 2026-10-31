using DollarShark.Application.Common;
using DollarShark.Domain;

namespace DollarShark.Application.Accounts;

public sealed record AccountRequest(string? Name, string? Institution, string? AccountType, string? Currency, string? LastFourDigits,
    decimal? OpeningBalance = null, DateOnly? OpeningBalanceDate = null, bool ClearOpeningBalance = false);
public sealed record AccountStatusRequest(bool IsActive);
public sealed record AccountDto(Guid Id, string Name, string Institution, string AccountType, string Classification,
    string Currency, string? LastFourDigits, decimal? OpeningBalance, DateOnly? OpeningBalanceDate,
    decimal? CalculatedBalance, bool IsActive, DateTime CreatedAtUtc);

public interface IAccountRepository
{
    Task<IReadOnlyList<AccountDto>> ListAsync(bool includeInactive, CancellationToken cancellationToken);
    Task<AccountDto?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<FinancialAccount?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task AddAsync(FinancialAccount account, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IAccountService
{
    Task<UseCaseResult<IReadOnlyList<AccountDto>>> ListAsync(bool includeInactive, CancellationToken cancellationToken);
    Task<UseCaseResult<AccountDto>> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<UseCaseResult<AccountDto>> CreateAsync(AccountRequest request, CancellationToken cancellationToken);
    Task<UseCaseResult<AccountDto>> UpdateAsync(Guid id, AccountRequest request, CancellationToken cancellationToken);
    Task<UseCaseResult<AccountDto>> SetStatusAsync(Guid id, AccountStatusRequest request, CancellationToken cancellationToken);
}

public sealed class AccountService(IAccountRepository repository) : IAccountService
{
    private static readonly string[] AccountTypes = ["Chequing", "Savings", "Credit Card", "Investment"];

    public async Task<UseCaseResult<IReadOnlyList<AccountDto>>> ListAsync(bool includeInactive, CancellationToken cancellationToken) =>
        UseCaseResult<IReadOnlyList<AccountDto>>.Ok(await repository.ListAsync(includeInactive, cancellationToken));

    public async Task<UseCaseResult<AccountDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty) return UseCaseResult<AccountDto>.Invalid(Errors(("id", "A valid account ID is required.")));
        var account = await repository.GetAsync(id, cancellationToken);
        return account is null ? UseCaseResult<AccountDto>.NotFound("Account was not found.") : UseCaseResult<AccountDto>.Ok(account);
    }

    public async Task<UseCaseResult<AccountDto>> CreateAsync(AccountRequest request, CancellationToken cancellationToken)
    {
        var normalized = Validate(request);
        if (normalized.Errors is not null) return UseCaseResult<AccountDto>.Invalid(normalized.Errors);
        if (request.ClearOpeningBalance) return UseCaseResult<AccountDto>.Invalid(Errors(("clearOpeningBalance", "An opening balance cannot be cleared while creating an account.")));
        var account = new FinancialAccount
        {
            Name = normalized.Name!, Institution = normalized.Institution!, AccountType = normalized.AccountType!,
            Currency = normalized.Currency!, LastFourDigits = normalized.LastFourDigits,
            OpeningBalance = normalized.OpeningBalance, OpeningBalanceDate = normalized.OpeningBalanceDate
        };
        await repository.AddAsync(account, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return UseCaseResult<AccountDto>.Created(ToDto(account));
    }

    public async Task<UseCaseResult<AccountDto>> UpdateAsync(Guid id, AccountRequest request, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty) return UseCaseResult<AccountDto>.Invalid(Errors(("id", "A valid account ID is required.")));
        var account = await repository.FindAsync(id, cancellationToken);
        if (account is null) return UseCaseResult<AccountDto>.NotFound("Account was not found.");
        var normalized = Validate(request);
        if (normalized.Errors is not null) return UseCaseResult<AccountDto>.Invalid(normalized.Errors);
        account.Name = normalized.Name!;
        account.Institution = normalized.Institution!;
        account.AccountType = normalized.AccountType!;
        account.Currency = normalized.Currency!;
        account.LastFourDigits = normalized.LastFourDigits;
        if (request.ClearOpeningBalance)
        {
            account.OpeningBalance = null;
            account.OpeningBalanceDate = null;
        }
        else if (normalized.OpeningBalance is decimal openingBalance && normalized.OpeningBalanceDate is DateOnly openingBalanceDate)
        {
            account.OpeningBalance = openingBalance;
            account.OpeningBalanceDate = openingBalanceDate;
        }
        await repository.SaveChangesAsync(cancellationToken);
        return UseCaseResult<AccountDto>.Ok((await repository.GetAsync(id, cancellationToken))!);
    }

    public async Task<UseCaseResult<AccountDto>> SetStatusAsync(Guid id, AccountStatusRequest request, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty) return UseCaseResult<AccountDto>.Invalid(Errors(("id", "A valid account ID is required.")));
        var account = await repository.FindAsync(id, cancellationToken);
        if (account is null) return UseCaseResult<AccountDto>.NotFound("Account was not found.");
        account.IsActive = request.IsActive;
        await repository.SaveChangesAsync(cancellationToken);
        return UseCaseResult<AccountDto>.Ok((await repository.GetAsync(id, cancellationToken))!);
    }

    private static (string? Name, string? Institution, string? AccountType, string? Currency, string? LastFourDigits,
        decimal? OpeningBalance, DateOnly? OpeningBalanceDate,
        IReadOnlyDictionary<string, string[]>? Errors) Validate(AccountRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        var name = request.Name?.Trim();
        var institution = request.Institution?.Trim();
        var accountType = AccountTypes.FirstOrDefault(x => string.Equals(x, request.AccountType?.Trim(), StringComparison.OrdinalIgnoreCase));
        var currency = request.Currency?.Trim().ToUpperInvariant();
        var lastFour = string.IsNullOrWhiteSpace(request.LastFourDigits) ? null : request.LastFourDigits.Trim();
        var openingBalance = request.OpeningBalance;
        var openingBalanceDate = request.OpeningBalanceDate;

        if (string.IsNullOrWhiteSpace(name) || name.Length > 120) errors["name"] = ["Name is required and must be at most 120 characters."];
        if (string.IsNullOrWhiteSpace(institution) || institution.Length > 120) errors["institution"] = ["Institution is required and must be at most 120 characters."];
        if (accountType is null) errors["accountType"] = ["Account type must be Chequing, Savings, Credit Card, or Investment."];
        if (currency is null || currency.Length != 3 || currency.Any(c => !char.IsAsciiLetter(c))) errors["currency"] = ["Currency must be a three-letter code, such as CAD."];
        if (lastFour is not null && (lastFour.Length != 4 || lastFour.Any(c => !char.IsAsciiDigit(c)))) errors["lastFourDigits"] = ["Last four digits must contain exactly four numbers."];
        if (request.ClearOpeningBalance && (openingBalance.HasValue || openingBalanceDate.HasValue)) errors["clearOpeningBalance"] = ["Clear the opening amount and date before requesting removal."];
        if (!request.ClearOpeningBalance && openingBalance.HasValue != openingBalanceDate.HasValue)
            errors["openingBalanceDate"] = ["Enter both an opening balance and its date, or leave both blank."];
        if (openingBalance is decimal amount && (amount < -999_999_999_999_999.9999m || amount > 999_999_999_999_999.9999m))
            errors["openingBalance"] = ["Opening balance must fit within 15 digits before the decimal and 4 digits after it."];
        else if (openingBalance is decimal preciseAmount && decimal.Round(preciseAmount, 4) != preciseAmount)
            errors["openingBalance"] = ["Opening balance supports up to 4 decimal places."];
        if (openingBalanceDate is DateOnly date && (date < new DateOnly(1900, 1, 1) || date > DateOnly.FromDateTime(DateTime.UtcNow)))
            errors["openingBalanceDate"] = ["Opening balance date must be between 1900 and today."];
        if (request.ClearOpeningBalance && openingBalance.HasValue == false && openingBalanceDate.HasValue == false)
        {
            openingBalance = null;
            openingBalanceDate = null;
        }
        return (name, institution, accountType, currency, lastFour, openingBalance, openingBalanceDate, errors.Count == 0 ? null : errors);
    }

    private static AccountDto ToDto(FinancialAccount account) =>
        new(account.Id, account.Name, account.Institution, account.AccountType, account.Classification.ToString(),
            account.Currency, account.LastFourDigits, account.OpeningBalance, account.OpeningBalanceDate,
            account.CalculateBalance(account.Transactions), account.IsActive, account.CreatedAtUtc);

    private static IReadOnlyDictionary<string, string[]> Errors((string Key, string Message) error) =>
        new Dictionary<string, string[]> { [error.Key] = [error.Message] };
}
