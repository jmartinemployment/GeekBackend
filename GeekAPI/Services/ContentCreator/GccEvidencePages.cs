using System.Text.RegularExpressions;
using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// Which pages a search returned are not evidence, and which may not give a quotation.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two questions, one place.</b> "Is this page evidence at all" and "may a block quotation be cut
/// from it" have different answers, and until 2026-10-10 only the second was asked: the quotation
/// list skipped a partner's legal and policy pages while every writer was still handed them. On the
/// 12:14 run of that day nine of the pages BILL's crawl returned were legal documents, a tool FAQ
/// question was shown its terms of service, and two more were shown a page-builder's template stub.
/// </para>
/// <para>
/// <b>Not evidence:</b> a legal document (terms, privacy, a data-processing agreement, licences), and
/// a page that still carries a page-builder's default text, which is a template nobody finished.
/// Neither is what a partner says its product does.
/// </para>
/// <para>
/// <b>Evidence, but never quoted:</b> a security, compliance, careers or accessibility page. A
/// question about a certification is answered from exactly such a page, so it stays evidence; a
/// block quotation is how a tool solves the reader's problem, and is not cut from one.
/// </para>
/// <para>
/// This filters what a search returned. It does not ask the search for more, so a place a legal page
/// took is not refilled. Sanitising input, not producing.
/// </para>
/// </remarks>
public static partial class GccEvidencePages
{
    /// <summary>
    /// Text a page builder puts in a block until someone replaces it. Exact strings, matched whole,
    /// so a page that merely discusses placeholder text is not caught.
    /// </summary>
    private static readonly string[] PlaceholderText =
    [
        "This is some text inside of a div block.",
        "Lorem ipsum dolor sit amet",
    ];

    /// <summary>Why this page is not evidence, in words for the run's record; null when it is.</summary>
    public static string? WhyNotEvidence(GccQuoteablePage page)
    {
        if (IsLegalDocument(page.Url)) return "a legal document";
        return CarriesPlaceholderText(page.Paragraphs) ? "a template page still carrying placeholder text" : null;
    }

    /// <summary>A legal document, by its address.</summary>
    public static bool IsLegalDocument(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var parsed) && LegalPath().IsMatch(parsed.AbsolutePath);

    /// <summary>A page no block quotation is cut from: a legal document, or a policy or company page.</summary>
    public static bool IsNotQuotable(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var parsed)
        && (LegalPath().IsMatch(parsed.AbsolutePath) || PolicyOrCompanyPath().IsMatch(parsed.AbsolutePath));

    /// <summary>True when any of the passages carries a page builder's default text.</summary>
    public static bool CarriesPlaceholderText(IReadOnlyList<string> passages) =>
        passages.Any(passage => PlaceholderText.Any(text => passage.Contains(text, StringComparison.OrdinalIgnoreCase)));

    /// <remarks>
    /// <para>
    /// Two shapes, and the split is deliberate. A bare word has to be the whole path segment, alone
    /// or with one of the endings legal pages are actually named with ("/privacy-policy",
    /// "/terms-of-service", "/cookie-notice"). "/legal-automation-software/" is a real product page
    /// for a legal-tech partner, and leaving it out would discard that partner's best evidence.
    /// Compound tokens are specific enough to match anywhere in a segment, which is what
    /// "/california-notice-at-collection/" needs: it was missed entirely while every token was
    /// anchored.
    /// </para>
    /// <para>
    /// Until 2026-10-10 the bare words were followed by "a slash, the end, a hyphen or a full stop",
    /// which caught "/legal-automation-software/" while the comment above it said it must not. No
    /// test held it. It only decided what could be quoted then; it decides what is evidence now.
    /// </para>
    /// </remarks>
    [GeneratedRegex(
        @"(/(privacy(-(policy|notice|statement|center|centre))?"
        + @"|legal(-(notices?|terms|information|disclaimer))?"
        + @"|terms(-(of-(service|use|sale)|and-conditions|conditions))?"
        + @"|cookies?(-(policy|notice|settings))?"
        + @"|gdpr|dpa|licen[cs]es?|msa|sla)(/|$|\.)"
        + @"|notice-at-collection|data-process(or|ing)|sub-?processor)",
        RegexOptions.IgnoreCase)]
    private static partial Regex LegalPath();

    /// <remarks>
    /// The start of a path segment, as before: "/security-overview" and "/careers/" are both caught.
    /// Looser than <see cref="LegalPath"/> on purpose. This one only decides what may be quoted.
    /// </remarks>
    [GeneratedRegex(@"/(security|compliance|careers|accessibility)(/|$|[-.])", RegexOptions.IgnoreCase)]
    private static partial Regex PolicyOrCompanyPath();
}
