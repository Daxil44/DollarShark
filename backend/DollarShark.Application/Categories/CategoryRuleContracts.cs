using DollarShark.Application.Common;
using DollarShark.Domain;

namespace DollarShark.Application.Categories;

public sealed record CategoryRuleRequest(string? Pattern, string? MatchType, Guid? CategoryId, int? Priority);
public sealed record CategoryRuleDto(Guid Id, string Pattern, string MatchType, Guid CategoryId, string CategoryName, int Priority, DateTime CreatedAtUtc);
public sealed record CategorySuggestion(Guid CategoryId, string CategoryName, Guid RuleId);
public sealed record ApplyCategoryRulesResult(int UpdatedCount, int UncategorizedRemaining);
public sealed record StarterRulesResult(int AddedCount, int AlreadyPresentCount, int SkippedForLimitCount);

public interface ICategoryRuleRepository
{
    Task<IReadOnlyList<CategoryRuleDto>> ListAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<CategoryRule>> ListForMatchingAsync(CancellationToken cancellationToken);
    Task<CategoryRule?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<CategoryRuleDto?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> CategoryIsVisibleAsync(Guid categoryId, CancellationToken cancellationToken);
    Task<bool> DuplicateExistsAsync(string pattern, CategoryRuleMatchType matchType, Guid? exceptId, CancellationToken cancellationToken);
    Task<int> CountAsync(CancellationToken cancellationToken);
    /// <summary>Tracked uncategorized transactions of the current user, for applying rules.</summary>
    Task<IReadOnlyList<Transaction>> ListUncategorizedTransactionsAsync(CancellationToken cancellationToken);
    Task AddAsync(CategoryRule rule, CancellationToken cancellationToken);
    void Delete(CategoryRule rule);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface ICategoryRuleService
{
    Task<UseCaseResult<IReadOnlyList<CategoryRuleDto>>> ListAsync(CancellationToken cancellationToken);
    Task<UseCaseResult<CategoryRuleDto>> CreateAsync(CategoryRuleRequest request, CancellationToken cancellationToken);
    Task<UseCaseResult<CategoryRuleDto>> UpdateAsync(Guid id, CategoryRuleRequest request, CancellationToken cancellationToken);
    Task<UseCaseResult<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task<UseCaseResult<ApplyCategoryRulesResult>> ApplyToUncategorizedAsync(CancellationToken cancellationToken);
    /// <summary>Copies the common-merchant starter rules into the user's own (editable) rules, skipping ones they already have.</summary>
    Task<UseCaseResult<StarterRulesResult>> AddStarterRulesAsync(CancellationToken cancellationToken);
    /// <summary>Returns one suggestion (or null) per description, in the same order.</summary>
    Task<IReadOnlyList<CategorySuggestion?>> SuggestAsync(IReadOnlyList<string?> descriptions, CancellationToken cancellationToken);
}

public sealed class CategoryRuleService(ICategoryRuleRepository repository) : ICategoryRuleService
{
    public const int MaxRulesPerUser = 200;

    public async Task<UseCaseResult<IReadOnlyList<CategoryRuleDto>>> ListAsync(CancellationToken cancellationToken) =>
        UseCaseResult<IReadOnlyList<CategoryRuleDto>>.Ok(await repository.ListAsync(cancellationToken));

    public async Task<UseCaseResult<CategoryRuleDto>> CreateAsync(CategoryRuleRequest request, CancellationToken cancellationToken)
    {
        if (await repository.CountAsync(cancellationToken) >= MaxRulesPerUser)
            return UseCaseResult<CategoryRuleDto>.Conflict($"You can have at most {MaxRulesPerUser} rules. Delete unused rules first.");
        var validation = await ValidateAsync(request, null, cancellationToken);
        if (validation.Errors is not null) return UseCaseResult<CategoryRuleDto>.Invalid(validation.Errors);

        var rule = new CategoryRule
        {
            Pattern = validation.Pattern!,
            MatchType = validation.MatchType,
            CategoryId = request.CategoryId!.Value,
            Priority = request.Priority ?? CategoryRule.DefaultPriority,
        };
        await repository.AddAsync(rule, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return UseCaseResult<CategoryRuleDto>.Created((await repository.GetAsync(rule.Id, cancellationToken))!);
    }

    public async Task<UseCaseResult<CategoryRuleDto>> UpdateAsync(Guid id, CategoryRuleRequest request, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty) return UseCaseResult<CategoryRuleDto>.Invalid(Error("id", "A valid rule ID is required."));
        var rule = await repository.FindAsync(id, cancellationToken);
        if (rule is null) return UseCaseResult<CategoryRuleDto>.NotFound("Rule was not found.");
        var validation = await ValidateAsync(request, id, cancellationToken);
        if (validation.Errors is not null) return UseCaseResult<CategoryRuleDto>.Invalid(validation.Errors);

        rule.Pattern = validation.Pattern!;
        rule.MatchType = validation.MatchType;
        rule.CategoryId = request.CategoryId!.Value;
        rule.Priority = request.Priority ?? CategoryRule.DefaultPriority;
        await repository.SaveChangesAsync(cancellationToken);
        return UseCaseResult<CategoryRuleDto>.Ok((await repository.GetAsync(id, cancellationToken))!);
    }

    public async Task<UseCaseResult<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty) return UseCaseResult<bool>.Invalid(Error("id", "A valid rule ID is required."));
        var rule = await repository.FindAsync(id, cancellationToken);
        if (rule is null) return UseCaseResult<bool>.NotFound("Rule was not found.");
        repository.Delete(rule);
        await repository.SaveChangesAsync(cancellationToken);
        return UseCaseResult<bool>.NoContent(true);
    }

    public async Task<UseCaseResult<ApplyCategoryRulesResult>> ApplyToUncategorizedAsync(CancellationToken cancellationToken)
    {
        var rules = await repository.ListForMatchingAsync(cancellationToken);
        var transactions = await repository.ListUncategorizedTransactionsAsync(cancellationToken);
        var updated = 0;
        foreach (var transaction in transactions)
        {
            var match = CategoryRule.FindBestMatch(rules, transaction.Description);
            if (match is null) continue;
            transaction.CategoryId = match.CategoryId;
            updated++;
        }
        if (updated > 0) await repository.SaveChangesAsync(cancellationToken);
        return UseCaseResult<ApplyCategoryRulesResult>.Ok(new ApplyCategoryRulesResult(updated, transactions.Count - updated));
    }

    public async Task<UseCaseResult<StarterRulesResult>> AddStarterRulesAsync(CancellationToken cancellationToken)
    {
        var existing = await repository.ListAsync(cancellationToken);
        var existingKeys = existing.Select(rule => Key(rule.Pattern, rule.MatchType)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var room = Math.Max(0, MaxRulesPerUser - existing.Count);
        int added = 0, alreadyPresent = 0, skippedForLimit = 0;
        foreach (var starter in StarterCategoryRules.All)
        {
            if (!existingKeys.Add(Key(starter.Pattern, starter.MatchType.ToString()))) { alreadyPresent++; continue; }
            if (added >= room) { skippedForLimit++; continue; }
            await repository.AddAsync(new CategoryRule { Pattern = starter.Pattern, MatchType = starter.MatchType, CategoryId = starter.CategoryId }, cancellationToken);
            added++;
        }
        if (added > 0) await repository.SaveChangesAsync(cancellationToken);
        return UseCaseResult<StarterRulesResult>.Ok(new StarterRulesResult(added, alreadyPresent, skippedForLimit));

        static string Key(string pattern, string matchType) => $"{matchType}|{pattern.Trim()}";
    }

    public async Task<IReadOnlyList<CategorySuggestion?>> SuggestAsync(IReadOnlyList<string?> descriptions, CancellationToken cancellationToken)
    {
        if (descriptions.Count == 0) return [];
        var rules = await repository.ListForMatchingAsync(cancellationToken);
        if (rules.Count == 0) return descriptions.Select(_ => (CategorySuggestion?)null).ToArray();
        return descriptions.Select(description =>
        {
            var match = CategoryRule.FindBestMatch(rules, description);
            return match is null ? null : new CategorySuggestion(match.CategoryId, match.Category?.Name ?? string.Empty, match.Id);
        }).ToArray();
    }

    private async Task<(string? Pattern, CategoryRuleMatchType MatchType, IReadOnlyDictionary<string, string[]>? Errors)> ValidateAsync(
        CategoryRuleRequest request, Guid? id, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var pattern = request.Pattern is null ? null : string.Join(' ', request.Pattern.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(pattern) || pattern.Length < CategoryRule.MinPatternLength || pattern.Length > CategoryRule.MaxPatternLength)
            errors["pattern"] = [$"Enter text between {CategoryRule.MinPatternLength} and {CategoryRule.MaxPatternLength} characters to match in the description."];

        var matchType = CategoryRuleMatchType.Contains;
        if (!string.IsNullOrWhiteSpace(request.MatchType) &&
            (!Enum.TryParse(request.MatchType, ignoreCase: true, out matchType) || !Enum.IsDefined(matchType)))
            errors["matchType"] = ["Match type must be Contains, StartsWith or Equals."];

        if (request.CategoryId is not Guid categoryId || categoryId == Guid.Empty)
            errors["categoryId"] = ["Choose a category for this rule."];
        else if (!await repository.CategoryIsVisibleAsync(categoryId, cancellationToken))
            errors["categoryId"] = ["Category was not found."];

        if (request.Priority is < 1 or > 1000)
            errors["priority"] = ["Priority must be between 1 and 1000 (lower runs first)."];

        if (!errors.ContainsKey("pattern") && !errors.ContainsKey("matchType") &&
            await repository.DuplicateExistsAsync(pattern!, matchType, id, cancellationToken))
            errors["pattern"] = ["You already have a rule with this text and match type."];

        return (pattern, matchType, errors.Count == 0 ? null : errors);
    }

    private static IReadOnlyDictionary<string, string[]> Error(string key, string message) =>
        new Dictionary<string, string[]> { [key] = [message] };
}
