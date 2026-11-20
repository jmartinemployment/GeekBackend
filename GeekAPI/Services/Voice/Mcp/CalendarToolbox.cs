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
    /// <summary>The gateway's tools that only read a calendar. Anything not named here or <see cref="BookingTool"/> is never offered, whatever the gateway lists.</summary>
    public static readonly IReadOnlySet<string> ReadOnlyTools =
        new HashSet<string>(StringComparer.Ordinal) { "list_calendars", "list_events", "get_event", "suggest_time" };

    /// <summary>The one tool that writes to the owner's calendar. Offered only to a business that asked for booking.</summary>
    public const string BookingTool = "create_event";

    /// <summary>The tool a booking whose answer was lost is looked for with; it is never booked a second time.</summary>
    public const string ListTool = "list_events";

    private readonly Guid _tenantId;
    private readonly CalendarTools _calendar;
    private readonly bool _mayBook;
    private readonly ILogger _logger;

    private CalendarToolbox(Guid tenantId, IReadOnlyList<ReceptionistToolDefinition> tools, CalendarTools calendar, bool mayBook, ILogger logger)
    {
        _tenantId = tenantId;
        Tools = tools;
        _calendar = calendar;
        _mayBook = mayBook;
        _logger = logger;
    }

    public IReadOnlyList<ReceptionistToolDefinition> Tools { get; }

    public bool IsBooking(string toolName) => string.Equals(toolName, BookingTool, StringComparison.Ordinal);

    /// <summary>
    /// A toolbox for this tenant, or null when the receptionist should simply have no calendar tools: the owner has not connected a
    /// calendar, or the gateway could not be asked. A null is not a failure of the call; the caller still answers the phone, without the
    /// calendar. A business that has connected no calendar is the ordinary case, not an error, and is not logged as one.
    /// </summary>
    public static async Task<CalendarToolbox?> CreateAsync(
        Guid tenantId, IVoiceStore store, CalendarTools calendar, CalendarToolCatalog catalog, McpGatewayClient gateway, ILogger logger,
        bool mayBook = false, CancellationToken ct = default)
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

        var offered = await catalog.ToolsAsync(gateway, logger, ct).ConfigureAwait(false);
        if (offered is null)
        {
            logger.LogError("Tenant {TenantId}: the calendar tools are not available, so this call has no calendar tools.", tenantId);
            return null;
        }

        var tools = offered.Where(tool => ReadOnlyTools.Contains(tool.Name) || (mayBook && tool.Name == BookingTool)).ToList();
        if (tools.Count == 0)
        {
            logger.LogError("Tenant {TenantId}: the calendar gateway offers nothing this receptionist may use; this call has no calendar tools.", tenantId);
            return null;
        }

        return new CalendarToolbox(tenantId, tools, calendar, mayBook && tools.Any(tool => tool.Name == BookingTool), logger);
    }

    public async Task<Result<string>> RunAsync(ReceptionistToolCall call, CancellationToken ct = default)
    {
        if (call.Name == BookingTool && !_mayBook)
        {
            _logger.LogError("Tenant {TenantId}: the receptionist asked to book, which this business has not turned on.", _tenantId);
            return Result<string>.Failure("This business's receptionist does not book appointments.", McpFailureCodes.ToolNotAllowed);
        }

        // The model is given only the tools above, so anything else is a model that went outside what it was offered.
        if (!ReadOnlyTools.Contains(call.Name) && call.Name != BookingTool)
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
        if (result.IsSuccess) return Answer(result.Value!);

        // A booking whose answer was lost may still have been made, so it is looked for rather than sent again.
        if (call.Name == BookingTool && result.ErrorCode == McpFailureCodes.GatewayUnavailable)
            return await ReconcileAsync(arguments!.Value, result, ct).ConfigureAwait(false);

        _logger.LogError("Tenant {TenantId}: the calendar tool {Tool} failed ({Code}).", _tenantId, call.Name, result.ErrorCode);
        return Result<string>.Failure(result.Error!, result.ErrorCode!);
    }

    /// <summary>Structured content when the tool sent it, because it is the shape a model reads best; otherwise what it did send.</summary>
    private static Result<string> Answer(McpToolResult result) =>
        Result<string>.Success((result.StructuredContent ?? result.Content).GetRawText());

    /// <summary>
    /// What became of a booking whose answer never arrived. Google's tool takes no event id, so sending it again could book the caller
    /// twice; instead the calendar is read over the time that was asked for and the event is looked for by its summary. Found, the booking
    /// stands. Not found, the model is told the booking failed, and may book again. Unreadable, the outcome is
    /// <see cref="McpFailureCodes.BookingOutcomeUnknown"/>: nobody may say it was booked and nobody may book it again here.
    /// </summary>
    private async Task<Result<string>> ReconcileAsync(JsonElement booking, Result<McpToolResult> failure, CancellationToken ct)
    {
        var summary = Text(booking, "summary");
        var query = ListArguments(booking);
        if (summary is null || query is null)
        {
            _logger.LogError("Tenant {TenantId}: a booking's answer was lost and it cannot be looked for, so its outcome is unknown.", _tenantId);
            return Unknown();
        }

        _logger.LogWarning("Tenant {TenantId}: a booking's answer was lost; the calendar is being read to see whether it was made.", _tenantId);

        var listed = await _calendar.CallAsync(_tenantId, GoogleCalendarOptions.Provider, ListTool, query, ct).ConfigureAwait(false);
        if (!listed.IsSuccess)
        {
            _logger.LogError("Tenant {TenantId}: a booking's answer was lost and the calendar could not be read ({Code}), so its outcome is unknown.",
                _tenantId, listed.ErrorCode);
            return Unknown();
        }

        if (Mentions(listed.Value!.StructuredContent ?? listed.Value.Content, summary))
        {
            _logger.LogWarning("Tenant {TenantId}: a booking whose answer was lost was found on the calendar and stands.", _tenantId);
            return Answer(listed.Value);
        }

        _logger.LogError("Tenant {TenantId}: a booking's answer was lost and it is not on the calendar, so it was not made ({Code}).",
            _tenantId, failure.ErrorCode);
        return Result<string>.Failure(failure.Error!, failure.ErrorCode!);
    }

    private static Result<string> Unknown() =>
        Result<string>.Failure("Whether the appointment was booked could not be established.", McpFailureCodes.BookingOutcomeUnknown);

    /// <summary>The arguments to read the calendar over the time a booking asked for; null when it named no time.</summary>
    private static JsonElement? ListArguments(JsonElement booking)
    {
        var start = Text(booking, "startTime");
        var end = Text(booking, "endTime");
        if (start is null || end is null) return null;

        var query = new Dictionary<string, string> { ["startTime"] = start, ["endTime"] = end };
        if (Text(booking, "calendarId") is { } calendarId) query["calendarId"] = calendarId;
        if (Text(booking, "timeZone") is { } timeZone) query["timeZone"] = timeZone;

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(query));
        return document.RootElement.Clone();
    }

    private static string? Text(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    /// <summary>
    /// Whether anything the calendar returned is summarised exactly as the booking was. The shape of Google's answer is Google's, so every
    /// "summary" in it is looked at wherever it sits, rather than a path being assumed and quietly missing the event.
    /// </summary>
    private static bool Mentions(JsonElement element, string summary)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.NameEquals("summary") && property.Value.ValueKind == JsonValueKind.String
                        && string.Equals(property.Value.GetString(), summary, StringComparison.Ordinal))
                        return true;
                    if (Mentions(property.Value, summary)) return true;
                }
                return false;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    if (Mentions(item, summary)) return true;
                return false;
            default:
                return false;
        }
    }
}
