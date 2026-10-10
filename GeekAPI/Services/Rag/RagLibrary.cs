using GeekAPI.Services.GeekCrawler;
using GeekApplication.Models.GeekCrawler;

namespace GeekAPI.Services.Rag;

/// <summary>
/// What the RAG library can do right now, and the one write it accepts: indexing ad templates. RAG is
/// retrieval and verification only; it never generates, and <see cref="RagLibraryStatusDto.GenerateEnabled"/>
/// is always false.
/// </summary>
public sealed class RagLibrary
{
    private readonly IGeekCrawlerRagClient _rag;
    private readonly bool _graphEnabled;
    private readonly bool _adTemplateIndexEnabled;

    public RagLibrary(IGeekCrawlerRagClient rag)
    {
        _rag = rag;
        _graphEnabled = ParseEnabledFlag(Environment.GetEnvironmentVariable("GEEK_RAG_GRAPH_ENABLED"));
        _adTemplateIndexEnabled = ParseEnabledFlag(Environment.GetEnvironmentVariable("GEEK_RAG_AD_TEMPLATES_ENABLED"));
    }

    public RagLibraryStatusDto GetStatus()
    {
        var ragOn = _rag.IsEnabled;
        return new RagLibraryStatusDto
        {
            Available = ragOn,
            RagClientEnabled = ragOn,
            GenerateEnabled = false,
            Reason = ragOn ? null : "Geek-Crawler-Rag client disabled (GEEK_CRAWLER_RAG_URL unset).",
            WritingIntents = RagWritingIntents.All,
            EntitySeeds = RagEntitySeedList.Names,
            LongFormModel = RagModelRouter.ResolveModel(RagRetrievalFamily.LongForm),
            ShortFormModel = RagModelRouter.ResolveModel(RagRetrievalFamily.ShortForm),
            GraphRetrievalAvailable = ragOn && _graphEnabled,
            AdTemplateIndexAvailable = ragOn && _adTemplateIndexEnabled,
            CiteableGenerateAvailable = false,
        };
    }

    public async Task<GeekCrawlerRagTemplateIndexResult> IndexAdTemplatesAsync(
        IReadOnlyList<RagAdTemplateDto> templates,
        CancellationToken ct)
    {
        if (!_rag.IsEnabled || !_adTemplateIndexEnabled)
        {
            throw new InvalidOperationException(
                "Ad template index requires RAG and GEEK_RAG_AD_TEMPLATES_ENABLED. "
                + "Soft-disabled index success is forbidden.");
        }

        var mapped = templates
            .Select(t => new GeekCrawlerRagTemplateDto
            {
                Id = t.Id,
                Name = t.Name,
                Channel = t.Channel,
                Framework = t.Framework,
                Body = t.Body,
            })
            .ToList();
        return await _rag.IndexTemplatesAsync(mapped, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "Ad template index returned no result from the research library.");
    }

    private static bool ParseEnabledFlag(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return true;
        return raw.Trim() switch
        {
            "0" or "false" or "False" or "FALSE" or "no" or "off" => false,
            _ => true,
        };
    }
}

public sealed class RagAdTemplateDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Channel { get; set; }
    public string? Framework { get; set; }
    public string Body { get; set; } = "";
}

/// <summary>What <c>GET api/rag/status</c> answers.</summary>
public sealed class RagLibraryStatusDto
{
    public bool Available { get; init; }
    public bool RagClientEnabled { get; init; }
    /// <summary>Always false: RAG generate is removed.</summary>
    public bool GenerateEnabled { get; init; }
    public string? Reason { get; init; }
    public IReadOnlyList<string> WritingIntents { get; init; } = RagWritingIntents.All;
    public IReadOnlyList<string> EntitySeeds { get; init; } = RagEntitySeedList.Names;
    public string LongFormModel { get; init; } = RagModelRouter.DefaultLongFormModel;
    public string ShortFormModel { get; init; } = RagModelRouter.DefaultStandardModel;
    public bool GraphRetrievalAvailable { get; init; }
    public bool AdTemplateIndexAvailable { get; init; }
    /// <summary>Always false, for the same reason.</summary>
    public bool CiteableGenerateAvailable { get; init; }
}
