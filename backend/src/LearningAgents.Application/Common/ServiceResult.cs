namespace LearningAgents.Application.Common;

public sealed record ServiceResult<T>
{
    public T? Data { get; }
    public string? ErrorMessage { get; }
    public bool IsSuccess => ErrorMessage is null;

    private ServiceResult(T data) => Data = data;
    private ServiceResult(string errorMessage) => ErrorMessage = errorMessage;

    public static ServiceResult<T> Success(T data) => new(data);
    public static ServiceResult<T> Failure(string errorMessage) => new(errorMessage);
}
