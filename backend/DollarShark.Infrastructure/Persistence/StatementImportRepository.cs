using DollarShark.Application.Statements;
using DollarShark.Domain;
using Microsoft.EntityFrameworkCore;

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

    public void Delete(StatementImport statement) => db.StatementImports.Remove(statement);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    private static IQueryable<StatementImportDto> Project(IQueryable<StatementImport> query) =>
        query.Select(x => new StatementImportDto(x.Id, x.FinancialAccountId, x.FinancialAccount.Name,
            x.OriginalFileName, x.FileSizeBytes, x.ContentType, x.Status.ToString(), x.UploadedAtUtc, x.ProcessedAtUtc));
}
