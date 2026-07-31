using System.Text.Json;
using System.Text.Json.Serialization;
using GeekApplication.Interfaces.ContentWriterV3;

namespace GeekAPI.Services.ContentWriterV3;

/// <summary>
/// Generates content using the Claude API via HTTP.
/// Tracks token usage for billing and optimization.
/// </summary>
public class ClaudeContentGenerator : IContentGenerator
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly ILogger<ClaudeContentGenerator> _logger;
    private TokenUsage _lastUsage = new();

    public TokenUsage LastUsage => _lastUsage;
    private const string ClaudeApiUrl = "https://api.anthropic.com/v1/messages";
    private const string ClaudeModel = "claude-sonnet-4-5-20250929";

    public ClaudeContentGenerator(ILogger<ClaudeContentGenerator> logger)
    {
        _logger = logger;
        _apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? string.Empty;

        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("x-api-key", _apiKey);
        _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
    }

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

    private async Task<string> GenerateWithClaudeAsync(string prompt, CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Calling Claude API for content generation");

            var request = new
            {
                model = ClaudeModel,
                max_tokens = 4096,
                messages = new[]
                {
                    new { role = "user", content = prompt }
                }
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(ClaudeApiUrl, content, ct);
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

            Respond with ONLY a single valid JSON object — no markdown fences, no commentary, matching exactly:
            {ContentDocumentJsonContract}
            """;
    }

    /// <summary>Extracts the first balanced JSON object from a possibly-noisy LLM response (strips
    /// markdown code fences if present) and validates it has the shape ContentDocumentJsonContract
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
            - Use markdown formatting (## for sections, etc.)

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
