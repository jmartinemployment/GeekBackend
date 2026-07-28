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
    private const string ClaudeModel = "claude-3-5-sonnet-20241022";

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
