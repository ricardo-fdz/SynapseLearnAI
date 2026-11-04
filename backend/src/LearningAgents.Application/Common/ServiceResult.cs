namespace LearningAgents.Application.Common;

public sealed record ServiceResult<T>
{
    public T? Data { get; }
    public string? ErrorMessage { get; }
    public int? StatusCode { get; }
    public object? ErrorDetails { get; }
    public bool IsSuccess => ErrorMessage is null;

    private ServiceResult(T data) => Data = data;

    private ServiceResult(string errorMessage, int? statusCode = null, object? errorDetails = null)
    {
        ErrorMessage = errorMessage;
        StatusCode = statusCode;
        ErrorDetails = errorDetails;
    }

    public static ServiceResult<T> Success(T data) => new(data);

    public static ServiceResult<T> Failure(string errorMessage, int? statusCode = null, object? errorDetails = null) =>
        new(errorMessage, statusCode, errorDetails);
}
