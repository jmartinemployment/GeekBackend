namespace GeekAPI.Services.Rag;

public sealed class RagGenerateRequest
{
    public string WritingIntent { get; set; } = "";
    public string Topic { get; set; } = "";
    public List<string>? TargetEntities { get; set; }

    /// <summary>Phase D2 — operator-selected ad template exemplars (owned by content-creator-v2).</summary>
    public List<RagAdTemplateDto>? AdTemplates { get; set; }

    /// <summary>Optional template ids when Rag ad-template index is available.</summary>
    public List<string>? TemplateIds { get; set; }
}

public sealed class RagAdTemplateDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Channel { get; set; }
    public string? Framework { get; set; }
    public string Body { get; set; } = "";
}

public sealed class RagGenerateSourceDto
{
    public string Url { get; init; } = "";
    public string? Title { get; init; }
    public string? Entity { get; init; }
    public string? CrawlType { get; init; }
    /// <summary>page | theme | template — Phase D source kinds.</summary>
    public string? Kind { get; init; }
    public string? PageId { get; init; }
}

public sealed class RagCitationDto
{
    public string? PageId { get; init; }
    public string Url { get; init; } = "";
    public string? Title { get; init; }
    public string? SectionTitle { get; init; }
    public string Quote { get; init; } = "";
    public string? CrawlType { get; init; }
}

public sealed class RagThemeSourceDto
{
    public string Label { get; init; } = "";
    public string? Relationship { get; init; }
    public string? Entity { get; init; }
    public string? Url { get; init; }
}

public sealed class RagBattlecardDto
{
    public string PartnerSummary { get; set; } = "";
    public string CompetitorSummary { get; set; } = "";
    public List<string> Differentiators { get; set; } = [];
    public List<string> Risks { get; set; } = [];
}

public sealed class RagGenerateResponse
{
    public required string Intent { get; init; }
    public string? Content { get; init; }
    public IReadOnlyList<string>? Variations { get; init; }
    public RagBattlecardDto? Battlecard { get; init; }
    public IReadOnlyList<RagGenerateSourceDto> Sources { get; init; } = [];
    public IReadOnlyList<RagCitationDto>? Citations { get; init; }
    public IReadOnlyList<RagThemeSourceDto>? ThemeSources { get; init; }
    public IReadOnlyList<RagAdTemplateDto>? AppliedTemplates { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public bool SoftDisabled { get; init; }
    public string? ModelUsed { get; init; }
    public string? RetrievalMode { get; init; }
}

public sealed class RagGenerateStatusDto
{
    public bool Available { get; init; }
    public bool RagClientEnabled { get; init; }
    public bool GenerateEnabled { get; init; }
    public string? Reason { get; init; }
    public IReadOnlyList<string> WritingIntents { get; init; } = RagWritingIntents.All;
    public IReadOnlyList<string> EntitySeeds { get; init; } = RagEntitySeedList.Names;
    public string LongFormModel { get; init; } = RagModelRouter.DefaultLongFormModel;
    public string ShortFormModel { get; init; } = RagModelRouter.DefaultStandardModel;
    public bool GraphRetrievalAvailable { get; init; }
    public bool AdTemplateIndexAvailable { get; init; }
    public bool CiteableGenerateAvailable { get; init; }
}
