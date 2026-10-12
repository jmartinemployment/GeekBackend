using GeekAPI.Services.Workflow.Domain.Enums;

namespace GeekAPI.Services.Workflow.Providers;

/// <summary>
/// Common contract every LLM backend (OpenAI, Anthropic, Groq) implements so the
/// orchestrator never needs to know which vendor is serving a given request.
/// </summary>
public interface IContentGenerationProvider
{
    LlmProviderType ProviderType { get; }

    /// <summary>
    /// The model <paramref name="request"/> would be sent to, as configured: the request's own when it
    /// names one, otherwise this provider's for the request's kind of work. Empty when none is
    /// configured. Sends nothing.
    /// </summary>
    /// <remarks>
    /// So a request can be counted against what its model accepts before it is paid for
    /// (<see cref="OpenAiModelLimits"/>). The same answer <see cref="CompleteAsync"/> acts on: each
    /// provider resolves its model in one place and both read it.
    /// </remarks>
    string ModelFor(ChatCompletionRequest request);

    Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default);
}
