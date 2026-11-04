namespace LearningAgents.Domain.LLM;

public sealed record LLMStreamChunk(
    string? TextDelta = null,
    IReadOnlyList<LLMFunctionCall>? FunctionCalls = null,
    LLMTokenUsage? Usage = null,
    bool IsError = false,
    string? ErrorMessage = null,
    int? StatusCode = null)
{
    public static LLMStreamChunk Delta(string textDelta) => new(TextDelta: textDelta);

    public static LLMStreamChunk ToolCalls(IReadOnlyList<LLMFunctionCall> functionCalls, LLMTokenUsage? usage = null) =>
        new(FunctionCalls: functionCalls, Usage: usage);

    public static LLMStreamChunk Completed(LLMTokenUsage? usage) => new(Usage: usage);

    public static LLMStreamChunk Failure(string errorMessage, int? statusCode = null) =>
        new(IsError: true, ErrorMessage: errorMessage, StatusCode: statusCode);
}
