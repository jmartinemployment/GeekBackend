namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// The keyword inside a create's topic.
///
/// <para>
/// A topic is written as context plus keyword, separated by a colon --
/// "Accounts Payable: Automated Data Entry &amp; Processing" -- because the model writes better
/// prose when it knows the subject area, while the keyword being ranked for is the second half.
/// Jeff, 2026-09-28: <i>"I am using two keywords in one to insure the model knows what I am talking
/// about, but the keyword is technically the second half."</i>
/// </para>
///
/// <para>
/// Everything that scores or matches on "the keyword" was using the whole string. The SEO report
/// then asked whether the lede contained "Accounts Payable: Automated Data Entry &amp; Processing"
/// verbatim, which no readable sentence ever will, so keyword-in-lede and keyword-in-heading failed
/// on drafts that used the keyword correctly throughout.
/// </para>
///
/// <para>
/// The context half is not discarded -- prompts still receive the full topic, which is the point of
/// writing it that way. This is only for the places that need the keyword itself.
/// </para>
/// </summary>
public static class GccTargetKeyword
{
    /// <summary>
    /// The keyword half, or the whole topic when there is no separator. A trailing half that is
    /// empty or a single short word is ignored: "Marketing: AI" is more likely a topic ending in a
    /// colon than a one-word keyword, and scoring against "AI" would pass on any draft.
    /// </summary>
    public static string FromTopic(string? topic)
    {
        var whole = (topic ?? string.Empty).Trim();
        if (whole.Length == 0) return whole;

        var idx = whole.LastIndexOf(':');
        if (idx < 0 || idx == whole.Length - 1) return whole;

        var tail = whole[(idx + 1)..].Trim();
        if (tail.Length < 3) return whole;

        // Two tokens is the floor everywhere else in this codebase for calling something a keyword
        // rather than a category -- see GccSiteStructureMatch, which refuses a single-token seed for
        // the same reason.
        var tokens = tail.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length >= 2 ? tail : whole;
    }
}
