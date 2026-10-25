using DollarShark.Application.Statements;
using DollarShark.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DollarShark.Infrastructure.Persistence;

public sealed class StatementImportRepository(FinanceDbContext db) : IStatementImportRepository
{
    public Task<string?> GetAccountNameForUserAsync(Guid accountId, string userId, CancellationToken cancellationToken) =>
        db.FinancialAccounts.AsNoTracking().Where(x => x.Id == accountId && x.UserId == userId)
            .Select(x => x.Name).SingleOrDefaultAsync(cancellationToken);

    public async Task AddAsync(StatementImport statement, CancellationToken cancellationToken) =>
        await db.StatementImports.AddAsync(statement, cancellationToken);

    public async Task<IReadOnlyList<StatementImportDto>> ListAsync(string userId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.StatementImports.AsNoTracking().Where(x => x.UserId == userId)
            .OrderByDescending(x => x.UploadedAtUtc).ThenByDescending(x => x.Id)
            .Skip((int)Math.Min((long)(page - 1) * pageSize, int.MaxValue)).Take(pageSize);
        return await Project(query).ToListAsync(cancellationToken);
    }

    public Task<int> CountAsync(string userId, CancellationToken cancellationToken) =>
        db.StatementImports.CountAsync(x => x.UserId == userId, cancellationToken);

    public Task<StatementImportDto?> GetAsync(Guid id, string userId, CancellationToken cancellationToken) =>
        Project(db.StatementImports.AsNoTracking().Where(x => x.Id == id && x.UserId == userId))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<StatementImport?> GetTrackedAsync(Guid id, string userId, CancellationToken cancellationToken) =>
        db.StatementImports.SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId, cancellationToken);

    public Task<string?> GetAccountCurrencyForUserAsync(Guid accountId, string userId, CancellationToken cancellationToken) =>
        db.FinancialAccounts.AsNoTracking().Where(x => x.Id == accountId && x.UserId == userId)
            .Select(x => x.Currency).SingleOrDefaultAsync(cancellationToken);

    public async Task<bool> TryClaimForProcessingAsync(Guid id, string userId, CancellationToken cancellationToken) =>
        await db.StatementImports.Where(x => x.Id == id && x.UserId == userId && x.Status == StatementImportStatus.Uploaded)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.Status, StatementImportStatus.Processing), cancellationToken) == 1;

    public async Task<IStatementImportTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
        new StatementImportTransaction(await db.Database.BeginTransactionAsync(cancellationToken));

    public void Delete(StatementImport statement) => db.StatementImports.Remove(statement);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    private static IQueryable<StatementImportDto> Project(IQueryable<StatementImport> query) =>
        query.Select(x => new StatementImportDto(x.Id, x.FinancialAccountId, x.FinancialAccount.Name,
            x.OriginalFileName, x.FileSizeBytes, x.ContentType, x.Status.ToString(), x.UploadedAtUtc, x.ProcessedAtUtc));

    private sealed class StatementImportTransaction(IDbContextTransaction transaction) : IStatementImportTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
