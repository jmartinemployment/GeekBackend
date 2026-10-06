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
                "the response is incomplete.")
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

    /// <summary>
    /// A call the HTTP client abandoned because the provider did not answer in time. The caller's own
    /// cancellation is not this: that one is the run being stopped, and stays a cancellation.
    /// </summary>
    public static bool IsTimeout(OperationCanceledException ex, CancellationToken callerToken) =>
        ex is TaskCanceledException && !callerToken.IsCancellationRequested;
}
