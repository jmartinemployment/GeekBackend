namespace GeekApplication.Results;

/// <summary>Non-generic result for void operations (SEO services).</summary>
public sealed class Result
{
    public bool IsSuccess { get; }
    public string? Error { get; }
    public string? ErrorCode { get; }

    private Result(bool isSuccess, string? error, string? errorCode)
    {
        IsSuccess = isSuccess;
        Error = error;
        ErrorCode = errorCode;
    }

    public static Result Success() => new(true, null, null);

    public static Result Failure(string error, string? errorCode = null) =>
        new(false, error, errorCode);
}
