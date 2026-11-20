using System.Text.Json;
using GeekApplication.Interfaces.Receptionist;
using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;

namespace GeekAPI.Services.Voice.Mcp;

/// <summary>
/// The calendar tools one tenant's receptionist may use while a caller is on the line. The definitions are the gateway's own, which are
/// Google's own (<see cref="McpGatewayClient.ListToolsAsync"/>), so no argument schema is invented here and none can drift from what
/// Google accepts.
///
/// Only the tools that read a calendar are offered: a receptionist can see what is booked and when the owner is free, and cannot create,
/// change or delete anything. Booking is deliberately not here. <c>create_event</c> has no idempotency key, so a call that fails or times
/// out may still have booked, and what a business wants collected before an appointment is made is its own choice; both belong to the
/// booking step, not to this read-only slice.
///
/// A tool that fails gives the model its code and nothing else, and no log line carries an argument, a result, or anything a caller said.
/// </summary>
public sealed class CalendarToolbox : IReceptionistToolbox
{
    /// <summary>The gateway's tools that only read a calendar. Anything not named here is never offered, whatever the gateway lists.</summary>
    public static readonly IReadOnlySet<string> ReadOnlyTools =
        new HashSet<string>(StringComparer.Ordinal) { "list_calendars", "list_events", "get_event", "suggest_time" };

    private readonly Guid _tenantId;
    private readonly CalendarTools _calendar;
    private readonly ILogger _logger;

    private CalendarToolbox(Guid tenantId, IReadOnlyList<ReceptionistToolDefinition> tools, CalendarTools calendar, ILogger logger)
    {
        _tenantId = tenantId;
        Tools = tools;
        _calendar = calendar;
        _logger = logger;
    }

    public IReadOnlyList<ReceptionistToolDefinition> Tools { get; }

    /// <summary>
    /// A toolbox for this tenant, or null when the receptionist should simply have no calendar tools: the owner has not connected a
    /// calendar, or the gateway could not be asked. A null is not a failure of the call; the caller still answers the phone, without the
    /// calendar. A business that has connected no calendar is the ordinary case, not an error, and is not logged as one.
    /// </summary>
    public static async Task<CalendarToolbox?> CreateAsync(
        Guid tenantId, IVoiceStore store, CalendarTools calendar, CalendarToolCatalog catalog, McpGatewayClient gateway, ILogger logger,
        CancellationToken ct = default)
    {
        // The owner connecting their calendar is what asks for this; without it every tool would only ever answer calendar_not_connected.
        var connection = await store.GetMcpConnectionAsync(tenantId, GoogleCalendarOptions.Provider, ct).ConfigureAwait(false);
        if (!connection.IsSuccess || !connection.Value!.IsActive)
        {
            if (connection.IsSuccess || connection.ErrorCode == VoiceFailureCodes.NotFound)
                logger.LogInformation("Tenant {TenantId}: no calendar is connected, so this call has no calendar tools.", tenantId);
            else
                logger.LogError("Tenant {TenantId}: the calendar connection could not be read ({Code}); this call has no calendar tools.", tenantId, connection.ErrorCode);
            return null;
        }

        var tools = await catalog.ReadOnlyToolsAsync(gateway, logger, ct).ConfigureAwait(false);
        if (tools is null)
        {
            logger.LogError("Tenant {TenantId}: the calendar tools are not available, so this call has none.", tenantId);
            return null;
        }

        return new CalendarToolbox(tenantId, tools, calendar, logger);
    }

    public async Task<Result<string>> RunAsync(ReceptionistToolCall call, CancellationToken ct = default)
    {
        // The model is given only the read-only tools, so anything else is a model that went outside what it was offered.
        if (!ReadOnlyTools.Contains(call.Name))
        {
            _logger.LogError("Tenant {TenantId}: the receptionist asked for the calendar tool {Tool}, which it was not offered.", _tenantId, call.Name);
            return Result<string>.Failure("That calendar tool is not offered.", McpFailureCodes.ToolNotAllowed);
        }

        JsonElement? arguments;
        try
        {
            using var parsed = JsonDocument.Parse(string.IsNullOrWhiteSpace(call.ArgumentsJson) ? "{}" : call.ArgumentsJson);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
            {
                _logger.LogError("Tenant {TenantId}: the receptionist's arguments for {Tool} are not an object.", _tenantId, call.Name);
                return Result<string>.Failure("The calendar tool's arguments are not an object.", VoiceFailureCodes.InvalidRequest);
            }
            arguments = parsed.RootElement.Clone();
        }
        catch (JsonException)
        {
            _logger.LogError("Tenant {TenantId}: the receptionist's arguments for {Tool} are not JSON.", _tenantId, call.Name);
            return Result<string>.Failure("The calendar tool's arguments are not JSON.", VoiceFailureCodes.InvalidRequest);
        }

        var result = await _calendar.CallAsync(_tenantId, GoogleCalendarOptions.Provider, call.Name, arguments, ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            _logger.LogError("Tenant {TenantId}: the calendar tool {Tool} failed ({Code}).", _tenantId, call.Name, result.ErrorCode);
            return Result<string>.Failure(result.Error!, result.ErrorCode!);
        }

        // Structured content when the tool sent it, because it is the shape a model reads best; otherwise what it did send.
        var answer = result.Value!.StructuredContent ?? result.Value.Content;
        return Result<string>.Success(answer.GetRawText());
    }
}
