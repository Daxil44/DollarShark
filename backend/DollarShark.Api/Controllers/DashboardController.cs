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

    [HttpGet("accounts/{accountId:guid}/balance-history")]
    public async Task<IActionResult> AccountBalanceHistory(
        Guid accountId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] string interval = "Daily", CancellationToken cancellationToken = default)
    {
        var result = await service.GetAccountBalanceHistoryAsync(
            new AccountBalanceHistoryQuery { AccountId = accountId, From = from, To = to, Interval = interval },
            cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Failure(result);
    }
}
