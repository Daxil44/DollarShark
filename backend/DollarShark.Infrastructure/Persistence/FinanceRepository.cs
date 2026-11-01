using DollarShark.Application.Accounts;
using DollarShark.Application.Categories;
using DollarShark.Application.Common;
using DollarShark.Application.Transactions;
using DollarShark.Application.Transfers;
using DollarShark.Domain;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace DollarShark.Infrastructure.Persistence;

public sealed class FinanceRepository(FinanceDbContext db, ICurrentUserContext currentUser) : IAccountRepository, ICategoryRepository, ITransactionRepository, ITransferRepository
{
    private static readonly SemaphoreSlim SqliteDuplicateQueryGate = new(1, 1);
    private string UserId => string.IsNullOrWhiteSpace(currentUser.UserId)
        ? throw new InvalidOperationException("A signed-in user is required for finance data access.")
        : currentUser.UserId;

    public async Task<IReadOnlyList<AccountDto>> ListAsync(bool includeInactive, CancellationToken cancellationToken)
    {
        var accounts = await db.FinancialAccounts.AsNoTracking()
            .Where(x => x.UserId == UserId && (includeInactive || x.IsActive))
            .OrderBy(x => x.Name).ThenBy(x => x.Id).ToListAsync(cancellationToken);
        return await ToAccountDtosAsync(accounts, cancellationToken);
    }

    async Task<AccountDto?> IAccountRepository.GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var account = await db.FinancialAccounts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.UserId == UserId, cancellationToken);
        if (account is null) return null;
        return (await ToAccountDtosAsync([account], cancellationToken)).Single();
    }

    private async Task<IReadOnlyList<AccountDto>> ToAccountDtosAsync(
        IReadOnlyList<FinancialAccount> accounts, CancellationToken cancellationToken)
    {
        var configuredAccounts = accounts.Where(x => x.OpeningBalance.HasValue && x.OpeningBalanceDate.HasValue).ToArray();
        var movements = new List<Transaction>();
        if (configuredAccounts.Length > 0)
        {
            var accountIds = configuredAccounts.Select(x => x.Id).ToArray();
            var earliestOpeningDate = configuredAccounts.Min(x => x.OpeningBalanceDate!.Value);
            movements = await db.Transactions.AsNoTracking()
                .Where(x => x.UserId == UserId && accountIds.Contains(x.FinancialAccountId) && x.TransactionDate > earliestOpeningDate)
                .Select(x => new Transaction
                {
                    FinancialAccountId = x.FinancialAccountId,
                    TransactionDate = x.TransactionDate,
                    Amount = x.Amount,
                    TransactionType = x.TransactionType,
                    LinkedTransferTransactionId = x.LinkedTransferTransactionId
                }).ToListAsync(cancellationToken);
        }

        return accounts.Select(account => new AccountDto(
            account.Id, account.Name, account.Institution, account.AccountType, account.Classification.ToString(),
            account.Currency, account.LastFourDigits, account.OpeningBalance, account.OpeningBalanceDate,
            account.CalculateBalance(movements), account.IsActive, account.CreatedAtUtc)).ToArray();
    }

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
        if (!string.IsNullOrWhiteSpace(filter.Search))
            query = query.Where(x => x.Description.Contains(filter.Search) ||
                (x.SubDescription != null && x.SubDescription.Contains(filter.Search)));
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
                x.FinancialAccountId, x.FinancialAccount.Name, x.CategoryId, x.Category == null ? null : x.Category.Name,
                x.CreatedAtUtc, x.SubDescription,
                x.LinkedTransferTransaction != null && x.LinkedTransferTransaction.UserId == UserId ? x.LinkedTransferTransactionId : null,
                x.LinkedTransferTransaction == null || x.LinkedTransferTransaction.UserId != UserId ? null : new LinkedTransferTransactionDto(
                    x.LinkedTransferTransaction.Id, x.LinkedTransferTransaction.TransactionDate,
                    x.LinkedTransferTransaction.Description, x.LinkedTransferTransaction.Amount,
                    x.LinkedTransferTransaction.Currency, x.LinkedTransferTransaction.FinancialAccountId,
                    x.LinkedTransferTransaction.FinancialAccount.Name)))
            .ToListAsync(cancellationToken);
        return new TransactionPage(items, filter.Page, filter.PageSize, total,
            total == 0 ? 0 : (int)Math.Ceiling(total / (double)filter.PageSize));
    }

    async Task<TransactionDto?> ITransactionRepository.GetAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Transactions.AsNoTracking().Where(x => x.Id == id && x.UserId == UserId)
            .Select(x => new TransactionDto(
                x.Id, x.TransactionDate, x.Description, x.Amount, x.TransactionType.ToString(), x.Currency,
                x.FinancialAccountId, x.FinancialAccount.Name, x.CategoryId, x.Category == null ? null : x.Category.Name,
                x.CreatedAtUtc, x.SubDescription,
                x.LinkedTransferTransaction != null && x.LinkedTransferTransaction.UserId == UserId ? x.LinkedTransferTransactionId : null,
                x.LinkedTransferTransaction == null || x.LinkedTransferTransaction.UserId != UserId ? null : new LinkedTransferTransactionDto(
                    x.LinkedTransferTransaction.Id, x.LinkedTransferTransaction.TransactionDate,
                    x.LinkedTransferTransaction.Description, x.LinkedTransferTransaction.Amount,
                    x.LinkedTransferTransaction.Currency, x.LinkedTransferTransaction.FinancialAccountId,
                    x.LinkedTransferTransaction.FinancialAccount.Name)))
            .SingleOrDefaultAsync(cancellationToken);

    Task<Transaction?> ITransactionRepository.FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Transactions.SingleOrDefaultAsync(x => x.Id == id && x.UserId == UserId, cancellationToken);

    public Task<bool> AccountExistsAsync(Guid id, CancellationToken cancellationToken) =>
        db.FinancialAccounts.AnyAsync(x => x.Id == id && x.UserId == UserId, cancellationToken);

    public Task<string?> GetAccountCurrencyAsync(Guid id, CancellationToken cancellationToken) =>
        db.FinancialAccounts.AsNoTracking().Where(x => x.Id == id && x.UserId == UserId)
            .Select(x => x.Currency).SingleOrDefaultAsync(cancellationToken);

    public async Task<TransferOperationResult> LinkAsync(
        Guid outgoingTransactionId, Guid incomingTransactionId, CancellationToken cancellationToken)
    {
        await using var databaseTransaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var userId = UserId;
            var ids = new[] { outgoingTransactionId, incomingTransactionId };
            var pair = await db.Transactions.Include(x => x.FinancialAccount)
                .Where(x => ids.Contains(x.Id) && x.UserId == userId)
                .ToListAsync(cancellationToken);
            var outgoing = pair.SingleOrDefault(x => x.Id == outgoingTransactionId);
            var incoming = pair.SingleOrDefault(x => x.Id == incomingTransactionId);
            if (outgoing is null || incoming is null)
            {
                await databaseTransaction.RollbackAsync(cancellationToken);
                return new TransferOperationResult(TransferOperationState.NotFound);
            }

            var validationError = LinkedTransferRules.Validate(outgoing, incoming);
            if (validationError is not null)
            {
                await databaseTransaction.RollbackAsync(cancellationToken);
                return new TransferOperationResult(TransferOperationState.Invalid, ValidationError: validationError);
            }

            outgoing.LinkedTransferTransactionId = incoming.Id;
            incoming.LinkedTransferTransactionId = outgoing.Id;
            await db.SaveChangesAsync(cancellationToken);
            await databaseTransaction.CommitAsync(cancellationToken);
            return new TransferOperationResult(TransferOperationState.Succeeded, ToTransferPair(outgoing, incoming));
        }
        catch (DbUpdateException)
        {
            await databaseTransaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            return new TransferOperationResult(TransferOperationState.Conflict);
        }
    }

    public async Task<TransferOperationResult> UnlinkAsync(Guid transactionId, CancellationToken cancellationToken)
    {
        await using var databaseTransaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var userId = UserId;
        var transaction = await db.Transactions.Include(x => x.FinancialAccount)
            .SingleOrDefaultAsync(x => x.Id == transactionId && x.UserId == userId, cancellationToken);
        if (transaction is null)
        {
            await databaseTransaction.RollbackAsync(cancellationToken);
            return new TransferOperationResult(TransferOperationState.NotFound);
        }
        if (transaction.LinkedTransferTransactionId is not Guid counterpartId)
        {
            await databaseTransaction.RollbackAsync(cancellationToken);
            return new TransferOperationResult(TransferOperationState.Invalid);
        }

        var counterpart = await db.Transactions.Include(x => x.FinancialAccount)
            .SingleOrDefaultAsync(x => x.Id == counterpartId && x.UserId == userId, cancellationToken);
        if (counterpart is null || counterpart.LinkedTransferTransactionId != transaction.Id)
        {
            await databaseTransaction.RollbackAsync(cancellationToken);
            return new TransferOperationResult(TransferOperationState.Conflict);
        }

        var outgoing = transaction.Amount < 0 ? transaction : counterpart;
        var incoming = transaction.Amount > 0 ? transaction : counterpart;
        transaction.LinkedTransferTransactionId = null;
        counterpart.LinkedTransferTransactionId = null;
        await db.SaveChangesAsync(cancellationToken);
        await databaseTransaction.CommitAsync(cancellationToken);
        return new TransferOperationResult(TransferOperationState.Succeeded, ToTransferPair(outgoing, incoming));
    }

    public async Task<IReadOnlyList<TransferSuggestionDto>> GetSuggestionsAsync(CancellationToken cancellationToken)
    {
        var candidates = await db.Transactions.AsNoTracking()
            .Where(x => x.UserId == UserId && x.FinancialAccount.UserId == UserId && x.TransactionType == TransactionType.Transfer &&
                x.LinkedTransferTransactionId == null && x.Amount != 0 && x.Currency == x.FinancialAccount.Currency)
            .OrderByDescending(x => x.TransactionDate)
            .Select(x => new LinkedTransferTransactionDto(x.Id, x.TransactionDate, x.Description, x.Amount,
                x.Currency, x.FinancialAccountId, x.FinancialAccount.Name))
            .ToListAsync(cancellationToken);

        var suggestions = new List<(TransferSuggestionDto Suggestion, int DescriptionScore)>();
        foreach (var group in candidates.GroupBy(x => (Currency: x.Currency.ToUpperInvariant(), Amount: decimal.Abs(x.Amount))))
        {
            var outgoing = group.Where(x => x.Amount < 0).ToArray();
            var incoming = group.Where(x => x.Amount > 0).ToArray();
            foreach (var source in outgoing)
            foreach (var destination in incoming)
            {
                if (source.FinancialAccountId == destination.FinancialAccountId) continue;
                var daysApart = Math.Abs(source.TransactionDate.DayNumber - destination.TransactionDate.DayNumber);
                if (daysApart > 5) continue;
                var descriptionScore = DescriptionSimilarity(source.Description, destination.Description);
                var details = descriptionScore >= 50 ? "; descriptions are similar" : string.Empty;
                var reason = $"Equal absolute amount, opposite directions, and dates {daysApart} day(s) apart{details}.";
                suggestions.Add((new TransferSuggestionDto(source, destination, daysApart, reason), descriptionScore));
            }
        }

        return suggestions.OrderByDescending(x => x.DescriptionScore).ThenBy(x => x.Suggestion.DaysApart)
            .ThenBy(x => x.Suggestion.Outgoing.TransactionDate).Take(100).Select(x => x.Suggestion).ToArray();
    }

    private static TransferPairDto ToTransferPair(Transaction outgoing, Transaction incoming) =>
        new(ToTransferSummary(outgoing), ToTransferSummary(incoming));

    private static LinkedTransferTransactionDto ToTransferSummary(Transaction transaction) =>
        new(transaction.Id, transaction.TransactionDate, transaction.Description, transaction.Amount,
            transaction.Currency, transaction.FinancialAccountId, transaction.FinancialAccount.Name);

    private static int DescriptionSimilarity(string left, string right)
    {
        static HashSet<string> Words(string description) => new(new string(description.ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : ' ').ToArray())
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);

        var leftWords = Words(left);
        var rightWords = Words(right);
        var union = leftWords.Union(rightWords).Count();
        return union == 0 ? 0 : (int)Math.Round(leftWords.Intersect(rightWords).Count() * 100d / union);
    }

    public Task<bool> CategoryExistsAsync(Guid id, CancellationToken cancellationToken) =>
        VisibleCategories().AnyAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<DuplicateTransactionCandidate>> FindDuplicateCandidatesAsync(
        Guid accountId, DateOnly? from, DateOnly? to, IReadOnlyCollection<string> externalReferences,
        CancellationToken cancellationToken)
    {
        var isSqlite = db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite";
        if (isSqlite) await SqliteDuplicateQueryGate.WaitAsync(cancellationToken);
        try
        {
            var results = new Dictionary<Guid, DuplicateTransactionCandidate>();
            if (from is DateOnly start && to is DateOnly end)
            {
                var dateMatches = await DuplicateTransactionQuery(accountId)
                    .Where(x => x.TransactionDate >= start && x.TransactionDate <= end)
                    .OrderByDescending(x => x.TransactionDate)
                    .Select(x => new DuplicateTransactionCandidate(x.Id, x.TransactionDate, x.Description, x.Amount,
                        x.Currency, x.FinancialAccount.Name, x.ExternalReference))
                    .ToListAsync(cancellationToken);
                foreach (var match in dateMatches) results[match.Id] = match;
            }

            foreach (var referenceBatch in externalReferences.Chunk(1_000))
            {
                var referenceMatches = await DuplicateTransactionQuery(accountId)
                    .Where(x => x.ExternalReference != null && referenceBatch.Contains(x.ExternalReference))
                    .Select(x => new DuplicateTransactionCandidate(x.Id, x.TransactionDate, x.Description, x.Amount,
                        x.Currency, x.FinancialAccount.Name, x.ExternalReference))
                    .ToListAsync(cancellationToken);
                foreach (var match in referenceMatches) results[match.Id] = match;
            }
            return results.Values.ToArray();
        }
        finally
        {
            if (isSqlite) SqliteDuplicateQueryGate.Release();
        }
    }

    private IQueryable<Transaction> DuplicateTransactionQuery(Guid accountId) =>
        db.Transactions.AsNoTracking().Where(x => x.UserId == UserId && x.FinancialAccountId == accountId);

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
