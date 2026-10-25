namespace DollarShark.Domain;
public sealed class Transaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FinancialAccountId { get; set; }
    public FinancialAccount FinancialAccount { get; set; } = null!;
    public Guid? CategoryId { get; set; }
    public Category? Category { get; set; }
    public DateOnly TransactionDate { get; set; }
    public string Description { get; set; } = string.Empty;
    // Positive amounts are money received; negative amounts are spending or fees.
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "CAD";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
