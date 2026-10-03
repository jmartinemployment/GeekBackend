using System.Text.Json;
using System.Text.Json.Serialization;
using GeekAPI.Services.Workflow.Providers;
using GeekApplication.Interfaces.ContentWriterV3;
using Microsoft.Extensions.Options;

namespace GeekAPI.Services.ContentWriterV3;

/// <summary>
/// Generates content using the Claude API via HTTP. Tracks token usage for billing and optimization.
/// </summary>
/// <remarks>
/// <para>
/// <b>Reads <c>LlmProviders:Anthropic</c>, like every other provider in this process.</b> It used to
/// hardcode the model, the URL and the API version, and read <c>ANTHROPIC_API_KEY</c> directly — so
/// <c>AnthropicOptions.Model</c> configured one Anthropic path while this one silently used a model
/// id pinned two minor versions behind it, and setting
/// <c>LlmProviders__Anthropic__ApiKey</c> configured that path and not this one. Two values behind one
/// name, decided by which code ran.
/// </para>
/// <para>
/// Its OpenAI counterpart already did this and says why: <i>"rather than reading its own env var or
/// duplicating key-resolution logic"</i> (<see cref="OpenAiContentGenerator"/>). This is that, for the
/// provider that was missed.
/// </para>
/// <para>
/// <b>The key is resolved per call and fails closed.</b> The constructor used to do
/// <c>?? string.Empty</c> and set <c>x-api-key</c> from it, so an unconfigured process sent a request
/// with an empty key and got an opaque 401 from Anthropic instead of saying what was wrong. Empty
/// counts as absent here — the rule that exists because <c>??</c> on a config value has caused two
/// production auth outages.
/// </para>
/// </remarks>
public class ClaudeContentGenerator : IContentGenerator
{
    private readonly HttpClient _httpClient;
    private readonly AnthropicOptions _options;
    private readonly ILogger<ClaudeContentGenerator> _logger;
    private TokenUsage _lastUsage = new();

    public TokenUsage LastUsage => _lastUsage;

    public ClaudeContentGenerator(
        IHttpClientFactory httpClientFactory,
        IOptions<LlmProvidersOptions> options,
        ILogger<ClaudeContentGenerator> logger)
    {
        _httpClient = httpClientFactory.CreateClient();
        _options = options.Value.Anthropic;
        _logger = logger;
    }

    /// <summary>
    /// The configured key, or null when none is set. Options first, then the environment variable the
    /// older build documented — <see cref="AnthropicProvider"/> resolves it in that same order, and two
    /// precedence rules for one credential is its own outage.
    /// </summary>
    private string? ResolveApiKey() =>
        !string.IsNullOrWhiteSpace(_options.ApiKey)
            ? _options.ApiKey
            : Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") is { } env
              && !string.IsNullOrWhiteSpace(env)
                ? env
                : null;

    public async Task<string> GenerateDraftAsync(
        string strategyBriefAngle,
        string audienceProfile,
        string callToAction,
        List<string> supportingEvidence,
        CancellationToken ct = default)
    {
        var prompt = BuildDraftPrompt(strategyBriefAngle, audienceProfile, callToAction, supportingEvidence);
        return await GenerateWithClaudeAsync(prompt, ct);
    }

    public async Task<string> GenerateStructuredDraftAsync(
        string angle,
        string audienceProfile,
        string buyingStage,
        string callToAction,
        List<string> supportingEvidence,
        CancellationToken ct = default)
    {
        var prompt = BuildStructuredDraftPrompt(angle, audienceProfile, buyingStage, callToAction, supportingEvidence);
        var raw = await GenerateWithClaudeAsync(prompt, ct);
        return ExtractAndValidateContentDocument(raw);
    }

    public async Task<string> ReviseStructuredDraftAsync(
        string currentDocumentJson,
        string feedback,
        CancellationToken ct = default)
    {
        var prompt = $"""
            You are a professional content editor. Revise the given ContentDocument JSON according to the feedback.

            Feedback: {feedback}

            Current document JSON:
            {currentDocumentJson}

            Requirements:
            - Apply the feedback thoroughly.
            - Preserve structure unless the feedback requires adding/removing sections.
            - Keep existing factual claims; do not invent new ones.
            - Respond with ONLY a single valid JSON object — no code fences, no commentary — matching exactly:
            {ContentDocumentJsonContract}
            """;

        var raw = await GenerateWithClaudeAsync(prompt, ct);
        return ExtractAndValidateContentDocument(raw);
    }

    public async Task<string> GenerateRepurposePackAsync(
        string pillarDocumentJson,
        string channelBrief,
        CancellationToken ct = default)
    {
        var prompt =
            $"""
            You are a senior content strategist. Repurpose a long-form pillar ContentDocument into a
            multi-channel short-form and paid-social pack.

            Channel brief:
            {channelBrief}

            Pillar ContentDocument JSON:
            {pillarDocumentJson}

            Stay faithful to the pillar's facts — do not invent claims. Vary angles across variants.
            Match platform norms in the channel brief exactly (counts and lengths).

            Respond with ONLY a single valid JSON object — no code fences, no commentary —
            with a top-level "variants" array. Each variant object must include:
            channel (linkedin|x|instagram|meta_ad|google_ad|email), title, optional headline,
            body, optional cta, optional hashtags string array.
            """;

        var raw = await GenerateWithClaudeAsync(prompt, ct, maxTokens: 8192);
        var pack = GeekAPI.Services.Gcw.GcwRepurposePack.Parse(raw);
        return JsonSerializer.Serialize(new
        {
            variants = pack.Variants.Select(v => new
            {
                channel = v.Channel,
                title = v.Title,
                headline = v.Headline,
                body = v.Body,
                cta = v.Cta,
                hashtags = v.Hashtags,
            }),
        });
    }

    public async Task<string> GenerateVideoSeoPackAsync(
        string pillarDocumentJson,
        string packBrief,
        CancellationToken ct = default)
    {
        var prompt =
            $"""
            You are a YouTube SEO strategist (VidIQ-class). From a long-form pillar article,
            produce a video SEO pack.

            Pack brief:
            {packBrief}

            Pillar ContentDocument JSON:
            {pillarDocumentJson}

            Stay faithful to the pillar — do not invent product claims. Follow the pack brief exactly.

            Respond with ONLY a single valid JSON object — no code fences — with a top-level
            "sections" array. Each section object needs: kind (titles|description|tags|chapters|
            thumbnails|shorts), title, optional body, optional items string array.
            """;

        var raw = await GenerateWithClaudeAsync(prompt, ct, maxTokens: 8192);
        var pack = GeekAPI.Services.Gcw.GcwVideoSeoPack.Parse(raw);
        return JsonSerializer.Serialize(new
        {
            sections = pack.Sections.Select(s => new
            {
                kind = s.Kind,
                title = s.Title,
                body = s.Body,
                items = s.Items,
            }),
        });
    }

    public async Task<string> GenerateSectionAsync(
        string sectionHeading,
        string context,
        string specificFeedback,
        CancellationToken ct = default)
    {
        var prompt = $"""
            You are a professional content writer. Regenerate the following section based on the feedback provided.

            Section: {sectionHeading}

            Context: {context}

            Feedback: {specificFeedback}

            Write ONLY the section content, no metadata or explanations. Use engaging, professional language suited to the audience.
            """;

        return await GenerateWithClaudeAsync(prompt, ct);
    }

    private async Task<string> GenerateWithClaudeAsync(string prompt, CancellationToken ct, int maxTokens = 4096)
    {
        try
        {
            _logger.LogInformation("Calling Claude API for content generation");

            // Fails closed before the request is built: an empty key reaches Anthropic as a 401 that
            // says nothing about configuration being the cause.
            var apiKey = ResolveApiKey();
            if (apiKey is null)
            {
                throw new InvalidOperationException(
                    "Anthropic API key is not configured. Set LlmProviders__Anthropic__ApiKey (or "
                    + "ANTHROPIC_API_KEY).");
            }

            var request = new
            {
                model = _options.Model,
                max_tokens = maxTokens,
                messages = new[]
                {
                    new { role = "user", content = prompt }
                }
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            // Per-request, not constructor defaults: the client comes from IHttpClientFactory and is
            // pooled, so headers set on it once would outlive this call and leak across consumers.
            using var message = new HttpRequestMessage(HttpMethod.Post, _options.BaseUrl)
            {
                Content = content,
            };
            message.Headers.Add("x-api-key", apiKey);
            message.Headers.Add("anthropic-version", _options.AnthropicVersion);

            var response = await _httpClient.SendAsync(message, ct);
            response.EnsureSuccessStatusCode();

            var responseText = await response.Content.ReadAsStringAsync(ct);
            var responseJson = JsonDocument.Parse(responseText);

            var root = responseJson.RootElement;
            var inputTokens = root.GetProperty("usage").GetProperty("input_tokens").GetInt32();
            var outputTokens = root.GetProperty("usage").GetProperty("output_tokens").GetInt32();
            var contentArray = root.GetProperty("content");
            var generatedText = contentArray[0].GetProperty("text").GetString() ?? string.Empty;

            _lastUsage = new TokenUsage
            {
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                EstimatedCost = CalculateTokenCost(inputTokens, outputTokens)
            };

            _logger.LogInformation(
                "Claude API call succeeded. Input: {InputTokens}, Output: {OutputTokens}, Cost: ${Cost}",
                _lastUsage.InputTokens, _lastUsage.OutputTokens, _lastUsage.EstimatedCost);

            return generatedText;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Claude API call failed");
            throw;
        }
    }

    /// <summary>JSON contract matching content-writer-v3/lib/types.ts exactly — ContentDocument's
    /// lede is a plain string (unlike content-writer-v2's lede-as-a-full-Section), and Paragraph is
    /// a discriminated union on "$type" ("text" | "list"). Do not drift from this shape; the
    /// frontend deserializes directly against these TS types.</summary>
    private const string ContentDocumentJsonContract = """
        {
          "lede": string (2-3 sentence opening hook/summary, plain prose, no heading),
          "sections": [
            {
              "heading": string,
              "paragraphs": [
                { "$type": "text", "runs": [ { "text": string, "bold": boolean (optional), "italic": boolean (optional), "linkUrl": string (optional) } ] }
                | { "$type": "list", "ordered": boolean, "items": [ [ { "text": string, "bold": boolean (optional), "italic": boolean (optional) } ], ... ] }
              ],
              "children": [ Section, ... ] (optional, same shape, nested subsections)
            }
          ]
        }
        """;

    private string BuildStructuredDraftPrompt(
        string angle,
        string audience,
        string buyingStage,
        string cta,
        List<string> evidence)
    {
        var evidenceText = evidence.Count > 0 ? string.Join("\n- ", evidence) : "(none provided)";

        return $"""
            You are a professional content strategist and writer. Create a compelling article based on the following brief.

            Angle/Thesis: {angle}
            Target Audience: {audience}
            Buyer's Stage: {buyingStage} — shape the content for this stage specifically. An awareness-stage
            reader needs problem education before any product framing; a decision-stage reader needs concrete
            differentiation and a low-friction next step. Do not write generic mid-funnel content regardless
            of stage.
            Call-to-Action: {cta}

            Supporting Evidence/Points (cite/paraphrase these; do not invent facts beyond them):
            - {evidenceText}

            Requirements:
            - Write in a professional but engaging tone.
            - 3-5 top-level sections, each substantive (not filler).
            - Include the call-to-action naturally in the final section.
            - Aim for 1500-2000 words across all sections combined.

            Respond with ONLY a single valid JSON object — no code fences, no commentary, matching exactly:
            {ContentDocumentJsonContract}
            """;
    }

    /// <summary>Extracts the first balanced JSON object from a possibly-noisy LLM response (strips
    /// code fences if present) and validates it has the shape ContentDocumentJsonContract
    /// describes before trusting it — Anthropic's plain-prompt JSON output isn't schema-enforced the
    /// way OpenAI's json_schema mode is, so a malformed/truncated response must fail loudly here
    /// rather than get stored as a broken ContentAssetVersion.</summary>
    private string ExtractAndValidateContentDocument(string rawContent)
    {
        var cleaned = rawContent.Trim();
        if (cleaned.StartsWith("```"))
        {
            var firstNewline = cleaned.IndexOf('\n');
            cleaned = firstNewline >= 0 ? cleaned[(firstNewline + 1)..] : cleaned;
            var lastFence = cleaned.LastIndexOf("```", StringComparison.Ordinal);
            if (lastFence >= 0)
            {
                cleaned = cleaned[..lastFence];
            }
            cleaned = cleaned.Trim();
        }

        var start = cleaned.IndexOf('{');
        var end = cleaned.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            throw new InvalidOperationException(
                $"Claude did not return a JSON object for structured draft generation. First 200 chars: {rawContent[..Math.Min(200, rawContent.Length)]}");
        }
        var candidate = cleaned[start..(end + 1)];

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(candidate);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Claude's structured draft response was not valid JSON: {ex.Message}. First 200 chars: {rawContent[..Math.Min(200, rawContent.Length)]}", ex);
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("lede", out var lede) || lede.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("sections", out var sections) || sections.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException(
                    "Claude's structured draft response is missing required top-level \"lede\" (string) or \"sections\" (array) fields.");
            }

            foreach (var section in sections.EnumerateArray())
            {
                ValidateSection(section);
            }

            return candidate;
        }
    }

    private static void ValidateSection(JsonElement section)
    {
        if (section.ValueKind != JsonValueKind.Object
            || !section.TryGetProperty("heading", out var heading) || heading.ValueKind != JsonValueKind.String
            || !section.TryGetProperty("paragraphs", out var paragraphs) || paragraphs.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                "Claude's structured draft response has a section missing required \"heading\" (string) or \"paragraphs\" (array) fields.");
        }

        foreach (var paragraph in paragraphs.EnumerateArray())
        {
            if (paragraph.ValueKind != JsonValueKind.Object
                || !paragraph.TryGetProperty("$type", out var type)
                || type.ValueKind != JsonValueKind.String
                || (type.GetString() != "text" && type.GetString() != "list"))
            {
                throw new InvalidOperationException(
                    "Claude's structured draft response has a paragraph with an invalid or missing \"$type\" (must be \"text\" or \"list\").");
            }
        }

        if (section.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
            {
                ValidateSection(child);
            }
        }
    }

    private string BuildDraftPrompt(
        string angle,
        string audience,
        string cta,
        List<string> evidence)
    {
        var evidenceText = string.Join("\n- ", evidence);

        return $"""
            You are a professional content strategist and writer. Create a compelling blog post or article based on the following brief:

            Angle/Thesis: {angle}
            Target Audience: {audience}
            Call-to-Action: {cta}

            Supporting Evidence/Points:
            - {evidenceText}

            Requirements:
            - Write in a professional but engaging tone
            - Structure with a clear introduction, body sections, and conclusion
            - Include the CTA naturally at the end
            - Use the evidence to support claims
            - Aim for 1500-2000 words
            - Use semantic HTML for structure: <h2>/<h3> headings, <p>, <ul>/<li>

            Write ONLY the article content. Do not include metadata, frontmatter, or explanations.
            """;
    }

    private decimal CalculateTokenCost(int inputTokens, int outputTokens)
    {
        // Claude 3.5 Sonnet pricing (as of 2024):
        // Input: $3 per 1M tokens
        // Output: $15 per 1M tokens
        const decimal inputCostPerToken = 3m / 1_000_000m;
        const decimal outputCostPerToken = 15m / 1_000_000m;

        return (inputTokens * inputCostPerToken) + (outputTokens * outputCostPerToken);
    }
}
