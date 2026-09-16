using GeekAPI.Services.Workflow.Domain.Enums;

namespace GeekAPI.Services.Workflow.Providers;

/// <summary>
/// Common contract every LLM backend (OpenAI, Anthropic, Groq) implements so the
/// orchestrator never needs to know which vendor is serving a given request.
/// </summary>
public interface IContentGenerationProvider
{
    LlmProviderType ProviderType { get; }

    Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default);
}
