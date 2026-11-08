using DollarShark.Application.Categories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DollarShark.Api.Controllers;

[Route("api/category-rules")]
[Authorize]
public sealed class CategoryRulesController(ICategoryRuleService service) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        Ok((await service.ListAsync(cancellationToken)).Value);

    [HttpPost]
    public async Task<IActionResult> Create(CategoryRuleRequest request, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(request, cancellationToken);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : Failure(result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, CategoryRuleRequest request, CancellationToken cancellationToken)
    {
        var result = await service.UpdateAsync(id, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Failure(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await service.DeleteAsync(id, cancellationToken);
        return result.IsSuccess ? NoContent() : Failure(result);
    }

    /// <summary>Adds the common-merchant starter rules as the user's own editable rules. Safe to call again.</summary>
    [HttpPost("starter")]
    public async Task<IActionResult> AddStarter(CancellationToken cancellationToken) =>
        Ok((await service.AddStarterRulesAsync(cancellationToken)).Value);

    /// <summary>Applies the user's rules to their transactions that have no category yet. Categorized transactions are never changed.</summary>
    [HttpPost("apply")]
    public async Task<IActionResult> Apply(CancellationToken cancellationToken) =>
        Ok((await service.ApplyToUncategorizedAsync(cancellationToken)).Value);
}
