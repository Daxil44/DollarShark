using DollarShark.Application.Common;
using DollarShark.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace DollarShark.Application.Statements;

public sealed class StatementUploadOptions
{
    public long MaxFileSizeBytes { get; set; } = 15 * 1024 * 1024;
}

public sealed record StatementUploadCommand(Guid FinancialAccountId, string OriginalFileName, long FileSizeBytes, Stream Content);
public sealed record StatementStorageResult(string StorageKey, long FileSizeBytes, string ContentType, string FileHash);
public sealed record StatementImportDto(
    Guid Id, Guid FinancialAccountId, string AccountName, string OriginalFileName,
    long FileSizeBytes, string ContentType, string Status, DateTime UploadedAtUtc, DateTime? ProcessedAtUtc);
public sealed record StatementImportPage(
    IReadOnlyList<StatementImportDto> Items, int Page, int PageSize, int TotalCount, int TotalPages);

public interface IStatementFileValidator
{
    Task<bool> IsValidAsync(Stream content, string extension, CancellationToken cancellationToken);
}

public interface IStatementStorage
{
    Task<StatementStorageResult> StoreAsync(Stream content, string extension, long maxFileSizeBytes, CancellationToken cancellationToken);
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken);
}

public interface IStatementImportRepository
{
    Task<string?> GetAccountNameForUserAsync(Guid accountId, string userId, CancellationToken cancellationToken);
    Task AddAsync(StatementImport statement, CancellationToken cancellationToken);
    Task<IReadOnlyList<StatementImportDto>> ListAsync(string userId, int page, int pageSize, CancellationToken cancellationToken);
    Task<int> CountAsync(string userId, CancellationToken cancellationToken);
    Task<StatementImportDto?> GetAsync(Guid id, string userId, CancellationToken cancellationToken);
    Task<StatementImport?> GetTrackedAsync(Guid id, string userId, CancellationToken cancellationToken);
    void Delete(StatementImport statement);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IStatementImportService
{
    Task<UseCaseResult<StatementImportDto>> UploadAsync(StatementUploadCommand command, CancellationToken cancellationToken);
    Task<UseCaseResult<StatementImportPage>> ListAsync(int page, int pageSize, CancellationToken cancellationToken);
    Task<UseCaseResult<StatementImportDto>> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<UseCaseResult<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken);
}

public sealed class StatementImportService(
    ICurrentUserContext currentUser,
    IStatementImportRepository repository,
    IStatementFileValidator validator,
    IStatementStorage storage,
    StatementUploadOptions options,
    ILogger<StatementImportService> logger) : IStatementImportService
{
    private const int MaxFileNameLength = 255;
    private static readonly IReadOnlyDictionary<string, string> AllowedTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".csv"] = "text/csv",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
    };

    public async Task<UseCaseResult<StatementImportDto>> UploadAsync(StatementUploadCommand command, CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        if (command.FinancialAccountId == Guid.Empty)
            return UseCaseResult<StatementImportDto>.Invalid(Error("financialAccountId", "Select a valid financial account."));
        var accountName = await repository.GetAccountNameForUserAsync(command.FinancialAccountId, userId, cancellationToken);
        if (accountName is null)
            return UseCaseResult<StatementImportDto>.NotFound("Financial account was not found.");
        if (command.FileSizeBytes <= 0)
            return UseCaseResult<StatementImportDto>.Invalid(Error("file", "Choose a non-empty statement file."));
        if (command.FileSizeBytes > options.MaxFileSizeBytes)
            return new UseCaseResult<StatementImportDto>(null, StatusCodes.Status413PayloadTooLarge, "The statement file exceeds the configured size limit.");

        var fileName = SafeFileName(command.OriginalFileName);
        if (fileName is null)
            return UseCaseResult<StatementImportDto>.Invalid(Error("file", "The statement filename is invalid."));
        var extension = Path.GetExtension(fileName);
        if (!AllowedTypes.ContainsKey(extension))
            return new UseCaseResult<StatementImportDto>(null, StatusCodes.Status415UnsupportedMediaType, "Upload a PDF, CSV, or XLSX statement.");

        if (command.Content.CanSeek) command.Content.Position = 0;
        if (!await validator.IsValidAsync(command.Content, extension, cancellationToken))
            return UseCaseResult<StatementImportDto>.Invalid(Error("file", "The file content does not match a valid supported statement format."));
        if (command.Content.CanSeek) command.Content.Position = 0;

        var stored = await storage.StoreAsync(command.Content, extension.ToLowerInvariant(), options.MaxFileSizeBytes, cancellationToken);
        var statement = new StatementImport
        {
            UserId = userId,
            FinancialAccountId = command.FinancialAccountId,
            OriginalFileName = fileName,
            StorageKey = stored.StorageKey,
            FileSizeBytes = stored.FileSizeBytes,
            ContentType = stored.ContentType,
            FileHash = stored.FileHash,
            Status = StatementImportStatus.Uploaded,
            UploadedAtUtc = DateTime.UtcNow
        };

        try
        {
            await repository.AddAsync(statement, cancellationToken);
            await repository.SaveChangesAsync(cancellationToken);
            return UseCaseResult<StatementImportDto>.Created(ToDto(statement, accountName));
        }
        catch
        {
            try
            {
                await storage.DeleteAsync(stored.StorageKey, CancellationToken.None);
            }
            catch (Exception cleanupException)
            {
                logger.LogError(cleanupException, "Failed to clean up statement storage object after a database failure.");
            }
            throw;
        }
    }

    public async Task<UseCaseResult<StatementImportPage>> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (page < 1) errors["page"] = ["Page must be at least 1."];
        if (pageSize is < 1 or > 100) errors["pageSize"] = ["Page size must be between 1 and 100."];
        if (errors.Count > 0) return UseCaseResult<StatementImportPage>.Invalid(errors);

        var userId = RequireUserId();
        var totalCount = await repository.CountAsync(userId, cancellationToken);
        var items = await repository.ListAsync(userId, page, pageSize, cancellationToken);
        return UseCaseResult<StatementImportPage>.Ok(new StatementImportPage(items, page, pageSize, totalCount,
            totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize)));
    }

    public async Task<UseCaseResult<StatementImportDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty) return UseCaseResult<StatementImportDto>.Invalid(Error("id", "A valid statement ID is required."));
        var statement = await repository.GetAsync(id, RequireUserId(), cancellationToken);
        return statement is null ? UseCaseResult<StatementImportDto>.NotFound("Statement was not found.") : UseCaseResult<StatementImportDto>.Ok(statement);
    }

    public async Task<UseCaseResult<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty) return UseCaseResult<bool>.Invalid(Error("id", "A valid statement ID is required."));
        var statement = await repository.GetTrackedAsync(id, RequireUserId(), cancellationToken);
        if (statement is null) return UseCaseResult<bool>.NotFound("Statement was not found.");

        // Persist a retryable state first. If file deletion or the final DB delete fails,
        // another DELETE request can safely finish the operation; the file delete is idempotent.
        if (statement.Status != StatementImportStatus.Deleting)
        {
            statement.Status = StatementImportStatus.Deleting;
            await repository.SaveChangesAsync(cancellationToken);
        }
        await storage.DeleteAsync(statement.StorageKey, cancellationToken);
        repository.Delete(statement);
        await repository.SaveChangesAsync(cancellationToken);
        return UseCaseResult<bool>.NoContent(true);
    }

    private string RequireUserId() => string.IsNullOrWhiteSpace(currentUser.UserId)
        ? throw new InvalidOperationException("An authenticated user is required for statement operations.")
        : currentUser.UserId;

    private static string? SafeFileName(string? originalFileName)
    {
        if (string.IsNullOrWhiteSpace(originalFileName)) return null;
        var basename = originalFileName.Replace('\\', '/').Split('/').LastOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(basename) || basename.Any(char.IsControl)) return null;
        return basename.Length <= MaxFileNameLength ? basename : basename[^MaxFileNameLength..];
    }

    private static IReadOnlyDictionary<string, string[]> Error(string key, string message) =>
        new Dictionary<string, string[]> { [key] = [message] };

    private static StatementImportDto ToDto(StatementImport statement, string accountName) =>
        new(statement.Id, statement.FinancialAccountId, accountName, statement.OriginalFileName,
            statement.FileSizeBytes, statement.ContentType, statement.Status.ToString(), statement.UploadedAtUtc, statement.ProcessedAtUtc);
}
