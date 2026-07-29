namespace GeekApplication.Interfaces.ContentWriterV4;

public sealed record GenerationResult(
    string Output,
    string Provider,
    string Model,
    int InputTokens,
    int OutputTokens);

/// <summary>
/// v4-owned LLM abstraction. Shares no code with the v3 IContentGenerator/ClaudeContentGenerator.
/// Providers report raw token counts only — cost pricing is a separate concern (IGenerationCostCalculator).
/// </summary>
public interface ILlmProviderV4
{
    string ProviderName { get; }

    Task<GenerationResult> GenerateAsync(
        string systemPrompt,
        string userPrompt,
        string model,
        CancellationToken ct = default);
}
