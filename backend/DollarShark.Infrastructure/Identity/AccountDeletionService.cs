using DollarShark.Application.Statements;
using DollarShark.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DollarShark.Infrastructure.Identity;

public sealed record AccountDeletionResult(bool Succeeded, int FilesDeleted, int FilesFailed);

/// <summary>
/// Permanently removes a user and everything they own: transactions, accounts, private categories, rules,
/// statement records and stored statement files. Shared default categories and other users are untouched.
/// </summary>
public sealed class AccountDeletionService(
    FinanceDbContext db,
    UserManager<ApplicationUser> users,
    IStatementStorage storage,
    ILogger<AccountDeletionService> logger)
{
    public async Task<AccountDeletionResult> DeleteAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var userId = user.Id;
        // Remember file locations before the database rows that point at them are gone.
        var storageKeys = await db.StatementImports.Where(x => x.UserId == userId).Select(x => x.StorageKey).ToListAsync(cancellationToken);

        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            // Order matters: the user's rows reference each other (and the user) with restrict foreign keys.
            await db.Transactions.Where(x => x.UserId == userId && x.LinkedTransferTransactionId != null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.LinkedTransferTransactionId, (Guid?)null), cancellationToken);
            await db.Transactions.Where(x => x.UserId == userId).ExecuteDeleteAsync(cancellationToken);
            await db.StatementImports.Where(x => x.UserId == userId).ExecuteDeleteAsync(cancellationToken);
            await db.CategoryRules.Where(x => x.UserId == userId).ExecuteDeleteAsync(cancellationToken);
            await db.Categories.Where(x => x.UserId == userId && !x.IsSystem && x.ParentCategoryId != null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ParentCategoryId, (Guid?)null), cancellationToken);
            await db.Categories.Where(x => x.UserId == userId && !x.IsSystem).ExecuteDeleteAsync(cancellationToken);
            await db.FinancialAccounts.Where(x => x.UserId == userId).ExecuteDeleteAsync(cancellationToken);

            // Identity removes its own rows (external logins such as Google, claims, tokens) with the user.
            var result = await users.DeleteAsync(user);
            if (!result.Succeeded)
            {
                logger.LogError("Deleting user {UserId} failed: {Errors}", userId, string.Join("; ", result.Errors.Select(e => e.Code)));
                return new AccountDeletionResult(false, 0, 0);
            }
            await transaction.CommitAsync(cancellationToken);
        }

        // Files are removed only after the database commit, so a failed delete never leaves records without files.
        int deleted = 0, failed = 0;
        foreach (var key in storageKeys)
        {
            try
            {
                await storage.DeleteAsync(key, CancellationToken.None);
                deleted++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                failed++;
                // Never log the user's data; the storage key alone lets an operator remove the orphaned file.
                logger.LogError(exception, "Could not delete statement file {StorageKey} for a deleted account.", key);
            }
        }
        logger.LogInformation("Deleted account {UserId}: {Deleted} statement files removed, {Failed} failed.", userId, deleted, failed);
        return new AccountDeletionResult(true, deleted, failed);
    }
}
