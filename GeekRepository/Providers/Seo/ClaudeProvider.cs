using System.Net.Http.Json;
using System.Text.Json;
using GeekApplication.Interfaces.Seo;
using GeekApplication.Models.Seo;
using GeekApplication.Results;

namespace GeekRepository.Providers.Seo;

public sealed class ClaudeProvider(IHttpClientFactory httpClientFactory) : IAIProvider
{
    public string ProviderName => "anthropic";

    public async Task<Result<AIResponse>> CompleteAsync(AIRequest request, CancellationToken ct = default)
    {
        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Result<AIResponse>.Failure(
                "ANTHROPIC_API_KEY is not set. Add it to GeekRepository to enable AI article generation.");
        }

        var client = httpClientFactory.CreateClient("Anthropic");
        client.DefaultRequestHeaders.Add("x-api-key", apiKey);
        client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        var body = new
        {
            model = request.Model,
            max_tokens = request.MaxTokens,
            temperature = request.Temperature,
            system = request.SystemPrompt,
            messages = new[] { new { role = "user", content = request.UserPrompt } },
        };

        using var response = await client.PostAsJsonAsync("/v1/messages", body, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            return Result<AIResponse>.Failure($"Anthropic API {(int)response.StatusCode}: {raw}");

        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;
        var content = root.GetProperty("content")[0].GetProperty("text").GetString() ?? string.Empty;
        var model = root.GetProperty("model").GetString() ?? request.Model;
        var inputTokens = root.GetProperty("usage").GetProperty("input_tokens").GetInt32();
        var outputTokens = root.GetProperty("usage").GetProperty("output_tokens").GetInt32();
        var stop = root.GetProperty("stop_reason").GetString() ?? "end_turn";

        return Result<AIResponse>.Success(new AIResponse
        {
            Content = content,
            Model = model,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            StopReason = stop,
        });
    }
}
