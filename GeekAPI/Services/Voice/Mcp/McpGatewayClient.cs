using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GeekApplication.Models.Voice;
using GeekApplication.Results;

namespace GeekAPI.Services.Voice.Mcp;

/// <summary>
/// Where GeekMcpGateway is and the key it expects (Railway variables MCP_GATEWAY_URL and MCP_GATEWAY_API_KEY on GeekAPI). Read once and
/// checked when a call needs them, not at startup: an unset variable fails the call with a logged error and a 503.
/// </summary>
public sealed record McpGatewayOptions(string? BaseUrl, string? ApiKey)
{
    public static McpGatewayOptions FromEnvironment() => From(Environment.GetEnvironmentVariable);

    public static McpGatewayOptions From(Func<string, string?> read) =>
        new(read("MCP_GATEWAY_URL")?.Trim().TrimEnd('/'), read("MCP_GATEWAY_API_KEY")?.Trim());

    public IReadOnlyList<string> Missing()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(BaseUrl)) missing.Add("MCP_GATEWAY_URL");
        if (string.IsNullOrWhiteSpace(ApiKey)) missing.Add("MCP_GATEWAY_API_KEY");
        return missing;
    }
}

/// <summary>What a calendar tool returned: its content and, when it sent one, its structured content.</summary>
public sealed record McpToolResult(JsonElement Content, JsonElement? StructuredContent);

/// <summary>One tool the gateway offers, exactly as the provider describes it: nothing here is written by us.</summary>
public sealed record McpToolDefinition(string Name, string Description, JsonElement InputSchema);

/// <summary>
/// GeekAPI's side of GeekMcpGateway (docs/Geek-Receptionist.md section 8, B1). The tenant's decrypted access token goes in the body of
/// one call and nowhere else; nothing here logs a token, an argument or a tool's content. Asked once, never retried. The gateway's own
/// statuses become codes: 401 token_rejected is reauthorization_required (the token was just renewed or is fresh, so only the owner
/// can fix it), 400 tool_not_allowed and 422 tool_error keep their meaning, and every other failure is the gateway being unavailable.
/// </summary>
public sealed class McpGatewayClient(HttpClient http, McpGatewayOptions options, ILogger<McpGatewayClient> logger)
{
    public async Task<Result<McpToolResult>> CallToolAsync(string provider, string accessToken, string tool, JsonElement? arguments, CancellationToken ct = default)
    {
        var missing = options.Missing();
        if (missing.Count > 0)
        {
            logger.LogError("Calendar tools cannot be called: {Variables} not set.", string.Join(", ", missing));
            return Result<McpToolResult>.Failure("The calendar gateway is not configured.", McpFailureCodes.GatewayNotConfigured);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{options.BaseUrl}/v1/mcp/call")
        {
            Content = JsonContent.Create(new { provider, accessToken, tool, arguments }),
        };
        request.Headers.Add("X-Internal-Api-Key", options.ApiKey);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "The calendar gateway is unreachable.");
            return Unavailable("The calendar gateway could not be reached.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogError("The calendar gateway timed out.");
            return Unavailable("The calendar gateway did not answer in time.");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            JsonDocument? document = null;
            try
            {
                try
                {
                    document = JsonDocument.Parse(body);
                }
                catch (JsonException)
                {
                    logger.LogError("The calendar gateway answered {Status} with something that is not JSON.", (int)response.StatusCode);
                    return Unavailable("The calendar gateway answered something unexpected.");
                }

                var root = document.RootElement;
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    if (!root.TryGetProperty("content", out var content))
                    {
                        logger.LogError("The calendar gateway's answer carries no content.");
                        return Unavailable("The calendar gateway answered something unexpected.");
                    }

                    JsonElement? structured = root.TryGetProperty("structuredContent", out var s) && s.ValueKind == JsonValueKind.Object ? s.Clone() : null;
                    return Result<McpToolResult>.Success(new McpToolResult(content.Clone(), structured));
                }

                var code = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
                logger.LogWarning("The calendar gateway refused a {Tool} call with {Status} {Code}.", tool, (int)response.StatusCode, code);

                return (response.StatusCode, code) switch
                {
                    (HttpStatusCode.Unauthorized, "token_rejected") =>
                        Result<McpToolResult>.Failure("Google no longer accepts this calendar connection. Connect the calendar again.", McpFailureCodes.ReauthorizationRequired),
                    (HttpStatusCode.BadRequest, "tool_not_allowed") =>
                        Result<McpToolResult>.Failure("That calendar tool is not offered.", McpFailureCodes.ToolNotAllowed),
                    (HttpStatusCode.BadRequest, _) =>
                        Result<McpToolResult>.Failure("The calendar request was refused.", VoiceFailureCodes.InvalidRequest),
                    (HttpStatusCode.UnprocessableEntity, "tool_error") =>
                        Result<McpToolResult>.Failure("The calendar tool reported an error.", McpFailureCodes.ToolError),
                    _ => Unavailable("The calendar gateway failed."),
                };
            }
            finally
            {
                document?.Dispose();
            }
        }
    }

    /// <summary>
    /// The tools the gateway offers for a provider, with the provider's own descriptions and argument schemas, so nothing here invents a
    /// contract Google owns. No token is needed and none is sent: this says what may be called, not what any tenant has.
    /// </summary>
    public async Task<Result<IReadOnlyList<McpToolDefinition>>> ListToolsAsync(string provider, CancellationToken ct = default)
    {
        var missing = options.Missing();
        if (missing.Count > 0)
        {
            logger.LogError("The calendar tools cannot be listed: {Variables} not set.", string.Join(", ", missing));
            return Result<IReadOnlyList<McpToolDefinition>>.Failure("The calendar gateway is not configured.", McpFailureCodes.GatewayNotConfigured);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{options.BaseUrl}/v1/mcp/tools")
        {
            Content = JsonContent.Create(new { provider }),
        };
        request.Headers.Add("X-Internal-Api-Key", options.ApiKey);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "The calendar gateway is unreachable.");
            return UnavailableTools("The calendar gateway could not be reached.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogError("The calendar gateway timed out listing its tools.");
            return UnavailableTools("The calendar gateway did not answer in time.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("The calendar gateway refused to list its tools with {Status}.", (int)response.StatusCode);
                return UnavailableTools("The calendar gateway would not list its tools.");
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            }
            catch (JsonException)
            {
                logger.LogError("The calendar gateway listed its tools with something that is not JSON.");
                return UnavailableTools("The calendar gateway answered something unexpected.");
            }

            using (document)
            {
                if (!document.RootElement.TryGetProperty("tools", out var list) || list.ValueKind != JsonValueKind.Array)
                {
                    logger.LogError("The calendar gateway's tool list carries no tools.");
                    return UnavailableTools("The calendar gateway answered something unexpected.");
                }

                var tools = new List<McpToolDefinition>();
                foreach (var tool in list.EnumerateArray())
                {
                    // A tool without a name or a schema cannot be offered to a model; it is left out rather than guessed at.
                    if (!tool.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(name.GetString())
                        || !tool.TryGetProperty("inputSchema", out var schema) || schema.ValueKind != JsonValueKind.Object)
                        continue;
                    tools.Add(new McpToolDefinition(
                        name.GetString()!,
                        tool.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString()! : "",
                        schema.Clone()));
                }

                logger.LogInformation("The calendar gateway offers {Count} tools for {Provider}.", tools.Count, provider);
                return Result<IReadOnlyList<McpToolDefinition>>.Success(tools);
            }
        }
    }

    private static Result<IReadOnlyList<McpToolDefinition>> UnavailableTools(string message) =>
        Result<IReadOnlyList<McpToolDefinition>>.Failure(message, McpFailureCodes.GatewayUnavailable);

    private static Result<McpToolResult> Unavailable(string message) =>
        Result<McpToolResult>.Failure(message, McpFailureCodes.GatewayUnavailable);
}
