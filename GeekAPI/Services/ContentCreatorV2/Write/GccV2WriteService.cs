using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Adapters;
using GeekAPI.Services.ContentCreatorV2.BrandKit;
using GeekAPI.Services.ContentCreatorV2.Jobs;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services;
using GeekAPI.Services.Workflow.Services.PromptBuilders;
using GeekAPI.Services.Workflow.Services.SchemaBuilders;

namespace GeekAPI.Services.ContentCreatorV2.Write;

/// <summary>One written section: its plan-stage identity (key/heading/job) plus the actual
/// generated <see cref="Section"/> body.</summary>
public sealed record GccV2WriteSection(string SectionKey, string Heading, string? Job, Section Section, bool UsedFallbackStub);

/// <summary>Everything WRITE produced for a job — enough for VALIDATE to build a
/// <see cref="ContentDocument"/>, run OverlapGate, and target REPAIR at one section.</summary>
public sealed class GccV2WriteOutput
{
    public required string Title { get; init; }
    public required string? MetaDescription { get; init; }
    public required GccV2WriteSection Lede { get; init; }
    public required IReadOnlyList<GccV2WriteSection> Sections { get; init; }
    public int TokensUsed { get; init; }

    public ContentDocument ToContentDocument() => new(Lede.Section, Sections.Select(s => s.Section).ToList());

    /// <summary>Lede + body sections, in document order — the full OverlapGate comparison set.</summary>
    public IReadOnlyList<GccV2WriteSection> AllSections => new[] { Lede }.Concat(Sections).ToList();

    public GccV2WriteOutput WithSection(GccV2WriteSection replacement)
    {
        if (replacement.SectionKey == Lede.SectionKey)
        {
            return new GccV2WriteOutput { Title = Title, MetaDescription = MetaDescription, Lede = replacement, Sections = Sections, TokensUsed = TokensUsed };
        }

        var sections = Sections.Select(s => s.SectionKey == replacement.SectionKey ? replacement : s).ToList();
        return new GccV2WriteOutput { Title = Title, MetaDescription = MetaDescription, Lede = Lede, Sections = sections, TokensUsed = TokensUsed };
    }
}

public sealed record GccV2OutlineSection(string Key, string Heading, string? Job, List<string> HierarchyChildHeadings);

public sealed record GccV2Outline(List<GccV2OutlineSection> Sections, List<string> HierarchyChildHeadings);

/// <summary>Everything needed to write (or repair) sections for one job — loaded once per stage
/// run so REPAIR doesn't re-fetch brief/brand-kit/outline for every flagged section.</summary>
public sealed record GccV2WriteContext(
    GccV2JobDto Job,
    GccV2BriefDto Brief,
    GccV2BrandKitContent? BrandKit,
    GccV2Outline Outline,
    ProjectGenerationContext BaseContext,
    IContentGenerationProvider Provider)
{
    /// <summary>
    /// Set by the worker before WRITE/VALIDATE run. Invoked after every section write/rewrite so a
    /// long pillar (many sequential LLM calls) never lets its claim lease expire mid-job — patches
    /// the job's lease via the existing <c>PatchJob</c> route, no new endpoint.
    /// </summary>
    public Func<CancellationToken, Task>? ExtendLease { get; init; }
}

/// <summary>
/// Phase 5–6 WRITE: section-by-section generation for long-form types via
/// <see cref="IContentPromptBuilder"/> (called, never edited/copied). Remaining content types
/// (tool, email, social, ads, image-prompt) use their canonical prompt builders in Phase 6.
/// </summary>
public sealed class GccV2WriteService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions ContentDocJson = CreateContentDocJson();

    private static JsonSerializerOptions CreateContentDocJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new ParagraphJsonConverter());
        return options;
    }

    private readonly HttpGccV2Repository _repo;
    private readonly GccV2JobEventWriter _events;
    private readonly GccV2ContextAdapter _contextAdapter;
    private readonly IContentPromptBuilder _prompts;
    private readonly IContentProviderFactory _providers;
    private readonly ILogger<GccV2WriteService> _logger;

    public GccV2WriteService(
        HttpGccV2Repository repo,
        GccV2JobEventWriter events,
        GccV2ContextAdapter contextAdapter,
        IContentPromptBuilder prompts,
        IContentProviderFactory providers,
        ILogger<GccV2WriteService> logger)
    {
        _repo = repo;
        _events = events;
        _contextAdapter = contextAdapter;
        _prompts = prompts;
        _providers = providers;
        _logger = logger;
    }

    /// <summary>Loads brief + brand kit + PLAN's outline and builds the base context — shared by
    /// the initial WRITE pass and every later REPAIR call so they never drift.</summary>
    public async Task<GccV2WriteContext> PrepareAsync(GccV2JobDto job, CancellationToken ct)
    {
        var brief = await _repo.GetBriefAsync(job.BriefId, ct)
            ?? throw new InvalidOperationException($"Brief {job.BriefId} not found for job {job.Id}.");

        if (job.SiteAnalysisProfileId is not { } profileId)
            throw new InvalidOperationException("WRITE requires a siteAnalysisProfileId — start from Site Analyzer.");

        var (brandKit, kitDto) = await LoadAcceptedBrandKitAsync(profileId, ct);
        var create = await _repo.GetCreateAsync(job.CreateId, ct)
            ?? throw new InvalidOperationException($"Create {job.CreateId} not found for job {job.Id}.");
        var siteSection = GeekAPI.Services.ContentCreator.GccGenerateService.ParseSiteSection(create.SiteSectionJson);
        if (siteSection is null || siteSection.RelatedPages is null || siteSection.RelatedPages.Count == 0)
            throw new InvalidOperationException("WRITE requires create.SiteSectionJson with non-empty relatedPages.");

        var outline = await LoadOutlineAsync(job.Id, ct);
        var provider = _providers.GetDefault();
        var baseContext = _contextAdapter.BuildContext(brief, brandKit, provider.ProviderType, siteSection);
        _ = kitDto;
        return new GccV2WriteContext(job, brief, brandKit, outline, baseContext, provider);
    }

    public Task<GccV2WriteOutput> WriteAsync(GccV2WriteContext wc, Guid ownerUserId, CancellationToken ct)
    {
        var contentType = string.IsNullOrWhiteSpace(wc.Job.ContentType) ? "blog" : wc.Job.ContentType.ToLowerInvariant();
        return contentType switch
        {
            "pillar" => WritePillarAsync(wc, ownerUserId, ct),
            "blog" => WriteBlogAsync(wc, ownerUserId, ct),
            "tool" => WriteToolAsync(wc, ownerUserId, ct),
            "email" => WriteEmailAsync(wc, ownerUserId, ct),
            "social" => WriteSocialAsync(wc, ownerUserId, ct),
            "ads" => WriteAdsAsync(wc, ownerUserId, ct),
            "image-prompt" => WriteImagePromptAsync(wc, ownerUserId, ct),
            _ => WriteStubAsync(wc, ownerUserId, contentType, ct),
        };
    }

    /// <summary>Persists a REPAIR-stage section and emits <c>SectionRepaired</c> — shared by editorial
    /// overlap/polish repair and guardrail pass-2 restructure.</summary>
    public Task PublishSectionRepairAsync(
        GccV2WriteContext wc, Guid ownerUserId, GccV2WriteSection write, int tokens, CancellationToken ct) =>
        PersistAndEmitAsync(wc, ownerUserId, "repair", "SectionRepaired", write, tokens, ct);

    /// <summary>Rewrites exactly one already-written section — used by VALIDATE's REPAIR loop and
    /// by the Canvas rewrite/expand/re-tone endpoints. Always <see cref="IContentPromptBuilder.BuildArticleSectionPrompt"/>
    /// with <c>isRegeneration:true</c>, regardless of whether the section was originally the lede —
    /// once written, a lede is just the document's first H2 for repair purposes.</summary>
    public async Task<GccV2WriteSection> RewriteSectionAsync(
        GccV2WriteContext wc,
        Guid ownerUserId,
        string title,
        GccV2WriteSection target,
        string revisionNotes,
        CancellationToken ct,
        string stage = "repair",
        string eventType = "SectionRepaired")
    {
        var headings = wc.Outline.Sections.Select(s => s.Heading).ToList();
        if (headings.Count == 0) headings = [target.Heading];

        var outlineEntry = wc.Outline.Sections.FirstOrDefault(s => s.Key == target.SectionKey);
        var index = Math.Max(0, wc.Outline.Sections.FindIndex(s => s.Key == target.SectionKey));
        var sectionContext = _contextAdapter.WithSectionAssignment(
            wc.BaseContext, target.Heading, target.Job, outlineEntry?.HierarchyChildHeadings);

        var metadata = new ArticleMetadataDraft(
            string.IsNullOrWhiteSpace(title) ? wc.BaseContext.TargetKeyword : title, "", [], headings);

        Section section;
        var tokens = 0;
        try
        {
            var result = await wc.Provider.CompleteAsync(
                _prompts.BuildArticleSectionPrompt(
                    sectionContext, metadata, target.Heading, index, Math.Max(headings.Count, 1), headings,
                    isRegeneration: true, revisionNotes: revisionNotes),
                ct);
            section = LlmResponseJsonParser.ParseSection(result.Content, "h2", $"repaired section \"{target.Heading}\"");
            tokens = (result.PromptTokens ?? 0) + (result.CompletionTokens ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rewrite of section \"{Heading}\" failed for job {JobId}.", target.Heading, wc.Job.Id);
            throw;
        }

        section = section with { Heading = target.Heading, Tag = "h2" };
        var write = new GccV2WriteSection(target.SectionKey, target.Heading, target.Job, section, false);
        await PersistAndEmitAsync(wc, ownerUserId, stage, eventType, write, tokens, ct);
        return write;
    }

    private async Task<GccV2WriteOutput> WritePillarAsync(GccV2WriteContext wc, Guid ownerUserId, CancellationToken ct)
    {
        var outlineSections = wc.Outline.Sections;
        var headings = outlineSections.Select(s => s.Heading).ToList();

        var metadata = await GeneratePillarMetadataAsync(wc, headings, ct);

        var ledeContext = _contextAdapter.WithSectionAssignment(wc.BaseContext, "Lede", "problem", null);
        Section ledeSection;
        var ledeTokens = 0;
        try
        {
            var ledeResult = await wc.Provider.CompleteAsync(
                _prompts.BuildPillarLedePrompt(
                    ledeContext, metadata, headings.Count > 0 ? headings[0] : "Introduction", 0,
                    Math.Max(headings.Count, 1), headings, isRegeneration: false),
                ct);
            var (lede, _, _) = LlmResponseJsonParser.ParseLedeAndIntroduction(ledeResult.Content, "pillar lede");
            ledeSection = lede;
            ledeTokens = (ledeResult.PromptTokens ?? 0) + (ledeResult.CompletionTokens ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pillar lede generation failed for job {JobId}.", wc.Job.Id);
            throw;
        }

        var ledeWrite = new GccV2WriteSection("lede", ledeSection.Heading, "problem", ledeSection, false);
        await PersistAndEmitAsync(wc, ownerUserId, "write", "SectionDrafted", ledeWrite, ledeTokens, ct);

        var sections = new List<GccV2WriteSection>();
        var tokensUsed = ledeTokens;
        for (var i = 0; i < outlineSections.Count; i++)
        {
            var entry = outlineSections[i];
            var sectionContext = _contextAdapter.WithSectionAssignment(wc.BaseContext, entry.Heading, entry.Job, entry.HierarchyChildHeadings);

            Section section;
            var tokens = 0;
            try
            {
                var result = await wc.Provider.CompleteAsync(
                    _prompts.BuildArticleSectionPrompt(sectionContext, metadata, entry.Heading, i, outlineSections.Count, headings, isRegeneration: false),
                    ct);
                section = LlmResponseJsonParser.ParseSection(result.Content, "h2", $"pillar section \"{entry.Heading}\"");
                tokens = (result.PromptTokens ?? 0) + (result.CompletionTokens ?? 0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pillar section \"{Heading}\" generation failed for job {JobId}.", entry.Heading, wc.Job.Id);
                throw;
            }

            section = section with { Heading = entry.Heading, Tag = "h2" };
            var write = new GccV2WriteSection(entry.Key, entry.Heading, entry.Job, section, false);
            await PersistAndEmitAsync(wc, ownerUserId, "write", "SectionDrafted", write, tokens, ct);
            sections.Add(write);
            tokensUsed += tokens;
        }

        return new GccV2WriteOutput
        {
            Title = metadata.Title,
            MetaDescription = metadata.MetaDescription,
            Lede = ledeWrite,
            Sections = sections,
            TokensUsed = tokensUsed,
        };
    }

    private async Task<GccV2WriteOutput> WriteBlogAsync(GccV2WriteContext wc, Guid ownerUserId, CancellationToken ct)
    {
        var outlineSections = wc.Outline.Sections;
        var headings = outlineSections.Select(s => s.Heading).ToList();

        var blogMeta = await GenerateBlogMetadataAsync(wc, headings, ct);
        var articleMeta = new ArticleMetadataDraft(blogMeta.Title, blogMeta.MetaDescription, blogMeta.Keywords, headings);

        var ledeContext = _contextAdapter.WithSectionAssignment(wc.BaseContext, "Lede", "problem", null);
        Section ledeSection;
        var ledeTokens = 0;
        try
        {
            var ledeResult = await wc.Provider.CompleteAsync(_prompts.BuildStandaloneBlogLedePrompt(ledeContext, blogMeta), ct);
            (ledeSection, _) = LlmResponseJsonParser.ParseLede(ledeResult.Content, "blog lede");
            ledeTokens = (ledeResult.PromptTokens ?? 0) + (ledeResult.CompletionTokens ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Blog lede generation failed for job {JobId}.", wc.Job.Id);
            throw;
        }

        var ledeWrite = new GccV2WriteSection("lede", ledeSection.Heading, "problem", ledeSection, false);
        await PersistAndEmitAsync(wc, ownerUserId, "write", "SectionDrafted", ledeWrite, ledeTokens, ct);

        var sections = new List<GccV2WriteSection>();
        var tokensUsed = ledeTokens;
        for (var i = 0; i < outlineSections.Count; i++)
        {
            var entry = outlineSections[i];
            var sectionContext = _contextAdapter.WithSectionAssignment(wc.BaseContext, entry.Heading, entry.Job, entry.HierarchyChildHeadings);

            Section section;
            var tokens = 0;
            try
            {
                var result = await wc.Provider.CompleteAsync(
                    _prompts.BuildArticleSectionPrompt(sectionContext, articleMeta, entry.Heading, i, outlineSections.Count, headings, isRegeneration: false),
                    ct);
                section = LlmResponseJsonParser.ParseSection(result.Content, "h2", $"blog section \"{entry.Heading}\"");
                tokens = (result.PromptTokens ?? 0) + (result.CompletionTokens ?? 0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Blog section \"{Heading}\" generation failed for job {JobId}.", entry.Heading, wc.Job.Id);
                throw;
            }

            section = section with { Heading = entry.Heading, Tag = "h2" };
            var write = new GccV2WriteSection(entry.Key, entry.Heading, entry.Job, section, false);
            await PersistAndEmitAsync(wc, ownerUserId, "write", "SectionDrafted", write, tokens, ct);
            sections.Add(write);
            tokensUsed += tokens;
        }

        return new GccV2WriteOutput
        {
            Title = blogMeta.Title,
            MetaDescription = blogMeta.MetaDescription,
            Lede = ledeWrite,
            Sections = sections,
            TokensUsed = tokensUsed,
        };
    }

    private async Task<GccV2WriteOutput> WriteToolAsync(GccV2WriteContext wc, Guid ownerUserId, CancellationToken ct)
    {
        var keyword = wc.BaseContext.TargetKeyword;
        var toolName = Capitalize(keyword);
        var metadata = await GeneratePillarMetadataAsync(wc, [toolName, "Overview", "Key Capabilities", "Implementation Considerations", "When to Use"], ct);
        var app = new SoftwareApplicationDescriptor(toolName, $"Overview of {toolName}.", wc.BaseContext.ToolBaseUrl);
        var slug = Slugify(keyword);

        IReadOnlyList<Section> parsedSections;
        var tokens = 0;
        try
        {
            var result = await wc.Provider.CompleteAsync(
                _prompts.BuildToolBodyPrompt(wc.BaseContext, metadata, app, slug), ct);
            parsedSections = LlmResponseJsonParser.ParseSections(result.Content, "tool body");
            tokens = (result.PromptTokens ?? 0) + (result.CompletionTokens ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool body generation failed for job {JobId}.", wc.Job.Id);
            throw;
        }

        if (parsedSections.Count == 0)
            throw new InvalidOperationException("Tool body generation returned no sections.");

        var ledeSection = parsedSections[0] with { Tag = "h2" };
        var ledeWrite = new GccV2WriteSection("lede", ledeSection.Heading, "problem", ledeSection, false);
        await PersistAndEmitAsync(wc, ownerUserId, "write", "SectionDrafted", ledeWrite, tokens, ct);

        var sections = new List<GccV2WriteSection>();
        for (var i = 1; i < parsedSections.Count; i++)
        {
            var section = parsedSections[i] with { Tag = "h2" };
            var key = Slugify(section.Heading);
            if (string.IsNullOrWhiteSpace(key)) key = $"section-{i}";
            var write = new GccV2WriteSection(key, section.Heading, "advance", section, false);
            await PersistAndEmitAsync(wc, ownerUserId, "write", "SectionDrafted", write, 0, ct);
            sections.Add(write);
        }

        return new GccV2WriteOutput
        {
            Title = metadata.Title,
            MetaDescription = metadata.MetaDescription,
            Lede = ledeWrite,
            Sections = sections,
            TokensUsed = tokens,
        };
    }

    private async Task<GccV2WriteOutput> WriteEmailAsync(GccV2WriteContext wc, Guid ownerUserId, CancellationToken ct)
    {
        var source = BuildSyntheticArticleDraft(wc);
        var articleUrl = wc.BaseContext.ArticleBaseUrl ?? "https://example.com/article";
        ColdOutreachEmailDraft draft;
        var tokens = 0;
        try
        {
            var result = await wc.Provider.CompleteAsync(
                _prompts.BuildColdOutreachPrompt(wc.BaseContext, source, articleUrl), ct);
            draft = LlmResponseJsonParser.ParseColdOutreach(result.Content, "cold outreach email");
            tokens = (result.PromptTokens ?? 0) + (result.CompletionTokens ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Email generation failed for job {JobId}.", wc.Job.Id);
            throw;
        }

        var ledeSection = new Section(
            "h2",
            draft.Subject,
            [new TextParagraph([new Run(draft.BodyText)]), new TextParagraph([new Run($"CTA: {draft.CtaLabel}")])],
            null,
            []);
        var ledeWrite = new GccV2WriteSection("email-body", draft.Subject, "problem", ledeSection, false);
        await PersistAndEmitAsync(wc, ownerUserId, "write", "SectionDrafted", ledeWrite, tokens, ct);

        return new GccV2WriteOutput
        {
            Title = draft.Subject,
            MetaDescription = Truncate(draft.BodyText, 160),
            Lede = ledeWrite,
            Sections = [],
            TokensUsed = tokens,
        };
    }

    private async Task<GccV2WriteOutput> WriteSocialAsync(GccV2WriteContext wc, Guid ownerUserId, CancellationToken ct)
    {
        var platform = ParseSocialPlatform(wc.Brief.RawBriefJson) ?? "LinkedIn";
        var source = BuildSyntheticArticleDraft(wc);
        var articleUrl = wc.BaseContext.ArticleBaseUrl ?? "https://example.com/article";
        string text;
        var tokens = 0;
        try
        {
            var result = await wc.Provider.CompleteAsync(
                _prompts.BuildSocialPrompt(wc.BaseContext, source, platform, articleUrl), ct);
            text = LlmResponseJsonParser.ParseSocialText(result.Content, articleUrl, $"{platform} post");
            tokens = (result.PromptTokens ?? 0) + (result.CompletionTokens ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Social generation failed for job {JobId}.", wc.Job.Id);
            throw;
        }

        var ledeSection = new Section(
            "h2",
            $"{platform} post",
            [new TextParagraph([new Run(text)])],
            null,
            []);
        var ledeWrite = new GccV2WriteSection("social-post", ledeSection.Heading, "problem", ledeSection, false);
        await PersistAndEmitAsync(wc, ownerUserId, "write", "SectionDrafted", ledeWrite, tokens, ct);

        return new GccV2WriteOutput
        {
            Title = source.Title,
            MetaDescription = source.MetaDescription,
            Lede = ledeWrite,
            Sections = [],
            TokensUsed = tokens,
        };
    }

    private async Task<GccV2WriteOutput> WriteAdsAsync(GccV2WriteContext wc, Guid ownerUserId, CancellationToken ct)
    {
        var source = BuildSyntheticArticleDraft(wc);
        var articleUrl = wc.BaseContext.ArticleBaseUrl ?? "https://example.com/article";
        AdvertisingDraft draft;
        var tokens = 0;
        try
        {
            var result = await wc.Provider.CompleteAsync(
                _prompts.BuildAdvertisingPrompt(wc.BaseContext, source, articleUrl), ct);
            draft = ParseAdvertisingDraft(result.Content);
            tokens = (result.PromptTokens ?? 0) + (result.CompletionTokens ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ads generation failed for job {JobId}.", wc.Job.Id);
            throw;
        }

        var ledeSection = new Section(
            "h2",
            draft.Title,
            [new TextParagraph([new Run(draft.BodyText)])],
            null,
            []);
        var ledeWrite = new GccV2WriteSection("ads-body", draft.Title, "problem", ledeSection, false);
        await PersistAndEmitAsync(wc, ownerUserId, "write", "SectionDrafted", ledeWrite, tokens, ct);

        return new GccV2WriteOutput
        {
            Title = draft.Title,
            MetaDescription = draft.MetaDescription,
            Lede = ledeWrite,
            Sections = [],
            TokensUsed = tokens,
        };
    }

    /// <summary>Image prompts are write-only — VALIDATE is skipped by the worker for this type.</summary>
    private async Task<GccV2WriteOutput> WriteImagePromptAsync(GccV2WriteContext wc, Guid ownerUserId, CancellationToken ct)
    {
        var topic = Capitalize(wc.BaseContext.TargetKeyword);
        var notes = wc.BaseContext.WritingNotes;
        ImagePromptDraft draft;
        var tokens = 0;
        try
        {
            var result = await wc.Provider.CompleteAsync(
                _prompts.BuildStandaloneImagePrompt(topic, notes, artifactContext: null), ct);
            draft = ParseImagePromptDraft(result.Content);
            tokens = (result.PromptTokens ?? 0) + (result.CompletionTokens ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Image prompt generation failed for job {JobId}.", wc.Job.Id);
            throw;
        }

        var ledeSection = new Section(
            "h2",
            topic,
            [new TextParagraph([new Run(draft.Prompt)]), new TextParagraph([new Run(draft.Notes ?? "")])],
            null,
            [],
            draft.Prompt);
        var ledeWrite = new GccV2WriteSection("image-prompt", topic, "problem", ledeSection, false);
        await PersistAndEmitAsync(wc, ownerUserId, "write", "SectionDrafted", ledeWrite, tokens, ct);

        return new GccV2WriteOutput
        {
            Title = topic,
            MetaDescription = Truncate(draft.Prompt, 160),
            Lede = ledeWrite,
            Sections = [],
            TokensUsed = tokens,
        };
    }

    /// <summary>Unknown content types fail the job — no stub drafts.</summary>
    private Task<GccV2WriteOutput> WriteStubAsync(GccV2WriteContext wc, Guid ownerUserId, string contentType, CancellationToken ct) =>
        throw new InvalidOperationException($"Unsupported content type for WRITE: {contentType}.");

    private async Task<ArticleMetadataDraft> GeneratePillarMetadataAsync(GccV2WriteContext wc, List<string> headings, CancellationToken ct)
    {
        try
        {
            var metaResult = await wc.Provider.CompleteAsync(_prompts.BuildArticleMetadataPrompt(wc.BaseContext), ct);
            var parsed = LlmResponseJsonParser.Parse<ArticleMetadataDraft>(metaResult.Content, "pillar metadata");
            return parsed with { SectionOutline = headings };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Pillar metadata generation failed for job {JobId}; using a fallback title.", wc.Job.Id);
            return new ArticleMetadataDraft(
                Capitalize(wc.BaseContext.TargetKeyword),
                $"A practical guide to {wc.BaseContext.TargetKeyword}.",
                [wc.BaseContext.TargetKeyword],
                headings);
        }
    }

    private async Task<BlogMetadataDraft> GenerateBlogMetadataAsync(GccV2WriteContext wc, List<string> headings, CancellationToken ct)
    {
        try
        {
            var metaResult = await wc.Provider.CompleteAsync(_prompts.BuildStandaloneBlogMetadataPrompt(wc.BaseContext), ct);
            var parsed = LlmResponseJsonParser.Parse<BlogMetadataDraft>(metaResult.Content, "blog metadata");
            return parsed with { SectionOutline = headings };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Blog metadata generation failed for job {JobId}; using a fallback title.", wc.Job.Id);
            return new BlogMetadataDraft(
                Capitalize(wc.BaseContext.TargetKeyword),
                $"A practical guide to {wc.BaseContext.TargetKeyword}.",
                [wc.BaseContext.TargetKeyword],
                headings);
        }
    }

    private async Task PersistAndEmitAsync(
        GccV2WriteContext wc, Guid ownerUserId, string stage, string eventType, GccV2WriteSection write, int tokens, CancellationToken ct)
    {
        var jobId = wc.Job.Id;
        var stagePayload = new { heading = write.Heading, job = write.Job, section = write.Section, usedFallbackStub = write.UsedFallbackStub };
        await _repo.AddStageResultAsync(
            jobId,
            new CreateGccV2StageResultCommand(stage, write.SectionKey, JsonSerializer.Serialize(stagePayload, ContentDocJson), tokens),
            ct);

        var wordCount = ContentDocumentText.CountWords(write.Section);
        await _events.AppendAsync(jobId, ownerUserId, eventType, new
        {
            sectionKey = write.SectionKey,
            heading = write.Heading,
            job = write.Job,
            documentJson = JsonSerializer.Serialize(write.Section, ContentDocJson),
            wordCount,
            usedFallbackStub = write.UsedFallbackStub,
        }, ct: ct);

        if (wc.ExtendLease is not null)
        {
            await wc.ExtendLease(ct);
        }
    }

    private async Task<(GccV2BrandKitContent Kit, GccV2BrandKitDto Dto)> LoadAcceptedBrandKitAsync(
        Guid profileId,
        CancellationToken ct)
    {
        var kits = await _repo.ListBrandKitsByProfileAsync(profileId, ct);
        var kitDto = kits.FirstOrDefault()
            ?? throw new InvalidOperationException($"No brand kit for profile {profileId}.");
        if (!string.Equals(kitDto.VoiceStatus, "accepted", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Brand kit must be accepted before WRITE.");

        var kit = JsonSerializer.Deserialize<GccV2BrandKitContent>(kitDto.KitJson, JsonOpts)
            ?? throw new InvalidOperationException("Brand kit JSON could not be parsed.");
        if (string.IsNullOrWhiteSpace(kit.CompanyName) || string.IsNullOrWhiteSpace(kit.Website))
            throw new InvalidOperationException("Accepted brand kit is missing companyName or website.");
        return (kit, kitDto);
    }

    private async Task<GccV2Outline> LoadOutlineAsync(Guid jobId, CancellationToken ct)
    {
        var results = await _repo.GetStageResultsAsync(jobId, ct);
        var planResult = results
            .Where(r => string.Equals(r.Stage, "plan", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.CompletedAtUtc)
            .FirstOrDefault();

        if (planResult is null)
        {
            return new GccV2Outline([], []);
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<OutlineJsonShape>(planResult.OutputJson, JsonOpts);
            var sections = (parsed?.Sections ?? [])
                .Select((s, i) => new GccV2OutlineSection(
                    string.IsNullOrWhiteSpace(s.Key) ? $"section-{i}" : s.Key,
                    string.IsNullOrWhiteSpace(s.Heading) ? $"Section {i + 1}" : s.Heading,
                    s.Job,
                    s.HierarchyChildHeadings ?? []))
                .ToList();
            return new GccV2Outline(sections, parsed?.HierarchyChildHeadings ?? []);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Could not parse plan-stage outline for job {JobId}; writing with an empty outline.", jobId);
            return new GccV2Outline([], []);
        }
    }

    private static string Capitalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Untitled";
        return char.ToUpperInvariant(value[0]) + value[1..];
    }

    private static string Truncate(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }

    private static string Slugify(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "section";
        var chars = value.ToLowerInvariant()
            .Where(c => char.IsLetterOrDigit(c) || c == ' ' || c == '-')
            .ToArray();
        var slug = new string(chars).Replace(' ', '-');
        while (slug.Contains("--", StringComparison.Ordinal)) slug = slug.Replace("--", "-", StringComparison.Ordinal);
        return string.IsNullOrWhiteSpace(slug) ? "section" : slug.Trim('-');
    }

    private static ArticleDraft BuildSyntheticArticleDraft(GccV2WriteContext wc)
    {
        var title = Capitalize(wc.BaseContext.TargetKeyword);
        var meta = $"A practical guide to {wc.BaseContext.TargetKeyword}.";
        var body = new ContentDocument(
            new Section("h2", title, [new TextParagraph([new Run(meta)])], null, []),
            []);
        return new ArticleDraft(title, meta, body, [wc.BaseContext.TargetKeyword], ContentDocumentText.CountWords(body), [title]);
    }

    private static string? ParseSocialPlatform(string rawBriefJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(rawBriefJson) ? "{}" : rawBriefJson);
            if (doc.RootElement.TryGetProperty("socialPlatform", out var platform))
                return platform.GetString();
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static AdvertisingDraft ParseAdvertisingDraft(string raw)
    {
        var cleaned = raw.Trim();
        if (cleaned.StartsWith("```", StringComparison.Ordinal))
        {
            var start = cleaned.IndexOf('{');
            var end = cleaned.LastIndexOf('}');
            if (start >= 0 && end > start) cleaned = cleaned[start..(end + 1)];
        }

        using var doc = JsonDocument.Parse(cleaned);
        var root = doc.RootElement;
        var title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "Advertiser article" : "Advertiser article";
        var bodyText = root.TryGetProperty("bodyText", out var b) ? b.GetString() ?? "" : "";
        var meta = root.TryGetProperty("metaDescription", out var m) ? m.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(bodyText))
            throw new InvalidOperationException("Advertising draft missing bodyText.");
        return new AdvertisingDraft(title, bodyText, meta);
    }

    private static ImagePromptDraft ParseImagePromptDraft(string raw)
    {
        var cleaned = raw.Trim();
        if (cleaned.StartsWith("```", StringComparison.Ordinal))
        {
            var start = cleaned.IndexOf('{');
            var end = cleaned.LastIndexOf('}');
            if (start >= 0 && end > start) cleaned = cleaned[start..(end + 1)];
        }

        using var doc = JsonDocument.Parse(cleaned);
        var root = doc.RootElement;
        var prompt = root.TryGetProperty("prompt", out var p) ? p.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(prompt))
            throw new InvalidOperationException("Image prompt JSON missing prompt.");
        return new ImagePromptDraft(
            prompt,
            root.TryGetProperty("style", out var s) ? s.GetString() : null,
            root.TryGetProperty("negativePrompt", out var n) ? n.GetString() : null,
            root.TryGetProperty("aspectRatio", out var a) ? a.GetString() : null,
            root.TryGetProperty("notes", out var notes) ? notes.GetString() : null);
    }

    private sealed record AdvertisingDraft(string Title, string BodyText, string MetaDescription);
    private sealed record ImagePromptDraft(string Prompt, string? Style, string? NegativePrompt, string? AspectRatio, string? Notes);

    private sealed record OutlineJsonShape(List<OutlineSectionJsonShape>? Sections, List<string>? HierarchyChildHeadings);

    private sealed record OutlineSectionJsonShape(string? Key, string? Heading, string? Job, List<string>? HierarchyChildHeadings);
}
