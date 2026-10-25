using DollarShark.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace DollarShark.Api.Controllers;

[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    protected IActionResult Failure<T>(UseCaseResult<T> result)
    {
        if (result.StatusCode == StatusCodes.Status400BadRequest && result.Errors is not null)
            return BadRequest(new ValidationProblemDetails(result.Errors.ToDictionary(x => x.Key, x => x.Value)) { Status = StatusCodes.Status400BadRequest, Title = result.Title });
        return Problem(statusCode: result.StatusCode, title: result.Title);
    }
}

