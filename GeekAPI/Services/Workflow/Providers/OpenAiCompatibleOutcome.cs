namespace GeekAPI.Services.Workflow.Providers;

/// <summary>
/// Decides whether a chat-completions answer is a usable answer, and names the reason when it is not.
/// </summary>
/// <remarks>
/// <para>
/// A response the model cut short, refused or had filtered is a failed call, and it is reported as
/// one, first time, by name. It used to fall through: <c>finish_reason</c> was parsed and never read,
/// so a body that hit its output ceiling surfaced as "did not return a valid sections array" with the
/// first 200 characters, and a refusal -- <c>content: null</c> -- as an <c>ArgumentNullException</c>
/// from a regex three layers away. Neither said what happened. Nothing here retries or repairs: the
/// run records the reason and stops.
/// </para>
/// <para>
/// The text the model did write is carried on the exception so the run log can show where it stopped.
/// </para>
/// </remarks>
internal static class OpenAiCompatibleOutcome
{
    /// <summary>Returns the answer's text, or throws <see cref="ContentGenerationException"/> naming why it cannot be used.</summary>
    public static string RequireUsableContent(OpenAiChoice choice, string provider, int maxOutputTokens)
    {
        var finish = choice.FinishReason;
        var content = choice.Message.Content;

        if (!string.IsNullOrWhiteSpace(choice.Message.Refusal))
        {
            throw new ContentGenerationException($"{provider} refused the request: {choice.Message.Refusal}")
            {
                FinishReason = finish,
                PartialResponse = content,
            };
        }

        if (string.Equals(finish, "length", StringComparison.OrdinalIgnoreCase))
        {
            throw new ContentGenerationException(
                $"{provider} stopped at the output limit of {maxOutputTokens} tokens before finishing; " +
                "the response is incomplete." + BlankRunNote(content))
            {
                FinishReason = finish,
                PartialResponse = content,
            };
        }

        if (string.Equals(finish, "content_filter", StringComparison.OrdinalIgnoreCase))
        {
            throw new ContentGenerationException($"{provider} filtered the response (finish_reason content_filter).")
            {
                FinishReason = finish,
                PartialResponse = content,
            };
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ContentGenerationException(
                $"{provider} returned no content (finish_reason {finish ?? "not given"}).")
            {
                FinishReason = finish,
            };
        }

        return content;
    }

    /// <summary>How many blank characters a cut reply must end in before the message says so.</summary>
    internal const int BlankRunWorthNaming = 32;

    /// <summary>
    /// What a reply cut at its limit is said to have ended in, when it ended in blanks; empty otherwise.
    /// </summary>
    /// <remarks>
    /// A reply cut for its length ends in the middle of a word. On 2026-10-13 two replies of one run
    /// ended instead in 2,913 tabs and in 2,481 spaces: the model had stopped writing its answer and
    /// filled the rest of the reply with blanks, which the reply's format allows between any two parts
    /// of it. The two read alike by their stop reason and call for opposite answers: the first needs a
    /// larger limit, and a larger limit only buys the second more blanks. So the message says which.
    /// </remarks>
    internal static string BlankRunNote(string? content)
    {
        if (string.IsNullOrEmpty(content)) return string.Empty;

        var blanks = content.Length - content.TrimEnd().Length;
        return blanks < BlankRunWorthNaming
            ? string.Empty
            : $" Its last {blanks:N0} characters are blank: the model stopped writing its answer and filled the "
                + "rest of the reply with blanks, so a larger limit would not have finished it.";
    }

    /// <summary>
    /// A call the HTTP client abandoned because the provider did not answer in time. The caller's own
    /// cancellation is not this: that one is the run being stopped, and stays a cancellation.
    /// </summary>
    public static bool IsTimeout(OperationCanceledException ex, CancellationToken callerToken) =>
        ex is TaskCanceledException && !callerToken.IsCancellationRequested;
}
