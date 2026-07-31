using System.Text;
using System.Text.Json;
using ContentWriter.Application.Providers;
using GeekApplication.Interfaces.ContentWriterV3;
using Microsoft.Extensions.Options;

namespace GeekAPI.Services.ContentWriterV3;

/// <summary>
/// OpenAI-backed alternative to ClaudeContentGenerator. Reuses content-writer-v2's already-bound
/// LlmProvidersOptions (GeekAPI already configures this section and already holds a working
/// LlmProviders__OpenAi__ApiKey — see AGENTS.md "Persistence and target architecture" for why
/// content-writer-v2's config lives in this process) rather than reading its own env var or
/// duplicating key-resolution logic.
/// </summary>
public class OpenAiContentGenerator : IContentGenerator
{
    private const string OpenAiApiUrl = "https://api.openai.com/v1/chat/completions";

    private readonly HttpClient _httpClient;
    private readonly OpenAiOptions _options;
    private readonly ILogger<OpenAiContentGenerator> _logger;
    private TokenUsage _lastUsage = new();

    public TokenUsage LastUsage => _lastUsage;

    public OpenAiContentGenerator(
        IHttpClientFactory httpClientFactory,
        IOptions<LlmProvidersOptions> options,
        ILogger<OpenAiContentGenerator> logger)
    {
        _httpClient = httpClientFactory.CreateClient();
        _options = options.Value.OpenAi;
        _logger = logger;
    }

    public Task<string> GenerateDraftAsync(
        string strategyBriefAngle,
        string audienceProfile,
        string callToAction,
        List<string> supportingEvidence,
        CancellationToken ct = default) =>
        throw new NotSupportedException(
            "OpenAiContentGenerator only implements structured (JSON) generation — call GenerateStructuredDraftAsync. " +
            "Markdown-prose generation was never wired to any caller and isn't being extended to a second provider.");

    public async Task<string> GenerateStructuredDraftAsync(
        string angle,
        string audienceProfile,
        string buyingStage,
        string callToAction,
        List<string> supportingEvidence,
        CancellationToken ct = default)
    {
        var evidenceText = supportingEvidence.Count > 0 ? string.Join("\n- ", supportingEvidence) : "(none provided)";

        var system = $"""
            You are a professional content strategist and writer. Create a compelling article based on the brief given.
            Respond with ONLY a single valid JSON object — no markdown fences, no commentary — matching exactly:
            {ContentDocumentJsonContract}
            """;

        var user = $"""
            Angle/Thesis: {angle}
            Target Audience: {audienceProfile}
            Buyer's Stage: {buyingStage} — shape the content for this stage specifically. An awareness-stage
            reader needs problem education before any product framing; a decision-stage reader needs concrete
            differentiation and a low-friction next step. Do not write generic mid-funnel content regardless
            of stage.
            Call-to-Action: {callToAction}

            Supporting Evidence/Points (cite/paraphrase these; do not invent facts beyond them):
            - {evidenceText}

            Requirements:
            - Write in a professional but engaging tone.
            - 3-5 top-level sections, each substantive (not filler).
            - Include the call-to-action naturally in the final section.
            - Aim for 1500-2000 words across all sections combined.
            """;

        var raw = await GenerateWithOpenAiAsync(system, user, ct);
        return ExtractAndValidateContentDocument(raw);
    }

    public async Task<string> ReviseStructuredDraftAsync(
        string currentDocumentJson,
        string feedback,
        CancellationToken ct = default)
    {
        var system = $"""
            You are a professional content editor. Revise the given ContentDocument JSON according to the feedback.
            Respond with ONLY a single valid JSON object — no markdown fences, no commentary — matching exactly:
            {ContentDocumentJsonContract}
            Preserve structure unless the feedback requires adding/removing sections. Keep approved facts intact;
            do not invent claims. Apply the feedback thoroughly.
            """;

        var user = $"""
            Feedback: {feedback}

            Current document JSON:
            {currentDocumentJson}
            """;

        var raw = await GenerateWithOpenAiAsync(system, user, ct);
        return ExtractAndValidateContentDocument(raw);
    }

    public async Task<string> GenerateSectionAsync(
        string sectionHeading,
        string context,
        string specificFeedback,
        CancellationToken ct = default)
    {
        var system = "You are a professional content writer. Return only the revised section prose — no JSON, no metadata.";
        var user = $"""
            Section: {sectionHeading}

            Context: {context}

            Feedback: {specificFeedback}

            Write ONLY the section content, no metadata or explanations.
            """;

        return await GenerateWithOpenAiAsync(system, user, ct);
    }

    private async Task<string> GenerateWithOpenAiAsync(string system, string user, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException(
                "LlmProviders:OpenAi:ApiKey is not configured — cannot generate with the OpenAi provider.");
        }

        _logger.LogInformation("Calling OpenAI API for structured content generation");

        var request = new
        {
            model = _options.Model,
            messages = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = user },
            },
            response_format = new { type = "json_object" },
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, OpenAiApiUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json"),
        };
        httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.ApiKey);

        var response = await _httpClient.SendAsync(httpRequest, ct);
        var responseText = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("OpenAI API call failed ({Status}): {Body}", response.StatusCode, responseText);
            throw new InvalidOperationException($"OpenAI API error ({(int)response.StatusCode}): {responseText}");
        }

        var responseJson = JsonDocument.Parse(responseText);
        var root = responseJson.RootElement;
        var usage = root.GetProperty("usage");
        var inputTokens = usage.GetProperty("prompt_tokens").GetInt32();
        var outputTokens = usage.GetProperty("completion_tokens").GetInt32();
        var generatedText = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;

        _lastUsage = new TokenUsage
        {
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            EstimatedCost = 0m, // Not tracked for this provider yet — TokenUsage.EstimatedCost is Anthropic-specific today.
        };

        _logger.LogInformation(
            "OpenAI API call succeeded. Input: {InputTokens}, Output: {OutputTokens}",
            inputTokens, outputTokens);

        return generatedText;
    }

    // Kept identical to ClaudeContentGenerator's contract/validation — same schema, same reason to
    // validate before trusting (json_object mode guarantees valid JSON syntax, not this app's
    // specific shape).
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

    private static string ExtractAndValidateContentDocument(string rawContent)
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
                $"OpenAI did not return a JSON object for structured draft generation. First 200 chars: {rawContent[..Math.Min(200, rawContent.Length)]}");
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
                $"OpenAI's structured draft response was not valid JSON: {ex.Message}. First 200 chars: {rawContent[..Math.Min(200, rawContent.Length)]}", ex);
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("lede", out var lede) || lede.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("sections", out var sections) || sections.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException(
                    "OpenAI's structured draft response is missing required top-level \"lede\" (string) or \"sections\" (array) fields.");
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
                "OpenAI's structured draft response has a section missing required \"heading\" (string) or \"paragraphs\" (array) fields.");
        }

        foreach (var paragraph in paragraphs.EnumerateArray())
        {
            if (paragraph.ValueKind != JsonValueKind.Object
                || !paragraph.TryGetProperty("$type", out var type)
                || type.ValueKind != JsonValueKind.String
                || (type.GetString() != "text" && type.GetString() != "list"))
            {
                throw new InvalidOperationException(
                    "OpenAI's structured draft response has a paragraph with an invalid or missing \"$type\" (must be \"text\" or \"list\").");
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
}
