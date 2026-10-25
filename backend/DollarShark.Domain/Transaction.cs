namespace DollarShark.Domain;

public sealed class Transaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? UserId { get; set; }
    public Guid FinancialAccountId { get; set; }
    public FinancialAccount FinancialAccount { get; set; } = null!;
    public Guid? CategoryId { get; set; }
    public Category? Category { get; set; }
    public DateOnly TransactionDate { get; set; }
    public string Description { get; set; } = string.Empty;
    // Expenses are negative; income and refunds positive. Transfers may use either sign.
    public decimal Amount { get; set; }
    public TransactionType TransactionType { get; set; } = TransactionType.Expense;
    public string Currency { get; set; } = "CAD";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
