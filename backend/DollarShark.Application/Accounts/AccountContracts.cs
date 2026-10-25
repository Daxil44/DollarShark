using DollarShark.Application.Common;
using DollarShark.Domain;

namespace DollarShark.Application.Accounts;

public sealed record AccountRequest(string? Name, string? Institution, string? AccountType, string? Currency, string? LastFourDigits);
public sealed record AccountStatusRequest(bool IsActive);
public sealed record AccountDto(Guid Id, string Name, string Institution, string AccountType, string Currency, string? LastFourDigits, bool IsActive, DateTime CreatedAtUtc);

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
    private static readonly string[] AccountTypes = ["Chequing", "Savings", "Credit Card"];

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
        var account = new FinancialAccount
        {
            Name = normalized.Name!, Institution = normalized.Institution!, AccountType = normalized.AccountType!,
            Currency = normalized.Currency!, LastFourDigits = normalized.LastFourDigits
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
        await repository.SaveChangesAsync(cancellationToken);
        return UseCaseResult<AccountDto>.Ok(ToDto(account));
    }

    public async Task<UseCaseResult<AccountDto>> SetStatusAsync(Guid id, AccountStatusRequest request, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty) return UseCaseResult<AccountDto>.Invalid(Errors(("id", "A valid account ID is required.")));
        var account = await repository.FindAsync(id, cancellationToken);
        if (account is null) return UseCaseResult<AccountDto>.NotFound("Account was not found.");
        account.IsActive = request.IsActive;
        await repository.SaveChangesAsync(cancellationToken);
        return UseCaseResult<AccountDto>.Ok(ToDto(account));
    }

    private static (string? Name, string? Institution, string? AccountType, string? Currency, string? LastFourDigits,
        IReadOnlyDictionary<string, string[]>? Errors) Validate(AccountRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        var name = request.Name?.Trim();
        var institution = request.Institution?.Trim();
        var accountType = AccountTypes.FirstOrDefault(x => string.Equals(x, request.AccountType?.Trim(), StringComparison.OrdinalIgnoreCase));
        var currency = request.Currency?.Trim().ToUpperInvariant();
        var lastFour = string.IsNullOrWhiteSpace(request.LastFourDigits) ? null : request.LastFourDigits.Trim();

        if (string.IsNullOrWhiteSpace(name) || name.Length > 120) errors["name"] = ["Name is required and must be at most 120 characters."];
        if (string.IsNullOrWhiteSpace(institution) || institution.Length > 120) errors["institution"] = ["Institution is required and must be at most 120 characters."];
        if (accountType is null) errors["accountType"] = ["Account type must be Chequing, Savings, or Credit Card."];
        if (currency is null || currency.Length != 3 || currency.Any(c => !char.IsAsciiLetter(c))) errors["currency"] = ["Currency must be a three-letter code, such as CAD."];
        if (lastFour is not null && (lastFour.Length != 4 || lastFour.Any(c => !char.IsAsciiDigit(c)))) errors["lastFourDigits"] = ["Last four digits must contain exactly four numbers."];
        return (name, institution, accountType, currency, lastFour, errors.Count == 0 ? null : errors);
    }

    private static AccountDto ToDto(FinancialAccount account) =>
        new(account.Id, account.Name, account.Institution, account.AccountType, account.Currency, account.LastFourDigits, account.IsActive, account.CreatedAtUtc);

    private static IReadOnlyDictionary<string, string[]> Errors((string Key, string Message) error) =>
        new Dictionary<string, string[]> { [error.Key] = [error.Message] };
}
