namespace GeekAPI.Services.Workflow.Providers;

public enum ChatRole
{
    System,
    User,
    Assistant
}

public record ChatMessage(ChatRole Role, string Content)
{
    public string RoleString => Role switch
    {
        ChatRole.System => "system",
        ChatRole.User => "user",
        ChatRole.Assistant => "assistant",
        _ => throw new ArgumentOutOfRangeException(nameof(Role))
    };
}

/// <summary>
/// <paramref name="JsonSchemaName"/>/<paramref name="JsonSchema"/> request provider-native
/// structured-output enforcement (Anthropic forced tool-use, OpenAI response_format json_schema).
/// Providers without reliable native enforcement (Groq) ignore these two fields and fall
/// back to prompt-only generation — correctness for those is guaranteed by the caller's own
/// two-tier validation (schema deserialization + content-hygiene scan) after the fact, not by the
/// provider. See the design plan's "provider reality check."
/// </summary>
/// <summary>
/// What kind of work a call is, so the model can be chosen per job rather than per provider.
///
/// <para>
/// One setting drove every OpenAI call -- 37 structured extraction calls, the lede, the body,
/// metadata, image prompts, the FAQ. That is why a single bad model value took the whole pipeline
/// out at once on 2026-09-23 and read as a data shortage for two hours, and why trying a cheaper
/// model meant trying it on the prose as well.
/// </para>
/// </summary>
public enum LlmTaskClass
{
    /// <summary>Prose a reader will read. The expensive one; do not economise here.</summary>
    Writing = 0,

    /// <summary>Structured JSON out of crawled pages. The bulk of the call volume, no prose judgement.</summary>
    Extraction = 1,

    /// <summary>Short structured work around the edges -- image prompts, FAQ formatting.</summary>
    Utility = 2,
}

public record ChatCompletionRequest(
    List<ChatMessage> Messages,
    double Temperature = 0.7,
    int MaxOutputTokens = 4096,
    string? Model = null,
    string? JsonSchemaName = null,
    string? JsonSchema = null,
    /// <summary>Chooses the model when <paramref name="Model"/> is unset. Defaults to Writing, so a
    /// call that says nothing gets the good model rather than silently getting the cheap one.</summary>
    LlmTaskClass TaskClass = LlmTaskClass.Writing);

public record ChatCompletionResult(
    string Content,
    string ModelUsed,
    int? PromptTokens,
    int? CompletionTokens,
    int RetryCount = 0,
    string? RetryReason = null,
    int? CachedTokens = null);

public class ContentGenerationException : Exception
{
    public ContentGenerationException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

