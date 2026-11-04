namespace LearningAgents.Domain.LLM;

public sealed record LLMTokenUsage(
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens);

public sealed record LLMResponse(
    bool IsSuccess,
    string? Content,
    string? ErrorMessage,
    int? StatusCode = null,
    IReadOnlyList<LLMFunctionCall>? FunctionCalls = null,
    LLMTokenUsage? Usage = null)
{
    public static LLMResponse Success(string content, LLMTokenUsage? usage = null) =>
        new(true, content, null, Usage: usage);

    public static LLMResponse ToolCalls(IReadOnlyList<LLMFunctionCall> functionCalls, LLMTokenUsage? usage = null) =>
        new(true, null, null, FunctionCalls: functionCalls, Usage: usage);

    public static LLMResponse Failure(string errorMessage, int? statusCode = null) =>
        new(false, null, errorMessage, statusCode);
}
