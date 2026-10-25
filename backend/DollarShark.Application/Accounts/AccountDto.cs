namespace DollarShark.Application.Accounts;
public sealed record AccountDto(Guid Id, string Name, string Institution, string AccountType, string Currency, string? LastFourDigits, bool IsActive, DateTime CreatedAtUtc);
public interface IAccountReader { Task<IReadOnlyList<AccountDto>> GetAccountsAsync(CancellationToken cancellationToken); }
