namespace GeekAPI.Services.Workflow.Providers;

public class LlmProvidersOptions
{
    public const string SectionName = "LlmProviders";

    public OpenAiOptions OpenAi { get; set; } = new();
    public AnthropicOptions Anthropic { get; set; } = new();
    public GroqOptions Groq { get; set; } = new();

    /// <summary>Which provider services requests when a caller doesn't specify one explicitly.</summary>
    public string DefaultProvider { get; set; } = "OpenAi";

    /// <summary>
    /// Cost kill switch for every LLM call on the v1/Workflow path. Set false while testing to stop
    /// spending before the first paid request.
    ///
    /// Defaults to <c>true</c> so production is unaffected by the setting's absence — a kill switch
    /// that defaults to "off" silently stops a working system the moment config is missing.
    ///
    /// Disabled means REFUSE, never substitute. Returning canned text here would be indistinguishable
    /// from a real draft downstream, and content nobody generated reaching a page as though it were
    /// written is the exact failure this project exists to avoid.
    /// </summary>
    public bool Enabled { get; set; } = true;
}

public class OpenAiOptions
{
    public string BaseUrl { get; set; } = "https://api.openai.com/v1/chat/completions";
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The writing model -- prose a reader will read. Also the fallback for every other task class,
    /// so leaving the two below unset keeps the previous single-model behaviour exactly.
    /// </summary>
    public string Model { get; set; } = "gpt-4o";

    /// <summary>
    /// Structured extraction over crawled pages: <c>LlmProviders__OpenAi__ExtractionModel</c>.
    /// The bulk of the call volume and no prose judgement in it, so this is where a cheaper model
    /// actually saves money. Empty falls back to <see cref="Model"/>.
    /// </summary>
    public string ExtractionModel { get; set; } = string.Empty;

    /// <summary>
    /// Short structured work around the edges -- image prompts, FAQ formatting:
    /// <c>LlmProviders__OpenAi__UtilityModel</c>. Empty falls back to <see cref="Model"/>.
    /// </summary>
    public string UtilityModel { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// The model for a task class, falling back to <see cref="Model"/> whenever the specific one is
    /// unset -- so an absent setting keeps working rather than sending an empty model name.
    /// </summary>
    public string ResolveModel(LlmTaskClass taskClass) => taskClass switch
    {
        LlmTaskClass.Extraction when !string.IsNullOrWhiteSpace(ExtractionModel) => ExtractionModel.Trim(),
        LlmTaskClass.Utility when !string.IsNullOrWhiteSpace(UtilityModel) => UtilityModel.Trim(),
        _ => Model,
    };
}

public class AnthropicOptions
{
    public string BaseUrl { get; set; } = "https://api.anthropic.com/v1/messages";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "claude-sonnet-5";
    public string AnthropicVersion { get; set; } = "2023-06-01";
    public int TimeoutSeconds { get; set; } = 120;
}

public class GroqOptions
{
    public string BaseUrl { get; set; } = "https://api.groq.com/openai/v1/chat/completions";
    public string ApiKey { get; set; } = string.Empty;
    // Groq retired llama-3.3-70b-versatile (2026-08-16); gpt-oss-120b is their recommended replacement.
    public string Model { get; set; } = "openai/gpt-oss-120b";
    public int TimeoutSeconds { get; set; } = 120;
}
