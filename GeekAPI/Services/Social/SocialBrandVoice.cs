using GeekApplication.Models.Social;

namespace GeekAPI.Services.Social;

/// <summary>
/// The voice every outbound GeekSocial text is written in, told to the drafter (and later the post
/// writer) in its system prompt. It is seeded from Jeff's copy rules. The rules a pattern can check are
/// also enforced in code by the Reviewer (<see cref="ReviewRules"/>), which refuses text that breaks
/// them; the prompt asks for them so a draft rarely needs refusing, and the code is what holds the line.
/// </summary>
public static class SocialBrandVoice
{
    /// <summary>The voice and the hard rules, with the length limit for <paramref name="kind"/> (an <see cref="OutboundTextKinds"/> value).</summary>
    public static string Rules(string kind, IReadOnlyCollection<string> approvedLinks)
    {
        var limit = ReviewLimits.For(kind);
        var links = approvedLinks.Count == 0
            ? "Do not include any link or web address."
            : "Do not include any link or web address except these, written exactly as given: " + string.Join(", ", approvedLinks) + ".";

        return $"""
            Voice:
            - Write as the business speaking on its own page: helpful, direct and plain-spoken.
            - Use full sentences.
            - Say "small businesses", never "SMB" or "SMBs".
            - Do not pitch AI for HR or hiring.

            Hard rules, checked by code after you write; text that breaks one is refused, not fixed:
            - Never use an em dash. Use a comma, a period or parentheses instead.
            - Never write a street address, a PO box, a phone number or an email address.
            - {links}
            - At most {limit} characters.
            """;
    }
}
