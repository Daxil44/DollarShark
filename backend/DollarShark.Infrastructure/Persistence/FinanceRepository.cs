using DollarShark.Application.Accounts;
using DollarShark.Application.Categories;
using DollarShark.Application.Common;
using DollarShark.Application.Transactions;
using DollarShark.Domain;
using Microsoft.EntityFrameworkCore;

namespace DollarShark.Infrastructure.Persistence;

public sealed class FinanceRepository(FinanceDbContext db, ICurrentUserContext currentUser) : IAccountRepository, ICategoryRepository, ITransactionRepository
{
    private string UserId => string.IsNullOrWhiteSpace(currentUser.UserId)
        ? throw new InvalidOperationException("A signed-in user is required for finance data access.")
        : currentUser.UserId;

    public async Task<IReadOnlyList<AccountDto>> ListAsync(bool includeInactive, CancellationToken cancellationToken) =>
        await db.FinancialAccounts.AsNoTracking().Where(x => x.UserId == UserId && (includeInactive || x.IsActive))
            .OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Select(x => new AccountDto(x.Id, x.Name, x.Institution, x.AccountType, x.Currency, x.LastFourDigits, x.IsActive, x.CreatedAtUtc))
            .ToListAsync(cancellationToken);

    async Task<AccountDto?> IAccountRepository.GetAsync(Guid id, CancellationToken cancellationToken) =>
        await db.FinancialAccounts.AsNoTracking().Where(x => x.Id == id && x.UserId == UserId)
            .Select(x => new AccountDto(x.Id, x.Name, x.Institution, x.AccountType, x.Currency, x.LastFourDigits, x.IsActive, x.CreatedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);

    Task<FinancialAccount?> IAccountRepository.FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.FinancialAccounts.SingleOrDefaultAsync(x => x.Id == id && x.UserId == UserId, cancellationToken);

    public async Task AddAsync(FinancialAccount account, CancellationToken cancellationToken)
    {
        account.UserId = UserId;
        await db.FinancialAccounts.AddAsync(account, cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public async Task<IReadOnlyList<CategoryDto>> ListAsync(CancellationToken cancellationToken) =>
        await CategoryProjection(VisibleCategories().OrderBy(x => x.Name).ThenBy(x => x.Id)).ToListAsync(cancellationToken);

    async Task<CategoryDto?> ICategoryRepository.GetAsync(Guid id, CancellationToken cancellationToken) =>
        await CategoryProjection(VisibleCategories().Where(x => x.Id == id)).SingleOrDefaultAsync(cancellationToken);

    Task<Category?> ICategoryRepository.FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Categories.SingleOrDefaultAsync(x => x.Id == id && !x.IsSystem && x.UserId == UserId, cancellationToken);

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) =>
        VisibleCategories().AnyAsync(x => x.Id == id, cancellationToken);

    public Task<bool> NameExistsAsync(string name, Guid? parentId, Guid? exceptId, CancellationToken cancellationToken) =>
        VisibleCategories().AnyAsync(x => x.Name == name && x.ParentCategoryId == parentId && (!exceptId.HasValue || x.Id != exceptId.Value), cancellationToken);

    public async Task<bool> WouldCreateCycleAsync(Guid categoryId, Guid? proposedParentId, CancellationToken cancellationToken)
    {
        var current = proposedParentId;
        while (current is Guid parentId)
        {
            if (parentId == categoryId) return true;
            current = await VisibleCategories().AsNoTracking().Where(x => x.Id == parentId)
                .Select(x => x.ParentCategoryId).SingleOrDefaultAsync(cancellationToken);
        }
        return false;
    }

    public Task<bool> HasChildrenAsync(Guid id, CancellationToken cancellationToken) =>
        db.Categories.AnyAsync(x => x.ParentCategoryId == id && x.UserId == UserId, cancellationToken);

    public Task<bool> HasTransactionsAsync(Guid id, CancellationToken cancellationToken) =>
        db.Transactions.AnyAsync(x => x.CategoryId == id && x.UserId == UserId, cancellationToken);

    public async Task AddAsync(Category category, CancellationToken cancellationToken)
    {
        category.UserId = UserId;
        category.IsSystem = false;
        await db.Categories.AddAsync(category, cancellationToken);
    }

    public void Delete(Category category) => db.Categories.Remove(category);

    async Task<TransactionPage> ITransactionRepository.ListAsync(TransactionFilter filter, CancellationToken cancellationToken)
    {
        IQueryable<Transaction> query = db.Transactions.AsNoTracking().Where(x => x.UserId == UserId);
        if (filter.Year is int year) query = query.Where(x => x.TransactionDate.Year == year);
        if (filter.Month is int month) query = query.Where(x => x.TransactionDate.Month == month);
        if (filter.AccountId is Guid accountId) query = query.Where(x => x.FinancialAccountId == accountId);
        if (filter.CategoryId is Guid categoryId) query = query.Where(x => x.CategoryId == categoryId);
        if (filter.Type is TransactionType type) query = query.Where(x => x.TransactionType == type);
        if (!string.IsNullOrWhiteSpace(filter.Search)) query = query.Where(x => x.Description.Contains(filter.Search));
        if (filter.From is DateOnly from) query = query.Where(x => x.TransactionDate >= from);
        if (filter.To is DateOnly to) query = query.Where(x => x.TransactionDate <= to);

        IOrderedQueryable<Transaction> ordered = (filter.SortBy, filter.Descending) switch
        {
            ("amount", false) => query.OrderBy(x => x.Amount),
            ("amount", true) => query.OrderByDescending(x => x.Amount),
            ("description", false) => query.OrderBy(x => x.Description),
            ("description", true) => query.OrderByDescending(x => x.Description),
            ("type", false) => query.OrderBy(x => x.TransactionType),
            ("type", true) => query.OrderByDescending(x => x.TransactionType),
            ("date", false) => query.OrderBy(x => x.TransactionDate),
            _ => query.OrderByDescending(x => x.TransactionDate)
        };
        ordered = ordered.ThenByDescending(x => x.Id);
        var total = await query.CountAsync(cancellationToken);
        var skip = Math.Min((long)(filter.Page - 1) * filter.PageSize, int.MaxValue);
        var items = await ordered.Skip((int)skip).Take(filter.PageSize)
            .Select(x => new TransactionDto(
                x.Id, x.TransactionDate, x.Description, x.Amount, x.TransactionType.ToString(), x.Currency,
                x.FinancialAccountId, x.FinancialAccount.Name, x.CategoryId, x.Category == null ? null : x.Category.Name, x.CreatedAtUtc))
            .ToListAsync(cancellationToken);
        return new TransactionPage(items, filter.Page, filter.PageSize, total,
            total == 0 ? 0 : (int)Math.Ceiling(total / (double)filter.PageSize));
    }

    async Task<TransactionDto?> ITransactionRepository.GetAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Transactions.AsNoTracking().Where(x => x.Id == id && x.UserId == UserId)
            .Select(x => new TransactionDto(
                x.Id, x.TransactionDate, x.Description, x.Amount, x.TransactionType.ToString(), x.Currency,
                x.FinancialAccountId, x.FinancialAccount.Name, x.CategoryId, x.Category == null ? null : x.Category.Name, x.CreatedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);

    Task<Transaction?> ITransactionRepository.FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Transactions.SingleOrDefaultAsync(x => x.Id == id && x.UserId == UserId, cancellationToken);

    public Task<bool> AccountExistsAsync(Guid id, CancellationToken cancellationToken) =>
        db.FinancialAccounts.AnyAsync(x => x.Id == id && x.UserId == UserId, cancellationToken);

    public Task<string?> GetAccountCurrencyAsync(Guid id, CancellationToken cancellationToken) =>
        db.FinancialAccounts.AsNoTracking().Where(x => x.Id == id && x.UserId == UserId)
            .Select(x => x.Currency).SingleOrDefaultAsync(cancellationToken);

    public Task<bool> CategoryExistsAsync(Guid id, CancellationToken cancellationToken) =>
        VisibleCategories().AnyAsync(x => x.Id == id, cancellationToken);

    public async Task AddAsync(Transaction transaction, CancellationToken cancellationToken)
    {
        transaction.UserId = UserId;
        await db.Transactions.AddAsync(transaction, cancellationToken);
    }

    public void Delete(Transaction transaction) => db.Transactions.Remove(transaction);

    private IQueryable<Category> VisibleCategories() =>
        db.Categories.Where(x => x.IsSystem || x.UserId == UserId);

    private IQueryable<CategoryDto> CategoryProjection(IQueryable<Category> query) =>
        query.Select(x => new CategoryDto(
            x.Id, x.Name, x.ParentCategoryId, x.ParentCategory == null ? null : x.ParentCategory.Name,
            x.Transactions.Count(t => t.UserId == UserId),
            x.Children.Count(c => c.UserId == UserId), x.IsSystem));
}
