using DollarShark.Application.Statements;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DollarShark.Api.Controllers;

[Route("api/statements")]
[Authorize]
public sealed class StatementsController(
    IStatementImportService service,
    ILogger<StatementsController> logger) : ApiControllerBase
{
    [HttpPost]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Upload(
        [FromForm] Guid financialAccountId,
        [FromForm] IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (file is null) return BadRequest(new { title = "Choose a statement file." });

        try
        {
            await using var content = file.OpenReadStream();
            var result = await service.UploadAsync(
                new StatementUploadCommand(financialAccountId, file.FileName, file.Length, content), cancellationToken);
            return result.IsSuccess
                ? CreatedAtAction(nameof(Get), new { id = result.Value!.Id }, result.Value)
                : Failure(result);
        }
        catch (StatementFileTooLargeException)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge, new ProblemDetails
            {
                Status = StatusCodes.Status413PayloadTooLarge,
                Title = "The statement file exceeds the configured size limit."
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Statement upload failed for the current user.");
            return Problem(statusCode: StatusCodes.Status500InternalServerError, title: "The statement could not be uploaded.");
        }
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await service.ListAsync(page, pageSize, cancellationToken);
            return result.IsSuccess ? Ok(result.Value) : Failure(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Statement list failed for the current user.");
            return Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Statements could not be loaded.");
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.GetAsync(id, cancellationToken);
            return result.IsSuccess ? Ok(result.Value) : Failure(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Statement metadata lookup failed for the current user.");
            return Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Statement information could not be loaded.");
        }
    }

    [HttpPost("{id:guid}/parse-csv")]
    public async Task<IActionResult> ParseCsv(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.ParseCsvAsync(id, cancellationToken);
            return result.IsSuccess ? Ok(result.Value) : Failure(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Statement CSV preview failed for the current user.");
            return Problem(statusCode: StatusCodes.Status500InternalServerError, title: "The CSV statement could not be previewed.");
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.DeleteAsync(id, cancellationToken);
            return result.IsSuccess ? NoContent() : Failure(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Statement deletion failed. The record can be retried safely.");
            return Problem(statusCode: StatusCodes.Status500InternalServerError, title: "The statement could not be deleted. Retry the delete request.");
        }
    }

    [HttpPost("{id:guid}/confirm-import")]
    public async Task<IActionResult> ConfirmImport(Guid id, [FromBody] StatementImportConfirmationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.ConfirmImportAsync(id, request, cancellationToken);
            if (result.IsSuccess) return Ok(result.Value);
            if (result.StatusCode == StatusCodes.Status422UnprocessableEntity && result.Value is not null)
                return UnprocessableEntity(result.Value);
            return Failure(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogError(exception, "Statement import confirmation failed for the current user.");
            return Problem(statusCode: StatusCodes.Status500InternalServerError, title: "The statement could not be imported.");
        }
    }
}
