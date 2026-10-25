namespace DollarShark.Application.Common;

public sealed record UseCaseResult<T>(
    T? Value,
    int StatusCode,
    string? Title = null,
    IReadOnlyDictionary<string, string[]>? Errors = null)
{
    public bool IsSuccess => StatusCode < 400;
    public static UseCaseResult<T> Ok(T value) => new(value, 200);
    public static UseCaseResult<T> Created(T value) => new(value, 201);
    public static UseCaseResult<T> NoContent(T value) => new(value, 204);
    public static UseCaseResult<T> Invalid(IReadOnlyDictionary<string, string[]> errors) => new(default, 400, "One or more validation errors occurred.", errors);
    public static UseCaseResult<T> NotFound(string title) => new(default, 404, title);
    public static UseCaseResult<T> Conflict(string title) => new(default, 409, title);
}
