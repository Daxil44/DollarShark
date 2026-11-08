using DollarShark.Application.Categories;
using DollarShark.Application.Common;
using DollarShark.Domain;
using Microsoft.EntityFrameworkCore;

namespace DollarShark.Infrastructure.Persistence;

public sealed class CategoryRuleRepository(FinanceDbContext db, ICurrentUserContext currentUser) : ICategoryRuleRepository
{
    private string UserId => string.IsNullOrWhiteSpace(currentUser.UserId)
        ? throw new InvalidOperationException("A signed-in user is required for finance data access.")
        : currentUser.UserId;

    private IQueryable<CategoryRule> OwnRules() => db.CategoryRules.Where(x => x.UserId == UserId);

    public async Task<IReadOnlyList<CategoryRuleDto>> ListAsync(CancellationToken cancellationToken) =>
        await Project(OwnRules().OrderBy(x => x.Priority).ThenBy(x => x.Pattern).ThenBy(x => x.Id)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CategoryRule>> ListForMatchingAsync(CancellationToken cancellationToken)
    {
        var userId = UserId;
        var isSqlite = SqliteQueryGate.IsSqlite(db);
        if (isSqlite) await SqliteQueryGate.Instance.WaitAsync(cancellationToken);
        try
        {
            // Ignore rules whose category is no longer visible to this user (defensive; deletes cascade).
            return await db.CategoryRules.AsNoTracking().Include(x => x.Category)
                .Where(x => x.UserId == userId && x.Category != null && (x.Category.IsSystem || x.Category.UserId == userId))
                .ToListAsync(cancellationToken);
        }
        finally
        {
            if (isSqlite) SqliteQueryGate.Instance.Release();
        }
    }

    public Task<CategoryRule?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        OwnRules().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<CategoryRuleDto?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        Project(OwnRules().Where(x => x.Id == id)).SingleOrDefaultAsync(cancellationToken);

    public Task<bool> CategoryIsVisibleAsync(Guid categoryId, CancellationToken cancellationToken)
    {
        var userId = UserId;
        return db.Categories.AnyAsync(x => x.Id == categoryId && (x.IsSystem || x.UserId == userId), cancellationToken);
    }

    public Task<bool> DuplicateExistsAsync(string pattern, CategoryRuleMatchType matchType, Guid? exceptId, CancellationToken cancellationToken)
    {
        var normalized = pattern.ToUpperInvariant();
        return OwnRules().AnyAsync(x => x.MatchType == matchType && x.Pattern.ToUpper() == normalized &&
            (!exceptId.HasValue || x.Id != exceptId.Value), cancellationToken);
    }

    public Task<int> CountAsync(CancellationToken cancellationToken) => OwnRules().CountAsync(cancellationToken);

    public async Task<IReadOnlyList<Transaction>> ListUncategorizedTransactionsAsync(CancellationToken cancellationToken)
    {
        var userId = UserId;
        return await db.Transactions.Where(x => x.UserId == userId && x.CategoryId == null).ToListAsync(cancellationToken);
    }

    public async Task AddAsync(CategoryRule rule, CancellationToken cancellationToken)
    {
        // Ownership always comes from the signed-in user, never from the request.
        rule.UserId = UserId;
        await db.CategoryRules.AddAsync(rule, cancellationToken);
    }

    public void Delete(CategoryRule rule) => db.CategoryRules.Remove(rule);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    private static IQueryable<CategoryRuleDto> Project(IQueryable<CategoryRule> query) =>
        query.Select(x => new CategoryRuleDto(x.Id, x.Pattern, x.MatchType.ToString(), x.CategoryId,
            x.Category == null ? string.Empty : x.Category.Name, x.Priority, x.CreatedAtUtc));
}
