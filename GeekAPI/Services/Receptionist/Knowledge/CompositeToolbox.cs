using GeekAPI.Services.Voice.Mcp;
using GeekApplication.Interfaces.Receptionist;
using GeekApplication.Results;

namespace GeekAPI.Services.Receptionist.Knowledge;

/// <summary>
/// One receptionist's tools when they come from more than one place — its calendar and its business information — presented to the model
/// as a single set.
///
/// <para>
/// A call is given one toolbox, and each source builds its own and knows nothing of the others. Composing them here keeps that: the
/// calendar toolbox still decides what a calendar may do, the knowledge toolbox still decides what a lookup may answer, and neither grows
/// a branch for the other.
/// </para>
///
/// <para>
/// A tool is routed to the toolbox that offered it. A name offered by two would be ambiguous, so the first to offer it wins and the
/// duplicate is dropped from what the model is shown — the model is never offered a name that would not reach the tool it describes.
/// </para>
/// </summary>
public sealed class CompositeToolbox : IReceptionistToolbox
{
    private readonly Dictionary<string, IReceptionistToolbox> _byTool;

    private CompositeToolbox(Dictionary<string, IReceptionistToolbox> byTool, IReadOnlyList<ReceptionistToolDefinition> tools)
    {
        _byTool = byTool;
        Tools = tools;
    }

    public IReadOnlyList<ReceptionistToolDefinition> Tools { get; }

    /// <summary>
    /// The toolboxes that exist, as one. Nulls are the ordinary case — a business with no calendar, or none with an indexed site — and are
    /// dropped. Nothing left is null, not an empty toolbox: a receptionist with no tools is told it has none rather than offered an empty
    /// set. One toolbox is returned as itself, so composing costs nothing when there is nothing to compose.
    /// </summary>
    public static IReceptionistToolbox? Of(params IReceptionistToolbox?[] boxes)
    {
        // A lone `null` literal binds the params array itself rather than one element, so the array is checked before it is read.
        if (boxes is null) return null;

        var present = boxes.Where(box => box is not null).Select(box => box!).Where(box => box.Tools.Count > 0).ToList();
        if (present.Count == 0) return null;
        if (present.Count == 1) return present[0];

        var byTool = new Dictionary<string, IReceptionistToolbox>(StringComparer.Ordinal);
        var tools = new List<ReceptionistToolDefinition>();
        foreach (var box in present)
        {
            foreach (var tool in box.Tools)
            {
                if (!byTool.TryAdd(tool.Name, box)) continue;
                tools.Add(tool);
            }
        }

        return new CompositeToolbox(byTool, tools);
    }

    public bool IsBooking(string toolName) =>
        toolName is not null && _byTool.TryGetValue(toolName, out var box) && box.IsBooking(toolName);

    public Task<Result<string>> RunAsync(ReceptionistToolCall call, CancellationToken ct = default)
    {
        if (call.Name is not null && _byTool.TryGetValue(call.Name, out var box)) return box.RunAsync(call, ct);

        // The model was offered only the names above, so this is a model that went outside its set. It is refused here rather than
        // guessed at: handing it to one of the toolboxes would make that toolbox answer for a tool that is not its own.
        return Task.FromResult(Result<string>.Failure("That tool is not offered.", McpFailureCodes.ToolNotAllowed));
    }
}
