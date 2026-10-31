using System;
using System.ComponentModel.DataAnnotations.Schema;
namespace DollarShark.Domain;
public sealed class FinancialAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Institution { get; set; } = string.Empty;
    public string AccountType { get; set; } = string.Empty;
    [NotMapped]
    public AccountClassification Classification => AccountType switch
    {
        "Chequing" or "Savings" or "Investment" => AccountClassification.Asset,
        "Credit Card" => AccountClassification.Liability,
        _ => throw new InvalidOperationException($"Unsupported financial account type '{AccountType}'.")
    };
    public string Currency { get; set; } = "CAD";
    public string? LastFourDigits { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
