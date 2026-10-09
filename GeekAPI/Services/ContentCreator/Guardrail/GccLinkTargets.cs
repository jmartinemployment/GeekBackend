using GeekAPI.Services.Workflow.DTOs;
using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.ContentCreator.Guardrail;

/// <summary>One place a paragraph may link: the id its prompt printed, where it leads, and its name.</summary>
public sealed record GccLinkTarget(string Id, string Href, string Name);

/// <summary>
/// Everything the writer may link, by the id its prompt printed. <c>S#</c> is a page of QUOTEABLE
/// RESEARCH, numbered in the order <c>GccGenerateService.BuildResearchBlock</c> prints them; <c>T#</c>
/// is a partner tool page, numbered in the order the KNOWN TOOLS block prints
/// <c>ProjectGenerationContext.KnownCrawlTools</c>. The renderers and this class read the same lists
/// in the same order, so an id means one page on both sides and nowhere does a URL change hands.
/// </summary>
/// <remarks>
/// A page the renderer prints but that has no address -- a quoteable with a blank URL, a tool with no
/// public path -- still consumes its number, so the ids the writer sees stay aligned with these. Such
/// an id is simply not a target: a link to it is refused by name rather than resolved to a guess.
/// </remarks>
public sealed class GccLinkTargets
{
    public static readonly GccLinkTargets None = new([]);

    private readonly Dictionary<string, GccLinkTarget> _byId;

    private GccLinkTargets(IReadOnlyList<GccLinkTarget> all)
    {
        All = all;
        _byId = all.ToDictionary(target => target.Id, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Every target, in the order the prompt prints them.</summary>
    public IReadOnlyList<GccLinkTarget> All { get; }

    /// <summary>The id printed before the <paramref name="index"/>th quoteable page (zero-based).</summary>
    public static string EvidenceId(int index) => $"S{index + 1}";

    /// <summary>The id printed before the <paramref name="index"/>th known tool (zero-based).</summary>
    public static string ToolId(int index) => $"T{index + 1}";

    /// <summary>
    /// The targets for one draft: the evidence pages the body prompt prints, and the partner tool pages
    /// its KNOWN TOOLS block prints. Pass <c>null</c> tools for a page that is handed none -- a tool page
    /// links no other tool page -- and a <c>T#</c> on it is refused as not a target.
    /// </summary>
    public static GccLinkTargets For(
        IReadOnlyList<GccQuoteablePage>? evidence,
        IReadOnlyList<KnownCrawlTool>? tools)
    {
        var all = new List<GccLinkTarget>();

        var pages = evidence ?? [];
        for (var i = 0; i < pages.Count; i++)
        {
            var url = pages[i].Url?.Trim();
            if (string.IsNullOrWhiteSpace(url)) continue;
            var title = string.IsNullOrWhiteSpace(pages[i].Title) ? url : pages[i].Title.Trim();
            all.Add(new GccLinkTarget(EvidenceId(i), url, title));
        }

        var list = tools ?? [];
        for (var i = 0; i < list.Count; i++)
        {
            var path = list[i].PublicPath?.Trim();
            if (string.IsNullOrWhiteSpace(path)) continue;
            all.Add(new GccLinkTarget(ToolId(i), path, list[i].Name));
        }

        return new GccLinkTargets(all);
    }

    /// <summary>The target an id names, if the prompt printed one for it. Case and surrounding space do not matter.</summary>
    public bool TryGet(string? id, out GccLinkTarget target)
    {
        if (!string.IsNullOrWhiteSpace(id) && _byId.TryGetValue(id.Trim(), out var found))
        {
            target = found;
            return true;
        }

        target = null!;
        return false;
    }
}
