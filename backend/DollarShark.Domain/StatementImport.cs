namespace DollarShark.Domain;

public sealed class StatementImport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = string.Empty;
    public Guid FinancialAccountId { get; set; }
    public string StatementType { get; set; } = "Bank";
    public int ImportedTransactionCount { get; set; }
    public FinancialAccount FinancialAccount { get; set; } = null!;
    public string OriginalFileName { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public string FileHash { get; set; } = string.Empty;
    public StatementImportStatus Status { get; set; } = StatementImportStatus.Uploaded;
    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAtUtc { get; set; }
}
