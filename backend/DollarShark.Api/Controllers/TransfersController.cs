using DollarShark.Application.Transfers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DollarShark.Api.Controllers;

[Route("api/transfers")]
[Authorize]
public sealed class TransfersController(ITransferService service) : ApiControllerBase
{
    [HttpGet("suggestions")]
    public async Task<IActionResult> Suggestions(CancellationToken cancellationToken)
    {
        var result = await service.GetSuggestionsAsync(cancellationToken);
        return Ok(result.Value);
    }

    [HttpPost("link")]
    public async Task<IActionResult> Link(TransferLinkRequest request, CancellationToken cancellationToken)
    {
        var result = await service.LinkAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Failure(result);
    }

    [HttpDelete("{transactionId:guid}")]
    public async Task<IActionResult> Unlink(Guid transactionId, CancellationToken cancellationToken)
    {
        var result = await service.UnlinkAsync(transactionId, cancellationToken);
        return result.IsSuccess ? NoContent() : Failure(result);
    }
}
