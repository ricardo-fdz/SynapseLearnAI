namespace LearningAgents.Domain.LLM;

public sealed record LLMMessage(
    string Role,
    string? Content,
    LLMFunctionCall? FunctionCall = null,
    LLMFunctionResponse? FunctionResponse = null,
    IReadOnlyList<LLMFunctionCall>? FunctionCalls = null,
    IReadOnlyList<LLMFunctionResponse>? FunctionResponses = null)
{
    public static LLMMessage ForFunctionCall(LLMFunctionCall functionCall) =>
        new("assistant", null, functionCall);

    public static LLMMessage ForFunctionCalls(IReadOnlyList<LLMFunctionCall> functionCalls) =>
        new("assistant", null, FunctionCalls: functionCalls);

    public static LLMMessage ForFunctionResponse(LLMFunctionResponse functionResponse) =>
        new("tool", null, null, functionResponse);

    public static LLMMessage ForFunctionResponses(IReadOnlyList<LLMFunctionResponse> functionResponses) =>
        new("tool", null, FunctionResponses: functionResponses);
}
