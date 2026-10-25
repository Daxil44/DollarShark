using DollarShark.Application.Accounts;
using Microsoft.EntityFrameworkCore;
namespace DollarShark.Infrastructure.Persistence;
public sealed class AccountReader(FinanceDbContext db) : IAccountReader
{
    public async Task<IReadOnlyList<AccountDto>> GetAccountsAsync(CancellationToken cancellationToken) =>
        await db.FinancialAccounts.AsNoTracking().OrderBy(x => x.Name)
            .Select(x => new AccountDto(x.Id, x.Name, x.Institution, x.AccountType, x.Currency, x.LastFourDigits, x.IsActive, x.CreatedAtUtc))
            .ToListAsync(cancellationToken);
}
