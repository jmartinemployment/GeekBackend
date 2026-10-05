using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.ContentCreator;

/// <summary>One declared partner's tool page: what it is called and where it is published.</summary>
/// <param name="Path">The page's path on the publisher's site, from <see cref="GccContentPath"/>.</param>
public sealed record GccPartnerToolPage(string Host, string ProductName, string Slug, string Path);

/// <summary>
/// The tool pages a project has: one per declared partner URL, and no others.
/// </summary>
/// <remarks>
/// <para>
/// This is the list a pillar or blog may name and link (Jeff, 2026-10-05, on a pillar that linked
/// Melio and Plooto: "links or anchor tags to Partners not listed"). Those two came from a different
/// list -- every tool the publisher's own site links under the keyword -- handed to the writer as
/// "name these and link each one". A site lists more tools than a project has partners.
/// </para>
/// <para>
/// The name and slug are the ones the tool page itself is generated with
/// (<see cref="GccPartnerToolSlices"/> names it from the same lookup, and
/// <c>GenerateToolPageAsync</c> slugs it with the same function), and the path is from the one path
/// builder. The link a pillar was told to set was assembled by hand as
/// <c>/tools/{department}/{slug}</c> with the create's default department, so it read
/// <c>/tools/marketing/ramp</c> while the page was published under
/// <c>/tools/accounting/accounts-payable/ramp</c>: a link to nowhere.
/// </para>
/// </remarks>
public static class GccPartnerToolPages
{
    public static IReadOnlyList<GccPartnerToolPage> For(
        GccCreateDto create,
        IReadOnlyList<string> partnerUrls,
        string toolBaseUrl)
    {
        if (partnerUrls.Count == 0) return [];

        var names = GccRequiredToolMentions.AnchorLookup(create.BriefJson, partnerUrls);
        var pages = new List<GccPartnerToolPage>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var url in partnerUrls)
        {
            var host = GccRequiredToolMentions.HostKeyOf(url);
            if (host.Length == 0 || !seen.Add(host)) continue;
            if (!names.TryGetValue(host, out var productName) || string.IsNullOrWhiteSpace(productName)) continue;

            var slug = GccGenerateService.ToolSlug(productName);
            pages.Add(new GccPartnerToolPage(
                host, productName.Trim(), slug, GccContentPath.PathFor(toolBaseUrl, create, slug)));
        }

        return pages;
    }
}
