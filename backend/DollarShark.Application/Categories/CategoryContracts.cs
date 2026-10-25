using DollarShark.Application.Common;
using DollarShark.Domain;

namespace DollarShark.Application.Categories;

public sealed record CategoryRequest(string? Name, Guid? ParentCategoryId);
public sealed record CategoryDto(Guid Id, string Name, Guid? ParentCategoryId, string? ParentCategoryName, int TransactionCount, int ChildCount, bool IsSystem);

public interface ICategoryRepository
{
    Task<IReadOnlyList<CategoryDto>> ListAsync(CancellationToken cancellationToken);
    Task<CategoryDto?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Category?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> NameExistsAsync(string name, Guid? parentId, Guid? exceptId, CancellationToken cancellationToken);
    Task<bool> WouldCreateCycleAsync(Guid categoryId, Guid? proposedParentId, CancellationToken cancellationToken);
    Task<bool> HasChildrenAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> HasTransactionsAsync(Guid id, CancellationToken cancellationToken);
    Task AddAsync(Category category, CancellationToken cancellationToken);
    void Delete(Category category);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface ICategoryService
{
    Task<UseCaseResult<IReadOnlyList<CategoryDto>>> ListAsync(CancellationToken cancellationToken);
    Task<UseCaseResult<CategoryDto>> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<UseCaseResult<CategoryDto>> CreateAsync(CategoryRequest request, CancellationToken cancellationToken);
    Task<UseCaseResult<CategoryDto>> UpdateAsync(Guid id, CategoryRequest request, CancellationToken cancellationToken);
    Task<UseCaseResult<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken);
}

public sealed class CategoryService(ICategoryRepository repository) : ICategoryService
{
    public async Task<UseCaseResult<IReadOnlyList<CategoryDto>>> ListAsync(CancellationToken cancellationToken) =>
        UseCaseResult<IReadOnlyList<CategoryDto>>.Ok(await repository.ListAsync(cancellationToken));

    public async Task<UseCaseResult<CategoryDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty) return UseCaseResult<CategoryDto>.Invalid(Error("id", "A valid category ID is required."));
        var category = await repository.GetAsync(id, cancellationToken);
        return category is null ? UseCaseResult<CategoryDto>.NotFound("Category was not found.") : UseCaseResult<CategoryDto>.Ok(category);
    }

    public async Task<UseCaseResult<CategoryDto>> CreateAsync(CategoryRequest request, CancellationToken cancellationToken)
    {
        var validation = await ValidateAsync(request, null, cancellationToken);
        if (validation.Errors is not null) return UseCaseResult<CategoryDto>.Invalid(validation.Errors);
        var category = new Category { Name = validation.Name!, ParentCategoryId = request.ParentCategoryId };
        await repository.AddAsync(category, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return UseCaseResult<CategoryDto>.Created(await repository.GetAsync(category.Id, cancellationToken) ?? ToDto(category));
    }

    public async Task<UseCaseResult<CategoryDto>> UpdateAsync(Guid id, CategoryRequest request, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty) return UseCaseResult<CategoryDto>.Invalid(Error("id", "A valid category ID is required."));
        var category = await repository.FindAsync(id, cancellationToken);
        if (category is null) return UseCaseResult<CategoryDto>.NotFound("Category was not found.");
        var validation = await ValidateAsync(request, id, cancellationToken);
        if (validation.Errors is not null) return UseCaseResult<CategoryDto>.Invalid(validation.Errors);
        category.Name = validation.Name!;
        category.ParentCategoryId = request.ParentCategoryId;
        await repository.SaveChangesAsync(cancellationToken);
        return UseCaseResult<CategoryDto>.Ok(await repository.GetAsync(id, cancellationToken) ?? ToDto(category));
    }

    public async Task<UseCaseResult<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty) return UseCaseResult<bool>.Invalid(Error("id", "A valid category ID is required."));
        var category = await repository.FindAsync(id, cancellationToken);
        if (category is null) return UseCaseResult<bool>.NotFound("Category was not found.");
        if (await repository.HasChildrenAsync(id, cancellationToken) || await repository.HasTransactionsAsync(id, cancellationToken))
            return UseCaseResult<bool>.Conflict("A category with child categories or transactions cannot be deleted.");
        repository.Delete(category);
        await repository.SaveChangesAsync(cancellationToken);
        return UseCaseResult<bool>.NoContent(true);
    }

    private async Task<(string? Name, IReadOnlyDictionary<string, string[]>? Errors)> ValidateAsync(
        CategoryRequest request, Guid? id, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80)
            errors["name"] = ["Name is required and must be at most 80 characters."];
        if (request.ParentCategoryId == Guid.Empty)
            errors["parentCategoryId"] = ["Parent category ID must be a valid ID."];
        if (request.ParentCategoryId is Guid parentId)
        {
            if (!await repository.ExistsAsync(parentId, cancellationToken))
                errors["parentCategoryId"] = ["Parent category was not found."];
            else if (id == parentId || (id is Guid categoryId && await repository.WouldCreateCycleAsync(categoryId, parentId, cancellationToken)))
                errors["parentCategoryId"] = ["The parent assignment would create a category cycle."];
        }
        if (!string.IsNullOrWhiteSpace(name) && name.Length <= 80 &&
            await repository.NameExistsAsync(name, request.ParentCategoryId, id, cancellationToken))
            errors["name"] = ["A category with this name already exists at the selected level."];
        return (name, errors.Count == 0 ? null : errors);
    }

    private static CategoryDto ToDto(Category category) =>
        new(category.Id, category.Name, category.ParentCategoryId, category.ParentCategory?.Name, category.Transactions.Count, category.Children.Count, category.IsSystem);

    private static IReadOnlyDictionary<string, string[]> Error(string key, string message) =>
        new Dictionary<string, string[]> { [key] = [message] };
}
