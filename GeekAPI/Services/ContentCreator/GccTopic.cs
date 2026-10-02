namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// A create's <c>Topic</c>, read as the two fields it actually is.
/// </summary>
/// <remarks>
/// <para>
/// <c>"Accounts Payable: Automated Data Entry &amp; Processing"</c> is a <b>descriptor</b> and a
/// <b>keyword</b>, split on the first colon. The descriptor defines the *type* of the keyword — the
/// keyword alone could be about medical records or legal discovery — and the keyword is the SEO target,
/// which names the <b>solution</b>. On a <c>problem_solution</c> angle the problem is the keyword's
/// manual form, and the partner's product is the agent of the solution. Jeff, 2026-10-01.
/// </para>
/// <para>
/// Nothing parsed this before, and that was the gap rather than the design. Passing the whole string
/// where a keyword belonged produced two live defects in one day: extraction hunting for a product named
/// after the keyword, and a retrieval query reading "the problem of doing Accounts Payable: Automated
/// Data Entry &amp; Processing manually" — automated, manually.
/// </para>
/// </remarks>
public static class GccTopic
{
    /// <param name="Descriptor">
    /// What type of thing the keyword is. Empty when the topic carries no colon — absence, not a guess.
    /// </param>
    /// <param name="Keyword">The SEO target. The whole topic when there is no descriptor.</param>
    public readonly record struct Parts(string Descriptor, string Keyword)
    {
        /// <summary>The keyword with its descriptor restored, for anything that wants the full subject.</summary>
        public string Qualified =>
            Descriptor.Length == 0 ? Keyword : $"{Descriptor}: {Keyword}";
    }

    /// <summary>
    /// Splits on the <b>first</b> colon only. A topic with no colon is all keyword.
    /// </summary>
    public static Parts Parse(string? topic)
    {
        var trimmed = (topic ?? string.Empty).Trim();
        if (trimmed.Length == 0) return new Parts(string.Empty, string.Empty);

        var colon = trimmed.IndexOf(':');
        if (colon < 0) return new Parts(string.Empty, trimmed);

        var descriptor = trimmed[..colon].Trim();
        var keyword = trimmed[(colon + 1)..].Trim();

        // A colon with nothing usable on one side is not a split. "Pricing:" is a keyword that happens
        // to end in a colon, and ": Automated Data Entry" has no descriptor to take.
        if (descriptor.Length == 0 || keyword.Length == 0)
        {
            return new Parts(string.Empty, trimmed);
        }

        return new Parts(descriptor, keyword);
    }

    /// <summary>The SEO target alone — what belongs in a problem frame.</summary>
    public static string KeywordOf(string? topic) => Parse(topic).Keyword;
}
