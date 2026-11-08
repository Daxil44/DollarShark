using DollarShark.Application.Categories;
using DollarShark.Domain;
using DollarShark.Infrastructure.Persistence;

namespace DollarShark.Tests;

public sealed class StarterCategoryRulesTests
{
    private static IReadOnlyList<CategoryRule> AsRules() => StarterCategoryRules.All
        .Select(rule => new CategoryRule { Pattern = rule.Pattern, MatchType = rule.MatchType, CategoryId = rule.CategoryId })
        .ToArray();

    [Fact]
    public void StarterRulesUseSeededDefaultCategoriesAndValidUniquePatterns()
    {
        var seeded = CategorySeedData.Categories.ToDictionary(category => category.Id, category => category.Name);
        Assert.True(StarterCategoryRules.All.Count is > 40 and <= CategoryRuleService.MaxRulesPerUser);
        Assert.All(StarterCategoryRules.All, rule =>
        {
            Assert.True(seeded.ContainsKey(rule.CategoryId), $"{rule.Pattern} points at a category that is not seeded.");
            Assert.InRange(rule.Pattern.Length, CategoryRule.MinPatternLength, CategoryRule.MaxPatternLength);
        });
        var keys = StarterCategoryRules.All.Select(rule => $"{rule.MatchType}|{rule.Pattern.ToUpperInvariant()}").ToArray();
        Assert.Equal(keys.Length, keys.Distinct().Count());
    }

    [Theory]
    [InlineData("WALMART SUPERCENTRE #1234", "Shopping")]
    [InlineData("REAL CANADIAN SUPERSTORE 1577", "Groceries")]
    [InlineData("TIM HORTONS #4410", "Restaurants")]
    [InlineData("UBER EATS TORONTO", "Restaurants")]
    [InlineData("UBER *TRIP HELP.UBER.COM", "Transportation")]
    [InlineData("AMAZON PRIME*2K4 AMZN.CA", "Subscriptions")]
    [InlineData("AMZN MKTP CA*1A2B3C", "Shopping")]
    [InlineData("ESSO CIRCLE K 0042", "Transportation")]
    [InlineData("NETFLIX.COM", "Subscriptions")]
    [InlineData("ROGERS ******1234", "Utilities")]
    [InlineData("PAYMENT RECEIVED - THANK YOU", "Transfers")]
    public void CommonDescriptionsLandInTheExpectedCategory(string description, string expectedCategory)
    {
        var seeded = CategorySeedData.Categories.ToDictionary(category => category.Id, category => category.Name);
        var match = CategoryRule.FindBestMatch(AsRules(), description);
        Assert.NotNull(match);
        Assert.Equal(expectedCategory, seeded[match.CategoryId]);
    }

    [Theory]
    [InlineData("ESPRESSO BAR LTD")]
    [InlineData("SEASHELL GIFTS")]
    [InlineData("E-TRANSFER JOHN SMITH")]
    public void LookalikeDescriptionsDoNotMatch(string description) =>
        Assert.Null(CategoryRule.FindBestMatch(AsRules(), description));
}
