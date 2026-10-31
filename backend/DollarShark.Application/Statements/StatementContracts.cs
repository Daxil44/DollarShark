using DollarShark.Application.Common;
using DollarShark.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using DollarShark.Application.Transactions;

namespace DollarShark.Application.Statements;

public sealed class StatementUploadOptions
{
    public long MaxFileSizeBytes { get; set; } = 15 * 1024 * 1024;
}

public sealed class StatementCsvParsingOptions
{
    public int MaxRows { get; set; } = 10_000;
}

public sealed record StatementUploadCommand(Guid FinancialAccountId, string OriginalFileName, long FileSizeBytes, Stream Content, string StatementType = "Bank");
public sealed record StatementStorageResult(string StorageKey, long FileSizeBytes, string ContentType, string FileHash);
public sealed record StatementImportDto(
    Guid Id, Guid FinancialAccountId, string AccountName, string OriginalFileName,
    long FileSizeBytes, string ContentType, string Status, DateTime UploadedAtUtc, DateTime? ProcessedAtUtc,
    string StatementType = "Bank", int ImportedTransactionCount = 0);
public sealed record StatementImportPage(
    IReadOnlyList<StatementImportDto> Items, int Page, int PageSize, int TotalCount, int TotalPages);
public sealed record StatementCsvCandidate(
    int SourceRowNumber,
    DateOnly? TransactionDate,
    DateOnly? PostedDate,
    string Description,
    decimal? Amount,
    string? Currency,
    string? ReferenceNumber,
    IReadOnlyList<string> ValidationMessages,
    string Status,
    DuplicateCheckResult? DuplicateCheck = null,
    string? SubDescription = null,
    string? SourceTransactionType = null,
    decimal? SourceAmount = null,
    string? SuggestedTransactionType = null);
public sealed record StatementCsvPreview(
    IReadOnlyList<StatementCsvCandidate> Candidates,
    int ValidRows,
    int InvalidRows);
public sealed record StatementImportCandidateChange(
    int SourceRowNumber, bool Selected, DateOnly? TransactionDate, string? Description,
    decimal? Amount, string? TransactionType, Guid? CategoryId, string? DuplicateDecision = null,
    Guid? ReviewedDuplicateTransactionId = null, string? ReviewedDuplicateClassification = null);
public sealed record StatementImportConfirmationRequest(IReadOnlyList<StatementImportCandidateChange>? Candidates);
public sealed record StatementImportRejectedRow(int SourceRowNumber, IReadOnlyList<string> Messages, DuplicateCheckResult? DuplicateCheck = null);
public sealed record StatementImportConfirmationResult(
    int CreatedCount, int ExcludedCount, int RejectedCount, IReadOnlyList<StatementImportRejectedRow> RejectedRows);

public interface IStatementImportTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}

public sealed class StatementCsvFormatException(string message) : Exception(message);
public sealed class StatementCsvRowLimitException(int maxRows) : Exception($"The CSV exceeds the maximum of {maxRows} data rows.");

public interface IStatementFileValidator
{
    Task<bool> IsValidAsync(Stream content, string extension, CancellationToken cancellationToken);
}

public interface IStatementStorage
{
    Task<StatementStorageResult> StoreAsync(Stream content, string extension, long maxFileSizeBytes, CancellationToken cancellationToken);
    Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken);
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken);
}

public interface IStatementCsvParser
{
    Task<StatementCsvPreview> ParseAsync(Stream content, int maxRows, CancellationToken cancellationToken);
}

public interface IStatementImportRepository
{
    Task<string?> GetAccountNameForUserAsync(Guid accountId, string userId, CancellationToken cancellationToken);
    Task AddAsync(StatementImport statement, CancellationToken cancellationToken);
    Task<IReadOnlyList<StatementImportDto>> ListAsync(string userId, int page, int pageSize, CancellationToken cancellationToken);
    Task<int> CountAsync(string userId, CancellationToken cancellationToken);
    Task<StatementImportDto?> GetAsync(Guid id, string userId, CancellationToken cancellationToken);
    Task<StatementImport?> GetTrackedAsync(Guid id, string userId, CancellationToken cancellationToken);
    Task<string?> GetAccountCurrencyForUserAsync(Guid accountId, string userId, CancellationToken cancellationToken);
    Task<bool> TryClaimForProcessingAsync(Guid id, string userId, CancellationToken cancellationToken);
    Task<IStatementImportTransaction> BeginTransactionAsync(CancellationToken cancellationToken);
    void Delete(StatementImport statement);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IStatementImportService
{
    Task<UseCaseResult<StatementImportDto>> UploadAsync(StatementUploadCommand command, CancellationToken cancellationToken);
    Task<UseCaseResult<StatementImportPage>> ListAsync(int page, int pageSize, CancellationToken cancellationToken);
    Task<UseCaseResult<StatementImportDto>> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<UseCaseResult<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task<UseCaseResult<StatementCsvPreview>> ParseCsvAsync(Guid id, CancellationToken cancellationToken);
    Task<UseCaseResult<StatementImportConfirmationResult>> ConfirmImportAsync(Guid id, StatementImportConfirmationRequest request, CancellationToken cancellationToken);
}

public sealed class StatementImportService(
    ICurrentUserContext currentUser,
    IStatementImportRepository repository,
    IStatementFileValidator validator,
    IStatementCsvParser csvParser,
    IDuplicateDetectionService duplicateDetection,
    ITransactionService transactionService,
    IStatementStorage storage,
    StatementUploadOptions options,
    StatementCsvParsingOptions csvOptions,
    ILogger<StatementImportService> logger) : IStatementImportService
{
    private static readonly Dictionary<Guid, ConfirmationGate> ConfirmationLocks = [];
    private static readonly object ConfirmationLocksSync = new();
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
        if (command.StatementType is not ("Bank" or "CreditCard"))
            return UseCaseResult<StatementImportDto>.Invalid(Error("statementType", "Choose Bank or CreditCard."));
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
            StatementType = command.StatementType,
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

    public async Task<UseCaseResult<StatementCsvPreview>> ParseCsvAsync(Guid id, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
            return UseCaseResult<StatementCsvPreview>.Invalid(Error("id", "A valid statement ID is required."));

        var statement = await repository.GetTrackedAsync(id, RequireUserId(), cancellationToken);
        if (statement is null) return UseCaseResult<StatementCsvPreview>.NotFound("Statement was not found.");
        if (!string.Equals(Path.GetExtension(statement.OriginalFileName), ".csv", StringComparison.OrdinalIgnoreCase))
            return new UseCaseResult<StatementCsvPreview>(null, StatusCodes.Status415UnsupportedMediaType, "Only CSV statements can be parsed.");
        if (statement.Status == StatementImportStatus.Deleting)
            return UseCaseResult<StatementCsvPreview>.Conflict("This statement is being deleted and cannot be parsed.");
        if (statement.FileSizeBytes <= 0 || statement.FileSizeBytes > options.MaxFileSizeBytes)
            return new UseCaseResult<StatementCsvPreview>(null, StatusCodes.Status413PayloadTooLarge, "The statement file exceeds the configured size limit.");

        try
        {
            await using var content = await storage.OpenReadAsync(statement.StorageKey, cancellationToken);
            if (content.CanSeek && content.Length > options.MaxFileSizeBytes)
                return new UseCaseResult<StatementCsvPreview>(null, StatusCodes.Status413PayloadTooLarge, "The statement file exceeds the configured size limit.");
            var preview = await csvParser.ParseAsync(content, csvOptions.MaxRows, cancellationToken);
            var currency = await repository.GetAccountCurrencyForUserAsync(statement.FinancialAccountId, RequireUserId(), cancellationToken) ?? "CAD";
            var duplicateRequests = preview.Candidates.Select(candidate => new DuplicateDetectionRequest(candidate.TransactionDate,
                candidate.Description, NormalizePreviewAmount(statement.StatementType, candidate.Amount), candidate.Currency ?? currency, candidate.ReferenceNumber)).ToArray();
            var duplicateResults = await duplicateDetection.CheckManyAsync(statement.FinancialAccountId, duplicateRequests, cancellationToken);
            var candidates = preview.Candidates.Select((candidate, index) => candidate with { DuplicateCheck = duplicateResults[index] }).ToArray();
            return UseCaseResult<StatementCsvPreview>.Ok(preview with { Candidates = candidates });
        }
        catch (FileNotFoundException)
        {
            return UseCaseResult<StatementCsvPreview>.Conflict("The stored statement file is unavailable. Upload the file again.");
        }
        catch (DirectoryNotFoundException)
        {
            return UseCaseResult<StatementCsvPreview>.Conflict("The stored statement file is unavailable. Upload the file again.");
        }
        catch (StatementCsvFormatException exception)
        {
            return new UseCaseResult<StatementCsvPreview>(null, StatusCodes.Status422UnprocessableEntity, exception.Message);
        }
        catch (StatementCsvRowLimitException exception)
        {
            return new UseCaseResult<StatementCsvPreview>(null, StatusCodes.Status413PayloadTooLarge, exception.Message);
        }
    }

    public async Task<UseCaseResult<StatementImportConfirmationResult>> ConfirmImportAsync(
        Guid id, StatementImportConfirmationRequest request, CancellationToken cancellationToken)
    {
        ConfirmationGate gate;
        lock (ConfirmationLocksSync)
        {
            if (ConfirmationLocks.TryGetValue(id, out var existing)) gate = existing;
            else ConfirmationLocks[id] = gate = new ConfirmationGate();
            gate.Users++;
        }

        var entered = false;
        try
        {
            await gate.Semaphore.WaitAsync(cancellationToken);
            entered = true;
            return await ConfirmImportCoreAsync(id, request, cancellationToken);
        }
        finally
        {
            if (entered) gate.Semaphore.Release();
            lock (ConfirmationLocksSync)
            {
                gate.Users--;
                if (gate.Users == 0) ConfirmationLocks.Remove(id);
            }
        }
    }

    private sealed class ConfirmationGate
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int Users { get; set; }
    }

    private async Task<UseCaseResult<StatementImportConfirmationResult>> ConfirmImportCoreAsync(
        Guid id, StatementImportConfirmationRequest request, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty) return UseCaseResult<StatementImportConfirmationResult>.Invalid(Error("id", "A valid statement ID is required."));
        var userId = RequireUserId();
        var statement = await repository.GetTrackedAsync(id, userId, cancellationToken);
        if (statement is null) return UseCaseResult<StatementImportConfirmationResult>.NotFound("Statement was not found.");
        if (!string.Equals(Path.GetExtension(statement.OriginalFileName), ".csv", StringComparison.OrdinalIgnoreCase))
            return new UseCaseResult<StatementImportConfirmationResult>(null, StatusCodes.Status415UnsupportedMediaType, "Only CSV statements can be imported.");
        if (statement.Status != StatementImportStatus.Uploaded)
            return UseCaseResult<StatementImportConfirmationResult>.Conflict("This statement has already been processed or cannot be imported in its current state.");
        if (request.Candidates is null)
            return UseCaseResult<StatementImportConfirmationResult>.Invalid(Error("candidates", "Candidate rows are required."));
        if (request.Candidates.Any(candidate => candidate is null))
            return UseCaseResult<StatementImportConfirmationResult>.Invalid(Error("candidates", "Candidate rows cannot be null."));

        StatementCsvPreview preview;
        try
        {
            await using var content = await storage.OpenReadAsync(statement.StorageKey, cancellationToken);
            preview = await csvParser.ParseAsync(content, csvOptions.MaxRows, cancellationToken);
        }
        catch (FileNotFoundException) { return UseCaseResult<StatementImportConfirmationResult>.Conflict("The stored statement file is unavailable. Upload the file again."); }
        catch (DirectoryNotFoundException) { return UseCaseResult<StatementImportConfirmationResult>.Conflict("The stored statement file is unavailable. Upload the file again."); }
        catch (StatementCsvFormatException exception) { return new UseCaseResult<StatementImportConfirmationResult>(null, StatusCodes.Status422UnprocessableEntity, exception.Message); }
        catch (StatementCsvRowLimitException exception) { return new UseCaseResult<StatementImportConfirmationResult>(null, StatusCodes.Status413PayloadTooLarge, exception.Message); }

        var sourceByNumber = preview.Candidates.ToDictionary(x => x.SourceRowNumber);
        if (request.Candidates.Count != sourceByNumber.Count || request.Candidates.Select(x => x.SourceRowNumber).Distinct().Count() != request.Candidates.Count ||
            request.Candidates.Any(x => !sourceByNumber.ContainsKey(x.SourceRowNumber)))
            return UseCaseResult<StatementImportConfirmationResult>.Invalid(Error("candidates", "Submitted rows do not match the uploaded CSV. Parse the statement again and retry."));

        var selected = request.Candidates.Where(x => x.Selected).ToArray();

        var currency = await repository.GetAccountCurrencyForUserAsync(statement.FinancialAccountId, userId, cancellationToken);
        if (currency is null) return UseCaseResult<StatementImportConfirmationResult>.NotFound("The statement's financial account was not found.");
        var rejected = new List<StatementImportRejectedRow>();
        var duplicateRequests = request.Candidates.Select(candidate =>
        {
            var source = sourceByNumber[candidate.SourceRowNumber];
            decimal? amount = candidate.Amount is null ? null : NormalizeImportAmount(statement.StatementType, candidate.Amount.Value, candidate.TransactionType);
            return new DuplicateDetectionRequest(candidate.TransactionDate, candidate.Description, amount,
                source.Currency ?? currency, source.ReferenceNumber);
        }).ToArray();
        var duplicateResults = await duplicateDetection.CheckManyAsync(statement.FinancialAccountId, duplicateRequests, cancellationToken);
        var duplicateChecks = request.Candidates.Select((candidate, index) => (candidate.SourceRowNumber, Result: duplicateResults[index]))
            .ToDictionary(x => x.SourceRowNumber, x => x.Result);
        foreach (var candidate in selected)
        {
            var messages = new List<string>();
            if (!sourceByNumber.TryGetValue(candidate.SourceRowNumber, out var source))
                messages.Add("The selected row is not part of this statement.");
            if (candidate.TransactionDate is null || candidate.TransactionDate.Value.Year < 1900 || candidate.TransactionDate > DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)))
                messages.Add("Enter a valid transaction date no earlier than 1900 and no later than tomorrow.");
            if (string.IsNullOrWhiteSpace(candidate.Description) || candidate.Description.Trim().Length > 300)
                messages.Add("Description is required and must be at most 300 characters.");
            if (candidate.Amount is null || candidate.Amount == 0 || decimal.Abs(candidate.Amount.Value) >= 1_000_000_000_000_000m || decimal.Round(candidate.Amount.Value, 4) != candidate.Amount.Value)
                messages.Add("Amount must be nonzero, fit decimal(19,4), and have at most four decimal places.");
            if (source?.Currency is not null && !string.Equals(source.Currency, currency, StringComparison.OrdinalIgnoreCase))
                messages.Add($"The CSV currency ({source.Currency}) does not match the account currency ({currency}).");
            if (candidate.Amount is decimal amount)
            {
                if (statement.StatementType == "Bank" && string.Equals(candidate.TransactionType, "Expense", StringComparison.OrdinalIgnoreCase) && amount >= 0)
                    messages.Add("Expenses must have a negative amount.");
                if (statement.StatementType == "Bank" && (string.Equals(candidate.TransactionType, "Income", StringComparison.OrdinalIgnoreCase) || string.Equals(candidate.TransactionType, "Refund", StringComparison.OrdinalIgnoreCase)) && amount <= 0)
                    messages.Add("Income and refunds must have a positive amount.");
            }
            if (!Enum.TryParse<TransactionType>(candidate.TransactionType, true, out var transactionType) || !Enum.IsDefined(transactionType))
                messages.Add("Choose Expense, Income, Refund, or Transfer for every selected row.");
            if (messages.Count > 0) rejected.Add(new StatementImportRejectedRow(candidate.SourceRowNumber, messages.Distinct().ToArray()));
        }

        foreach (var candidate in request.Candidates)
        {
            var duplicate = duplicateChecks[candidate.SourceRowNumber];
            var isMatch = duplicate.Classification is nameof(DuplicateClassification.LikelyDuplicate) or nameof(DuplicateClassification.PossibleDuplicate);
            var reviewMatches = isMatch
                ? candidate.ReviewedDuplicateTransactionId == duplicate.ExistingTransactionId &&
                  string.Equals(candidate.ReviewedDuplicateClassification, duplicate.Classification, StringComparison.Ordinal)
                : candidate.ReviewedDuplicateTransactionId is null &&
                  (candidate.ReviewedDuplicateClassification is null or nameof(DuplicateClassification.NoMatch));
            var decision = Enum.TryParse<DuplicateDecision>(candidate.DuplicateDecision, true, out var parsedDecision) && Enum.IsDefined(parsedDecision)
                ? parsedDecision : (DuplicateDecision?)null;
            string? decisionError = null;
            if (!reviewMatches) decisionError = "Duplicate results changed since review. Review the updated match before confirming.";
            else if (isMatch && decision is null) decisionError = "Review this possible duplicate: choose Skip or Import anyway before confirming.";
            else if (isMatch && decision == DuplicateDecision.Unresolved) decisionError = "This possible duplicate is unresolved. Choose Skip or Import anyway before confirming.";
            else if (isMatch && decision == DuplicateDecision.Skip && candidate.Selected) decisionError = "A skipped duplicate cannot be selected for import.";
            else if (isMatch && decision == DuplicateDecision.ImportAnyway && !candidate.Selected) decisionError = "Select this row to import it anyway, or choose Skip.";
            else if (!isMatch && candidate.DuplicateDecision is not null) decisionError = "Duplicate status changed since review. Review the updated result before confirming.";
            if (decisionError is not null)
                rejected.Add(new StatementImportRejectedRow(candidate.SourceRowNumber, [decisionError], duplicate));
        }
        if (rejected.Count > 0)
        {
            var rejectedRows = rejected.GroupBy(x => x.SourceRowNumber).Select(group => new StatementImportRejectedRow(
                group.Key, group.SelectMany(x => x.Messages).Distinct().ToArray(),
                duplicateChecks.GetValueOrDefault(group.Key) ?? group.Select(x => x.DuplicateCheck).FirstOrDefault(x => x is not null))).ToArray();
            var value = new StatementImportConfirmationResult(0, request.Candidates.Count - selected.Length, rejectedRows.Length, rejectedRows);
            return new UseCaseResult<StatementImportConfirmationResult>(value, StatusCodes.Status422UnprocessableEntity, "Some selected rows need correction before they can be imported.");
        }

        var explicitlySkippedEveryCandidate = request.Candidates.Count > 0 && request.Candidates.All(candidate =>
            (duplicateChecks[candidate.SourceRowNumber].Classification is nameof(DuplicateClassification.LikelyDuplicate) or nameof(DuplicateClassification.PossibleDuplicate)) &&
            candidate.DuplicateDecision?.Equals("Skip", StringComparison.OrdinalIgnoreCase) == true && !candidate.Selected);
        if (selected.Length == 0 && !explicitlySkippedEveryCandidate)
            return UseCaseResult<StatementImportConfirmationResult>.Invalid(Error("candidates", "Select at least one valid row or explicitly skip every flagged duplicate."));

        await using var transaction = await repository.BeginTransactionAsync(cancellationToken);
        if (!await repository.TryClaimForProcessingAsync(id, userId, cancellationToken))
            return UseCaseResult<StatementImportConfirmationResult>.Conflict("This statement has already been processed or is being imported.");

        statement.Status = StatementImportStatus.Processing;
        // Repeat duplicate detection after claiming the statement, inside the same database transaction.
        var currentDuplicateResults = await duplicateDetection.CheckManyAsync(statement.FinancialAccountId, duplicateRequests, cancellationToken);
        for (var index = 0; index < request.Candidates.Count; index++)
        {
            var candidate = request.Candidates[index];
            var source = sourceByNumber[candidate.SourceRowNumber];
            var current = currentDuplicateResults[index];
            var wasCheckedMatch = duplicateChecks[candidate.SourceRowNumber].Classification is nameof(DuplicateClassification.LikelyDuplicate) or nameof(DuplicateClassification.PossibleDuplicate);
            var isNowMatch = current.Classification is nameof(DuplicateClassification.LikelyDuplicate) or nameof(DuplicateClassification.PossibleDuplicate);
            var decisionValid = wasCheckedMatch == isNowMatch &&
                duplicateChecks[candidate.SourceRowNumber].ExistingTransactionId == current.ExistingTransactionId &&
                duplicateChecks[candidate.SourceRowNumber].Classification == current.Classification && (!isNowMatch ||
                (candidate.DuplicateDecision?.Equals("Skip", StringComparison.OrdinalIgnoreCase) == true && !candidate.Selected) ||
                (candidate.DuplicateDecision?.Equals("ImportAnyway", StringComparison.OrdinalIgnoreCase) == true && candidate.Selected));
            if (!decisionValid)
            {
                var value = new StatementImportConfirmationResult(0, request.Candidates.Count - selected.Length, 1,
                    [new StatementImportRejectedRow(candidate.SourceRowNumber, ["Duplicate results changed during confirmation. Review the statement and try again."], current)]);
                return new UseCaseResult<StatementImportConfirmationResult>(value, StatusCodes.Status422UnprocessableEntity, "Duplicate results changed during confirmation.");
            }
        }
        foreach (var candidate in selected)
        {
            var duplicateWasOverridden = candidate.DuplicateDecision?.Equals("ImportAnyway", StringComparison.OrdinalIgnoreCase) == true &&
                duplicateChecks[candidate.SourceRowNumber].Classification is nameof(DuplicateClassification.LikelyDuplicate) or nameof(DuplicateClassification.PossibleDuplicate);
            var create = await transactionService.CreateImportedAsync(new TransactionRequest(
                candidate.TransactionDate!.Value, candidate.Description,
                NormalizeImportAmount(statement.StatementType, candidate.Amount!.Value, candidate.TransactionType),
                candidate.TransactionType, statement.FinancialAccountId, candidate.CategoryId,
                sourceByNumber[candidate.SourceRowNumber].ReferenceNumber), duplicateWasOverridden, cancellationToken);
            if (!create.IsSuccess)
            {
                var messages = create.Errors?.SelectMany(x => x.Value).Distinct().ToArray() ?? [create.Title ?? "The transaction could not be saved."];
                var value = new StatementImportConfirmationResult(0, request.Candidates.Count - selected.Length, 1,
                    [new StatementImportRejectedRow(candidate.SourceRowNumber, messages)]);
                return new UseCaseResult<StatementImportConfirmationResult>(value, StatusCodes.Status422UnprocessableEntity, "The import was rejected. No rows were saved.");
            }
        }

        statement.Status = StatementImportStatus.Processed;
        statement.ImportedTransactionCount = selected.Length;
        statement.ProcessedAtUtc = DateTime.UtcNow;
        await repository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return UseCaseResult<StatementImportConfirmationResult>.Ok(new StatementImportConfirmationResult(
            selected.Length, request.Candidates.Count - selected.Length, 0, []));
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
            statement.FileSizeBytes, statement.ContentType, statement.Status.ToString(), statement.UploadedAtUtc, statement.ProcessedAtUtc, statement.StatementType, statement.ImportedTransactionCount);

    private static decimal NormalizeImportAmount(string statementType, decimal sourceAmount, string? transactionType) =>
        statementType == "CreditCard" ? transactionType?.ToLowerInvariant() switch
        {
            "expense" => -decimal.Abs(sourceAmount),
            "income" or "refund" => decimal.Abs(sourceAmount),
            _ => sourceAmount
        } : sourceAmount;

    private static decimal? NormalizePreviewAmount(string statementType, decimal? sourceAmount) =>
        statementType == "CreditCard" && sourceAmount > 0 ? -sourceAmount : sourceAmount;
}
