using GeekApplication.Interfaces.Receptionist;

namespace GeekAPI.Services.Voice.Mcp;

/// <summary>
/// The calendar tools the gateway offers, kept for <see cref="Lifetime"/> so answering a phone call does not wait on a round trip to
/// list them. The list is the same for every tenant and holds nothing of any tenant's: no token, no calendar, no call. One fetch is made
/// at a time, so a burst of calls on a cold process makes one request, not one each. A fetch that fails is not cached, so the next call
/// tries again; the gateway client is passed in rather than held, because it is a scoped HTTP client and a singleton must not keep one.
/// </summary>
public sealed class CalendarToolCatalog(TimeProvider clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<ReceptionistToolDefinition>? _tools;
    private DateTimeOffset _freshUntil;

    /// <summary>Every tool the gateway offers, or null when it could not be asked or offers none. Which of them a business may use is not decided here.</summary>
    public async Task<IReadOnlyList<ReceptionistToolDefinition>?> ToolsAsync(McpGatewayClient gateway, ILogger logger, CancellationToken ct = default)
    {
        if (_tools is { } cached && clock.GetUtcNow() < _freshUntil) return cached;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_tools is { } other && clock.GetUtcNow() < _freshUntil) return other;

            var offered = await gateway.ListToolsAsync(GoogleCalendarOptions.Provider, ct).ConfigureAwait(false);
            if (!offered.IsSuccess)
            {
                logger.LogError("The calendar tools could not be listed ({Code}).", offered.ErrorCode);
                return null;
            }

            var tools = offered.Value!
                .Select(tool => new ReceptionistToolDefinition(tool.Name, tool.Description, tool.InputSchema))
                .ToList();
            if (tools.Count == 0)
            {
                logger.LogError("The calendar gateway offers no tools.");
                return null;
            }

            _tools = tools;
            _freshUntil = clock.GetUtcNow() + Lifetime;
            return tools;
        }
        finally
        {
            _gate.Release();
        }
    }
}
