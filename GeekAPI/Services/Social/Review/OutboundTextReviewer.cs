using System.Text.RegularExpressions;
using GeekApplication.Interfaces.Social;
using GeekApplication.Models.Social;
using GeekApplication.Results;

namespace GeekAPI.Services.Social.Review;

/// <summary>
/// Checks any outbound text (a draft, an edit a person made, post copy) against the voice and safety
/// rules in <see cref="ReviewRules"/> and answers which it breaks. It never rewrites and it calls
/// nothing: no model, no platform, no store. A refusal is a successful review with
/// <see cref="OutboundTextReview.Passed"/> false; a failed Result means it could not run, and text it
/// could not check is never passed.
/// </summary>
/// <remarks>
/// <para>
/// What each rule looks for, so a pass is not read as more than it is:
/// the em dash is U+2014 only; "SMB" is the word, with or without a plural; an address is a street
/// address of any kind (a number, up to four words, a street word such as Street, Ave or Blvd) or a
/// PO box, because the code does not hold Jeff's own address and a pattern is the only thing it can
/// check; a phone number is a North American number or a "+" number of ten or more digits; an email
/// address is any local@domain.tld; a link is an https or http address, a www address, or a bare
/// host on a common top-level domain, and it passes only when it equals an approved link (the scheme,
/// a leading www and a trailing slash do not count).
/// </para>
/// <para>
/// Each pattern refuses on a match and does not try to be clever about a near miss, so it can refuse
/// text a person would call fine. That is the safe direction: a refused draft is stored rejected with
/// the rule's name and an operator sees it.
/// </para>
/// </remarks>
public sealed partial class OutboundTextReviewer : IOutboundTextReviewer
{
    private const int MatchTimeoutMilliseconds = 2000;
    private const char EmDash = '—';

    // A word standing alone, so "SMBs" and "SMB's" match and "smbclient" does not.
    [GeneratedRegex(@"\bSMBs?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeoutMilliseconds)]
    private static partial Regex SmbWord();

    [GeneratedRegex(
        @"\b\d{1,6}\s+(?:[NSEW]\.?\s+|(?:North|South|East|West)\s+)?(?:[A-Za-z0-9'.-]+\s+){1,4}"
        + @"(?:Street|St|Avenue|Ave|Road|Rd|Boulevard|Blvd|Drive|Dr|Lane|Ln|Court|Ct|Way|Place|Pl|Parkway|Pkwy|Highway|Hwy|Circle|Cir|Terrace|Ter|Trail|Trl)\b"
        + @"|\bP\.?\s?O\.?\s+Box\s+\d+\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        MatchTimeoutMilliseconds)]
    private static partial Regex StreetAddress();

    [GeneratedRegex(
        @"(?<![\w.])(?:\+?1[\s.-]?)?(?:\(\d{3}\)\s?|\d{3}[\s.-]?)\d{3}[\s.-]?\d{4}(?!\w)"
        + @"|\+\d[\d\s().-]{8,}\d",
        RegexOptions.CultureInvariant,
        MatchTimeoutMilliseconds)]
    private static partial Regex PhoneNumber();

    [GeneratedRegex(
        @"[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,}",
        RegexOptions.CultureInvariant,
        MatchTimeoutMilliseconds)]
    private static partial Regex EmailAddress();

    // A scheme or www address, or a bare host on a common top-level domain. A host that follows an @ is
    // the domain of an email address and is the email rule's, not a link.
    [GeneratedRegex(
        @"(?<![@\w])(?:https?://|www\.)[^\s<>""')\]]+"
        + @"|(?<![@\w.-])(?:[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\.)+(?:com|net|org|io|co|ai|app|dev|us|info|biz|me|tv|ly|gg|xyz|edu|gov)(?![a-z0-9-])(?:/[^\s<>""')\]]*)?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        MatchTimeoutMilliseconds)]
    private static partial Regex Link();

    public Result<OutboundTextReview> Review(OutboundText text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (!SocialPlatforms.IsKnown(text.Platform))
            return Result<OutboundTextReview>.Failure("The text names no known social platform.", SocialFailureCodes.InvalidRequest);

        if (!OutboundTextKinds.All.Contains(text.Kind))
            return Result<OutboundTextReview>.Failure($"Unknown outbound text kind '{text.Kind}'.", SocialFailureCodes.InvalidRequest);

        if (string.IsNullOrWhiteSpace(text.Text))
            return Result<OutboundTextReview>.Failure("There is no text to review.", SocialFailureCodes.InvalidRequest);

        var approved = new HashSet<string>(StringComparer.Ordinal);
        foreach (var link in text.ApprovedLinks)
        {
            if (!TryNormalize(link, out var normalized))
                return Result<OutboundTextReview>.Failure("An approved link is not an address the Reviewer can compare.", SocialFailureCodes.InvalidRequest);

            approved.Add(normalized);
        }

        try
        {
            var violations = new List<string>();

            if (text.Text.Contains(EmDash))
                violations.Add(ReviewRules.EmDash);

            if (SmbWord().IsMatch(text.Text))
                violations.Add(ReviewRules.Smb);

            if (StreetAddress().IsMatch(text.Text))
                violations.Add(ReviewRules.HomeAddress);

            if (PhoneNumber().IsMatch(text.Text))
                violations.Add(ReviewRules.PhoneNumber);

            if (EmailAddress().IsMatch(text.Text))
                violations.Add(ReviewRules.EmailAddress);

            if (HasUnapprovedLink(text.Text, approved))
                violations.Add(ReviewRules.UnapprovedLink);

            if (text.Text.Length > ReviewLimits.For(text.Kind))
                violations.Add(ReviewRules.TooLong);

            return Result<OutboundTextReview>.Success(new OutboundTextReview
            {
                Passed = violations.Count == 0,
                Violations = violations,
            });
        }
        catch (RegexMatchTimeoutException)
        {
            return Result<OutboundTextReview>.Failure("The text could not be checked in time, so it is not passed.", SocialFailureCodes.InvalidRequest);
        }
    }

    private static bool HasUnapprovedLink(string text, HashSet<string> approved)
    {
        foreach (Match match in Link().Matches(text))
        {
            var candidate = match.Value.TrimEnd('.', ',', ';', ':', '!', '?');
            if (!TryNormalize(candidate, out var normalized) || !approved.Contains(normalized))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The address with its scheme, a leading www and a trailing slash taken off and its host lowercased,
    /// so two spellings of one address compare equal. False when it is not an address at all.
    /// </summary>
    private static bool TryNormalize(string? link, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(link))
            return false;

        var withScheme = link.Contains("://", StringComparison.Ordinal) ? link.Trim() : "https://" + link.Trim();
        if (!Uri.TryCreate(withScheme, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
            return false;

        var host = uri.Host.StartsWith("www.", StringComparison.Ordinal) ? uri.Host[4..] : uri.Host;
        normalized = host + uri.PathAndQuery.TrimEnd('/') + uri.Fragment;
        return true;
    }
}
