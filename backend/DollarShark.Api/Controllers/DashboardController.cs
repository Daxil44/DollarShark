using DollarShark.Application.Dashboard;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DollarShark.Api.Controllers;

[Route("api/dashboard")]
[Authorize]
public sealed class DashboardController(IDashboardService service) : ApiControllerBase
{
    [HttpGet("overview")]
    public async Task<IActionResult> Overview([FromQuery] DashboardQuery query, CancellationToken cancellationToken)
    {
        var result = await service.GetOverviewAsync(query, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Failure(result);
    }
}
