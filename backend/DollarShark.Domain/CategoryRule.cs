namespace DollarShark.Domain;

public enum CategoryRuleMatchType
{
    Contains,
    StartsWith,
    Equals
}

/// <summary>
/// A user's "description matches → category" rule used to suggest categories for imported or uncategorized transactions.
/// </summary>
public sealed class CategoryRule
{
    public const int MaxPatternLength = 100;
    public const int MinPatternLength = 2;
    public const int DefaultPriority = 100;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = string.Empty;
    public string Pattern { get; set; } = string.Empty;
    public CategoryRuleMatchType MatchType { get; set; } = CategoryRuleMatchType.Contains;
    public Guid CategoryId { get; set; }
    public Category? Category { get; set; }
    /// <summary>Lower numbers win when several rules match.</summary>
    public int Priority { get; set; } = DefaultPriority;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public bool Matches(string? description)
    {
        if (string.IsNullOrWhiteSpace(description) || string.IsNullOrWhiteSpace(Pattern)) return false;
        var text = Normalize(description);
        var pattern = Normalize(Pattern);
        return MatchType switch
        {
            CategoryRuleMatchType.Contains => text.Contains(pattern, StringComparison.OrdinalIgnoreCase),
            CategoryRuleMatchType.StartsWith => text.StartsWith(pattern, StringComparison.OrdinalIgnoreCase),
            CategoryRuleMatchType.Equals => string.Equals(text, pattern, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    /// <summary>
    /// Picks the winning rule: lowest priority number, then the longest (most specific) pattern, then the oldest rule.
    /// </summary>
    public static CategoryRule? FindBestMatch(IEnumerable<CategoryRule> rules, string? description) =>
        rules.Where(rule => rule.Matches(description))
            .OrderBy(rule => rule.Priority)
            .ThenByDescending(rule => rule.Pattern.Trim().Length)
            .ThenBy(rule => rule.CreatedAtUtc)
            .ThenBy(rule => rule.Id)
            .FirstOrDefault();

    /// <summary>Collapses repeated whitespace so "FRESH  MART" and "FRESH MART" compare equally.</summary>
    private static string Normalize(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
