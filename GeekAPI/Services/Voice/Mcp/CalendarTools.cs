using System.Text.Json;
using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;

namespace GeekAPI.Services.Voice.Mcp;

/// <summary>
/// Runs one calendar tool for one tenant (docs/Geek-Receptionist.md sections 7 and 8). The tenant's live access token comes from
/// <see cref="GoogleAccessTokens"/>, which reads the stored connection and renews the token when it is close to expiring; this hands
/// that token to the gateway for one call. A token the gateway still refuses is reauthorization_required: it is not renewed a second
/// time and the call is not repeated. A tenant with no active connection is calendar_not_connected (404). Nothing is retried, and no
/// log line carries a token, an argument or a tool's content.
/// </summary>
public sealed class CalendarTools(
    GoogleAccessTokens tokens,
    McpGatewayClient gateway)
{
    public async Task<Result<McpToolResult>> CallAsync(Guid tenantId, string provider, string tool, JsonElement? arguments, CancellationToken ct = default)
    {
        if (!string.Equals(provider, GoogleCalendarOptions.Provider, StringComparison.Ordinal))
            return Result<McpToolResult>.Failure($"Only {GoogleCalendarOptions.Provider} can be used so far.", McpFailureCodes.ProviderNotSupported);

        var accessToken = await tokens.ForAsync(tenantId, provider, McpFailureCodes.CalendarNotConnected, ct).ConfigureAwait(false);
        if (!accessToken.IsSuccess) return Result<McpToolResult>.Failure(accessToken.Error!, accessToken.ErrorCode!);

        return await gateway.CallToolAsync(provider, accessToken.Value!, tool, arguments, ct).ConfigureAwait(false);
    }
}
