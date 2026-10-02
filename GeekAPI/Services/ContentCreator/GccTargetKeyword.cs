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
    /// <remarks>
    /// Delegates to <see cref="GccTopic.Parse"/>, which is the one parser. This was a second
    /// implementation until 2026-10-02 and the two disagreed — it split on the <b>last</b> colon while
    /// <c>GccTopic.Parse</c> split on the <b>first</b>, and only this one carried the length and
    /// token-count guards. Two readers of one string is the drift this codebase has been bitten by
    /// repeatedly; it stopped being survivable once the descriptor became a published URL directory,
    /// where disagreeing means filing a page at one path and ranking it for another.
    ///
    /// Kept as a named method rather than replaced at its call sites: "the keyword, for scoring" is a
    /// question worth having a name, and both call sites read better for asking it.
    /// </remarks>
    public static string FromTopic(string? topic) => GccTopic.KeywordOf(topic);
}
