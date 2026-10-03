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
    /// so leaving the two below unset keeps single-model behaviour.
    /// </summary>
    /// <remarks>
    /// <b>No default on purpose</b> -- set <c>LlmProviders__OpenAi__Model</c>. A hardcoded model name
    /// here is a model nobody chose: delete the variable and generation keeps working, silently billed
    /// against whatever the last developer typed. <c>ContentProviderFactory</c> already refuses to
    /// substitute a <i>provider</i> for exactly this reason -- "silently substituting one is how every
    /// create ends up billed against a model nobody chose" -- and a model is the same decision one level
    /// down. Empty is refused at call time by the provider, naming the variable to set.
    /// </remarks>
    public string Model { get; set; } = string.Empty;

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
    /// <summary>
    /// The model for every Anthropic call. There is no task-class split here and none is needed: the
    /// extraction path resolves its provider through <c>GetDefault()</c>, so Anthropic only ever serves
    /// writing (plus the three small utility prompts inside a writing flow).
    /// </summary>
    /// <remarks>
    /// <b>No default on purpose</b> -- set <c>LlmProviders__Anthropic__Model</c>. Same reason as
    /// <see cref="OpenAiOptions.Model"/>: a model id in source outlives the release it was current for,
    /// and the failure mode is silent spend on a model nobody picked rather than an error.
    /// </remarks>
    public string Model { get; set; } = string.Empty;
    public string AnthropicVersion { get; set; } = "2023-06-01";
    public int TimeoutSeconds { get; set; } = 120;
}

public class GroqOptions
{
    public string BaseUrl { get; set; } = "https://api.groq.com/openai/v1/chat/completions";
    public string ApiKey { get; set; } = string.Empty;
    /// <summary>
    /// No default, as with the other two providers -- set <c>LlmProviders__Groq__Model</c>. This one
    /// already demonstrated the cost of a hardcoded id: it carried a model Groq retired on 2026-08-16,
    /// so the source said one thing and the API accepted another until somebody noticed.
    /// </summary>
    public string Model { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 120;
}
