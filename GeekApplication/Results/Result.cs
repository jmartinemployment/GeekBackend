namespace GeekApplication.Results;

public sealed class Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? Error { get; }
    public ResultStatus Status { get; }

    private Result(bool success, T? value, string? error, ResultStatus status)
    {
        IsSuccess = success;
        Value = value;
        Error = error;
        Status = status;
    }

    public static Result<T> Success(T value) => new(true, value, null, ResultStatus.Ok);
    public static Result<T> NotFound(string error) => new(false, default, error, ResultStatus.NotFound);
    public static Result<T> Failure(string error) => new(false, default, error, ResultStatus.Failure);
}

public enum ResultStatus
{
    Ok,
    NotFound,
    Failure
}
