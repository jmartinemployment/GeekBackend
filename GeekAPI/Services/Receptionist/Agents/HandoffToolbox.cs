using System.Text.Json;
using GeekAPI.Services.Voice.Mcp;
using GeekApplication.Interfaces.Receptionist;
using GeekApplication.Results;

namespace GeekAPI.Services.Receptionist.Agents;

/// <summary>
/// The one tool every role has that no role owns: handing the caller to the role that does what they are asking for.
///
/// <para>
/// A handoff is a tool call rather than a parsed phrase in the reply, so it cannot be triggered by the model happening to say a word, and
/// a role that is not offered a destination cannot reach it. The destinations are the other roles this business was given, by name, with
/// their purposes — so the model chooses from what exists rather than naming a role that was never built.
/// </para>
///
/// <para>
/// It does not perform the handoff. It validates it and reports where to go; the call loop is what swaps the role, because the loop owns
/// the conversation and a toolbox that reached in to change it would be two things deciding who is speaking.
/// </para>
/// </summary>
public sealed class HandoffToolbox : IReceptionistToolbox
{
    public const string Tool = "hand_off";

    private readonly IReadOnlyDictionary<string, string> _destinations;

    private HandoffToolbox(IReadOnlyDictionary<string, string> destinations, IReadOnlyList<ReceptionistToolDefinition> tools)
    {
        _destinations = destinations;
        Tools = tools;
    }

    public IReadOnlyList<ReceptionistToolDefinition> Tools { get; }

    /// <summary>Where a handoff asked to go, when the last call was a valid one. The loop reads this instead of parsing the tool's answer back out of JSON.</summary>
    public string? Requested { get; private set; }

    /// <summary>
    /// A handoff tool offering these destinations, or null when there are none — a business with one role has nowhere to hand to, and a
    /// role offered a handoff with no destinations would be offered a tool that can only fail.
    /// </summary>
    public static HandoffToolbox? For(IReadOnlyDictionary<string, string> destinations)
    {
        if (destinations.Count == 0) return null;

        var names = string.Join(", ", destinations.Select(d => $"{d.Key} ({d.Value})"));
        using var schema = JsonDocument.Parse(
            $$"""
            {
              "type": "object",
              "properties": {
                "to": {
                  "type": "string",
                  "enum": [{{string.Join(", ", destinations.Keys.Select(k => JsonSerializer.Serialize(k)))}}],
                  "description": "Which part of this business's reception should take over."
                }
              },
              "required": ["to"]
            }
            """);

        var definition = new ReceptionistToolDefinition(
            Tool,
            $"Hand this caller to the part of reception that handles what they are asking for. Do this as soon as you know what they want, "
            + $"rather than attempting it yourself. The caller hears nothing and does not repeat themselves. Available: {names}.",
            schema.RootElement.Clone());

        return new HandoffToolbox(destinations, [definition]);
    }

    public Task<Result<string>> RunAsync(ReceptionistToolCall call, CancellationToken ct = default)
    {
        Requested = null;

        if (!string.Equals(call.Name, Tool, StringComparison.Ordinal))
            return Task.FromResult(Result<string>.Failure("That tool is not offered.", McpFailureCodes.ToolNotAllowed));

        var to = Destination(call.ArgumentsJson);
        if (to is null)
            return Task.FromResult(Result<string>.Failure("A handoff must name where it is going.", HandoffFailureCodes.NoDestination));

        // A role that was never given to this business, or a model inventing one. Told plainly, with what does exist, so the model's next
        // attempt is a real destination rather than another guess.
        if (!_destinations.TryGetValue(to, out var purpose))
            return Task.FromResult(Result<string>.Failure(
                $"There is no such part of reception. Available: {string.Join(", ", _destinations.Keys)}.",
                HandoffFailureCodes.UnknownDestination));

        Requested = to;
        return Task.FromResult(Result<string>.Success(JsonSerializer.Serialize(new { handedTo = to, purpose })));
    }

    private static string? Destination(string? argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson)) return null;
        try
        {
            using var parsed = JsonDocument.Parse(argumentsJson);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!parsed.RootElement.TryGetProperty("to", out var to) || to.ValueKind != JsonValueKind.String) return null;
            var name = to.GetString();
            return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>Why a handoff did not happen. Both mean the caller stays with the role they are already speaking to.</summary>
public static class HandoffFailureCodes
{
    public const string NoDestination = "handoff_no_destination";
    public const string UnknownDestination = "handoff_unknown_destination";
}
