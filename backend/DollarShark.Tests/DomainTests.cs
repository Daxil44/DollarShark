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
    public void CategoryRulesMatchCaseInsensitivelyAndPickTheMostSpecificWinner()
    {
        var groceries = Guid.NewGuid();
        var coffee = Guid.NewGuid();
        var fuel = Guid.NewGuid();
        var contains = new CategoryRule { Pattern = "freshmart", MatchType = CategoryRuleMatchType.Contains, CategoryId = groceries };
        var longer = new CategoryRule { Pattern = "FRESHMART CAFE", MatchType = CategoryRuleMatchType.Contains, CategoryId = coffee };
        var startsWith = new CategoryRule { Pattern = "SHELL", MatchType = CategoryRuleMatchType.StartsWith, CategoryId = fuel };
        var exact = new CategoryRule { Pattern = "Rent", MatchType = CategoryRuleMatchType.Equals, CategoryId = groceries };
        var rules = new[] { contains, longer, startsWith, exact };

        Assert.True(contains.Matches("POS FreshMart  #123 TORONTO"));
        Assert.True(startsWith.Matches("shell oil 5521"));
        Assert.False(startsWith.Matches("SEASHELL GIFTS"));
        Assert.True(exact.Matches("  rent "));
        Assert.False(exact.Matches("Rent deposit"));

        // Same priority: the longer (more specific) pattern wins.
        Assert.Same(longer, CategoryRule.FindBestMatch(rules, "FRESHMART CAFE QUEEN ST"));
        Assert.Same(contains, CategoryRule.FindBestMatch(rules, "FRESHMART #9"));
        // A lower priority number beats specificity.
        contains.Priority = 10;
        Assert.Same(contains, CategoryRule.FindBestMatch(rules, "FRESHMART CAFE QUEEN ST"));
        Assert.Null(CategoryRule.FindBestMatch(rules, "Payroll deposit"));
        Assert.Null(CategoryRule.FindBestMatch(rules, null));
    }

    [Fact]
    public void TransactionUsesDecimalAndDateOnlyForStatementValues()
    {
        var transaction = new Transaction { Amount = -12.34m, TransactionDate = new DateOnly(2026, 1, 2) };
        Assert.Equal(-12.34m, transaction.Amount);
        Assert.Equal(new DateOnly(2026, 1, 2), transaction.TransactionDate);
    }
}
