using GeekApplication.Results;

namespace GeekApplication.Interfaces.Receptionist;

/// <summary>
/// The tools one business's receptionist may use in a call, and what running one does. The set is the business's own: a business with no
/// toolbox has a receptionist that can only talk and hang up. A tool that cannot be run is a failure result with a code, never an
/// exception and never a made-up answer; what the model is told is the code only.
/// </summary>
public interface IReceptionistToolbox
{
    IReadOnlyList<ReceptionistToolDefinition> Tools { get; }

    /// <summary>The tool's result as JSON, or a coded failure.</summary>
    Task<Result<string>> RunAsync(ReceptionistToolCall call, CancellationToken ct = default);

    /// <summary>
    /// Whether this tool is the one that puts an appointment in the business's calendar, so a call that used it can be flagged as
    /// booked. A toolbox with nothing that books says no to everything, which is the default.
    /// </summary>
    bool IsBooking(string toolName) => false;
}
