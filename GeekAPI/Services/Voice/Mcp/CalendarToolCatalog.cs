using System.Collections.Concurrent;
using GeekApplication.Interfaces.Receptionist;
using GeekApplication.Models.Voice;

namespace GeekAPI.Services.Voice.Mcp;

/// <summary>
/// The tools the gateway offers for a provider, kept for <see cref="Lifetime"/> so answering a phone call does not wait on a round trip
/// to list them. A provider's list is the same for every tenant and holds nothing of any tenant's: no token, no calendar, no call. One
/// fetch per provider is made at a time, so a burst of calls on a cold process makes one request, not one each. A fetch that fails is
/// not cached, so the next call tries again; the gateway client is passed in rather than held, because it is a scoped HTTP client and a
/// singleton must not keep one.
///
/// <para>
/// <b>The cache is per provider, and that is not an optimisation.</b> It held one list for every tenant and asked the gateway only ever
/// for Google Calendar, which was correct while one provider could be connected and became wrong the moment a second could: a tenant
/// connected to a CRM would have been offered a calendar's tools, and the model would have called one. The gateway would refuse it, so
/// nothing incorrect would have happened to the business - but it would have happened mid-call, with the turn already spent.
/// Keyed by provider, a tenant can only ever be offered the tools of a provider it actually connected.
/// </para>
/// </summary>
public sealed class CalendarToolCatalog(TimeProvider clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    /// <summary>One provider's tools, and the one gate that keeps a cold burst to a single fetch for it.</summary>
    private sealed class Entry
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public IReadOnlyList<ReceptionistToolDefinition>? Tools { get; set; }
        public DateTimeOffset FreshUntil { get; set; }
    }

    private readonly ConcurrentDictionary<string, Entry> _byProvider = new(StringComparer.Ordinal);

    /// <summary>
    /// Every tool the gateway offers for <paramref name="provider"/>, or null when it could not be asked or offers none. Which of them
    /// a business may use is not decided here. A provider this product does not have is refused rather than asked for: the dictionary
    /// is keyed by whatever is passed, so an unknown key would both grow it without bound and send the gateway a provider it cannot
    /// answer for.
    /// </summary>
    public async Task<IReadOnlyList<ReceptionistToolDefinition>?> ToolsAsync(
        string provider, McpGatewayClient gateway, ILogger logger, CancellationToken ct = default)
    {
        if (!VoiceMcpProviders.All.Contains(provider, StringComparer.Ordinal))
        {
            logger.LogError("The tools for {Provider} were asked for, which is not a provider this product has.", provider);
            return null;
        }

        var entry = _byProvider.GetOrAdd(provider, _ => new Entry());

        if (entry.Tools is { } cached && clock.GetUtcNow() < entry.FreshUntil) return cached;

        await entry.Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (entry.Tools is { } other && clock.GetUtcNow() < entry.FreshUntil) return other;

            var offered = await gateway.ListToolsAsync(provider, ct).ConfigureAwait(false);
            if (!offered.IsSuccess)
            {
                logger.LogError("The {Provider} tools could not be listed ({Code}).", provider, offered.ErrorCode);
                return null;
            }

            var tools = offered.Value!
                .Select(tool => new ReceptionistToolDefinition(tool.Name, tool.Description, tool.InputSchema))
                .ToList();
            if (tools.Count == 0)
            {
                logger.LogError("The gateway offers no tools for {Provider}.", provider);
                return null;
            }

            entry.Tools = tools;
            entry.FreshUntil = clock.GetUtcNow() + Lifetime;
            return tools;
        }
        finally
        {
            entry.Gate.Release();
        }
    }
}
