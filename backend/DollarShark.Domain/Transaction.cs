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
    public string? SubDescription { get; set; }
    public string? ExternalReference { get; set; }
    public bool DuplicateWarningOverridden { get; set; }
    // Expenses are negative; income and refunds positive. Transfers may use either sign.
    public decimal Amount { get; set; }
    public TransactionType TransactionType { get; set; } = TransactionType.Expense;
    public string Currency { get; set; } = "CAD";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Returns this transaction's effect on its account's balance convention without changing Amount.
    /// Asset balances are owned value; liability balances are positive amounts owed.
    /// Expense/Income/Refund signs follow reporting convention; transfers use their signed account-side amount.
    /// </summary>
    public decimal GetAccountBalanceEffect(AccountClassification classification) => classification switch
    {
        AccountClassification.Asset => TransactionType switch
        {
            TransactionType.Expense => -decimal.Abs(Amount),
            TransactionType.Income or TransactionType.Refund => decimal.Abs(Amount),
            TransactionType.Transfer => Amount,
            _ => throw new ArgumentOutOfRangeException(nameof(TransactionType))
        },
        AccountClassification.Liability => TransactionType switch
        {
            TransactionType.Expense => decimal.Abs(Amount),
            TransactionType.Income or TransactionType.Refund => -decimal.Abs(Amount),
            TransactionType.Transfer => Amount,
            _ => throw new ArgumentOutOfRangeException(nameof(TransactionType))
        },
        _ => throw new ArgumentOutOfRangeException(nameof(classification))
    };
}
