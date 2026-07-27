namespace LearningAgents.Domain.LLM;

public sealed record LLMResponse(
    bool IsSuccess,
    string? Content,
    string? ErrorMessage,
    int? StatusCode = null,
    IReadOnlyList<LLMFunctionCall>? FunctionCalls = null)
{
    public static LLMResponse Success(string content) => new(true, content, null);

    public static LLMResponse ToolCalls(IReadOnlyList<LLMFunctionCall> functionCalls) =>
        new(true, null, null, FunctionCalls: functionCalls);

    public static LLMResponse Failure(string errorMessage, int? statusCode = null) =>
        new(false, null, errorMessage, statusCode);
}
