namespace GeekApplication.Results;

public sealed class Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? Error { get; }
    /// <summary>
    /// A stable, machine-readable reason, the same field the non-generic <see cref="Result"/> carries.
    /// Null on success and on a failure raised without one.
    /// </summary>
    public string? ErrorCode { get; }
    public ResultStatus Status { get; }

    private Result(bool success, T? value, string? error, string? errorCode, ResultStatus status)
    {
        IsSuccess = success;
        Value = value;
        Error = error;
        ErrorCode = errorCode;
        Status = status;
    }

    public static Result<T> Success(T value) => new(true, value, null, null, ResultStatus.Ok);
    public static Result<T> NotFound(string error) => new(false, default, error, null, ResultStatus.NotFound);
    public static Result<T> Failure(string error) => new(false, default, error, null, ResultStatus.Failure);
    public static Result<T> Failure(string error, string errorCode) =>
        new(false, default, error, errorCode, ResultStatus.Failure);
}

public enum ResultStatus
{
    Ok,
    NotFound,
    Failure
}
