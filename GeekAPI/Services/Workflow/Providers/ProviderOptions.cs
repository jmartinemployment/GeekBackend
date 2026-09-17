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
    public string Model { get; set; } = "gpt-4o";
    public int TimeoutSeconds { get; set; } = 120;
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
