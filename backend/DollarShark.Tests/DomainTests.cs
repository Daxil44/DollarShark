using DollarShark.Domain;
namespace DollarShark.Tests;
public sealed class DomainTests
{
    [Fact]
    public void FinancialAccountDefaultsToActiveWithCurrency()
    {
        var account = new FinancialAccount();
        Assert.NotEqual(Guid.Empty, account.Id);
        Assert.True(account.IsActive);
        Assert.Equal("CAD", account.Currency);
        Assert.Empty(account.Transactions);
    }
    [Fact]
    public void TransactionUsesDecimalAndDateOnlyForStatementValues()
    {
        var transaction = new Transaction { Amount = -12.34m, TransactionDate = new DateOnly(2026, 1, 2) };
        Assert.Equal(-12.34m, transaction.Amount);
        Assert.Equal(new DateOnly(2026, 1, 2), transaction.TransactionDate);
    }
}
