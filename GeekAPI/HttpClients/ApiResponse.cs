namespace GeekAPI.HttpClients;

internal sealed class ApiResponse<T>
{
    public bool Success { get; init; }
    public T Data { get; init; } = default!;
    public ApiError? Error { get; init; }
}

internal sealed record ApiError(string Code, string Message);
