namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// What a run's record says about an exception, so a failure can be understood from the record alone.
/// </summary>
/// <remarks>
/// <para>
/// A run says two different things with an exception. A <b>refusal</b> ("Refused: the pillar. ...") is
/// the run declining a draft or a request on purpose, and its message is the whole story. A <b>fault</b>
/// is anything else: a bug, a bad row, a service that was down, a model reply nobody could parse. Its
/// message is rarely enough, so the record carries its type, its stack and its inner exceptions. Until
/// 2026-10-07 the record kept only <c>ex.Message</c>, so a fault in the code read the same as a draft
/// that broke a rule.
/// </para>
/// <para>
/// The stack of a refusal is left out: it names the line that threw the refusal, which says nothing the
/// message does not, and a refused run can hold a dozen of them.
/// </para>
/// </remarks>
internal static class GccRunFault
{
    /// <summary>Enough of a stack to find the line, not a log line the size of a page.</summary>
    private const int MaxStackChars = 8_000;

    private const int MaxInnerExceptions = 5;

    /// <summary>
    /// Whether the exception is a refusal: the run declining, in words, on purpose. Every refusal the
    /// generators throw says "Refused:", and a joined failure names each type's refusal the same way.
    /// </summary>
    internal static bool IsRefusal(Exception ex) => ex.Message.Contains("Refused:", StringComparison.Ordinal);

    /// <summary>The exception as the run's record writes it.</summary>
    internal static object Describe(Exception ex)
    {
        var refusal = IsRefusal(ex);
        return new
        {
            kind = refusal ? "refusal" : "fault",
            type = ex.GetType().FullName,
            message = ex.Message,
            stackTrace = refusal ? null : Trimmed(ex.StackTrace),
            inner = Inner(ex),
        };
    }

    private static string? Trimmed(string? stack) =>
        string.IsNullOrEmpty(stack) || stack.Length <= MaxStackChars ? stack : stack[..MaxStackChars] + "...";

    private static List<object> Inner(Exception ex)
    {
        var chain = new List<object>();
        for (var inner = ex.InnerException; inner is not null && chain.Count < MaxInnerExceptions; inner = inner.InnerException)
        {
            chain.Add(new { type = inner.GetType().FullName, message = inner.Message });
        }

        return chain;
    }
}
