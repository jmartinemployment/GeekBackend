using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.Gcw;
using GeekAPI.Services.ContentCreatorV2.Adapters;
using GeekAPI.Services.ContentCreatorV2.ContentTypes;
using GeekAPI.Services.ContentCreatorV2.BrandKit;
using GeekAPI.Services.ContentCreatorV2.Jobs;
using GeekAPI.Services.ContentCreatorV2.Generation;
using GeekAPI.Services.ContentCreatorV2.ToolPages;
using GeekAPI.Services.Rag;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services;
using GeekAPI.Services.Workflow.Services.PromptBuilders;
using GeekAPI.Services.Workflow.Services.SchemaBuilders;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace GeekAPI.Services.ContentCreatorV2.Write;

/// <summary>One written section: its plan-stage identity (key/heading/job) plus the actual
/// generated <see cref="Section"/> body.</summary>
public sealed record GccV2WriteSection(
    string SectionKey,
    string Heading,
    string? Job,
    Section Section,
    bool UsedFallbackStub,
    IReadOnlyList<RagCitationDto>? Citations = null,
    GccV2GenerationProvenance? Provenance = null,
    IReadOnlyList<RagGenerateSourceDto>? Sources = null);

/// <summary>Everything WRITE produced for a job — enough for VALIDATE to build a
/// <see cref="ContentDocument"/>, run OverlapGate, and target REPAIR at one section.</summary>
public sealed class GccV2WriteOutput
{
    public required string Title { get; init; }
    public required string? MetaDescription { get; init; }
    public required GccV2WriteSection Lede { get; init; }
    public required IReadOnlyList<GccV2WriteSection> Sections { get; init; }
    public int TokensUsed { get; init; }
    public IReadOnlyList<string> Keywords { get; init; } = [];
    public GccV2ToolPageWriteExtras? ToolPage { get; init; }
    public IReadOnlyList<RagCitationDto> Citations { get; init; } = [];
    public IReadOnlyList<GccV2GenerationProvenance> Provenance { get; init; } = [];
    public IReadOnlyList<RagGenerateSourceDto> Sources { get; init; } = [];

    public ContentDocument ToContentDocument() => new(Lede.Section, Sections.Select(s => s.Section).ToList());

    /// <summary>Lede + body sections, in document order — the full OverlapGate comparison set.</summary>
    public IReadOnlyList<GccV2WriteSection> AllSections => new[] { Lede }.Concat(Sections).ToList();

    public GccV2WriteOutput WithSection(GccV2WriteSection replacement)
    {
        if (replacement.SectionKey == Lede.SectionKey)
        {
            return new GccV2WriteOutput { Title = Title, MetaDescription = MetaDescription, Lede = replacement, Sections = Sections, TokensUsed = TokensUsed, Keywords = Keywords, ToolPage = ToolPage, Citations = MergeCitations(new[] { replacement }.Concat(Sections)), Provenance = MergeProvenance(new[] { replacement }.Concat(Sections)), Sources = MergeSources(new[] { replacement }.Concat(Sections)) };
        }

        var sections = Sections.Select(s => s.SectionKey == replacement.SectionKey ? replacement : s).ToList();
        return new GccV2WriteOutput { Title = Title, MetaDescription = MetaDescription, Lede = Lede, Sections = sections, TokensUsed = TokensUsed, Keywords = Keywords, ToolPage = ToolPage, Citations = MergeCitations(new[] { Lede }.Concat(sections)), Provenance = MergeProvenance(new[] { Lede }.Concat(sections)), Sources = MergeSources(new[] { Lede }.Concat(sections)) };
    }

    public GccV2WriteOutput WithAppendedSection(GccV2WriteSection section) =>
        new()
        {
            Title = Title,
            MetaDescription = MetaDescription,
            Lede = Lede,
            Sections = Sections.Append(section).ToList(),
            TokensUsed = TokensUsed,
            Keywords = Keywords,
            ToolPage = ToolPage,
            Citations = MergeCitations(new[] { Lede }.Concat(Sections).Append(section)),
            Provenance = MergeProvenance(new[] { Lede }.Concat(Sections).Append(section)),
            Sources = MergeSources(new[] { Lede }.Concat(Sections).Append(section)),
        };

    internal static IReadOnlyList<RagCitationDto> MergeCitations(IEnumerable<GccV2WriteSection> sections) =>
        sections.SelectMany(s => s.Citations ?? []).DistinctBy(c => $"{c.PageId}|{c.Url}|{c.Quote}").ToList();

    internal static IReadOnlyList<GccV2GenerationProvenance> MergeProvenance(IEnumerable<GccV2WriteSection> sections) =>
        sections.Select(s => s.Provenance).Where(p => p is not null).Cast<GccV2GenerationProvenance>().ToList();

    internal static IReadOnlyList<RagGenerateSourceDto> MergeSources(IEnumerable<GccV2WriteSection> sections) =>
        sections.SelectMany(s => s.Sources ?? [])
            .DistinctBy(s => $"{s.PageId}|{s.Url}|{s.Kind}")
            .ToList();
}

public sealed record GccV2OutlineSection(
    string Key,
    string Heading,
    string? Job,
    List<string> HierarchyChildHeadings,
    string? Brief = null,
    IReadOnlyList<string>? EvidenceIds = null);

public sealed record GccV2Outline(List<GccV2OutlineSection> Sections, List<string> HierarchyChildHeadings);

/// <summary>Everything needed to write (or repair) sections for one job — loaded once per stage
/// run so REPAIR doesn't re-fetch brief/brand-kit/outline for every flagged section.</summary>
public sealed record GccV2WriteContext(
    GccV2JobDto Job,
    GccV2BriefDto Brief,
    GccV2BrandKitContent? BrandKit,
    GccV2Outline Outline,
    ProjectGenerationContext BaseContext,
    IContentGenerationProvider Provider,
    GccV2GenerationBrief GenerationBrief,
    GccV2JobModelPolicyOverride? JobModelPolicyOverride)
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
    private readonly GccV2PartnerToolWriteService _partnerToolWrite;
    private readonly GccV2ToolOverviewWriteService _toolOverviewWrite;
    private readonly RagGenerateService _rag;
    private readonly ContentModelPolicy _modelPolicy;
    private readonly GccV2JobModelPolicyOverrideStore _jobModelPolicies;
    private readonly ILogger<GccV2WriteService> _logger;

    public GccV2WriteService(
        HttpGccV2Repository repo,
        GccV2JobEventWriter events,
        GccV2ContextAdapter contextAdapter,
        IContentPromptBuilder prompts,
        IContentProviderFactory providers,
        GccV2PartnerToolWriteService partnerToolWrite,
        GccV2ToolOverviewWriteService toolOverviewWrite,
        RagGenerateService rag,
        ContentModelPolicy modelPolicy,
        GccV2JobModelPolicyOverrideStore jobModelPolicies,
        ILogger<GccV2WriteService> logger)
    {
        _repo = repo;
        _events = events;
        _contextAdapter = contextAdapter;
        _prompts = prompts;
        _providers = providers;
        _partnerToolWrite = partnerToolWrite;
        _toolOverviewWrite = toolOverviewWrite;
        _rag = rag;
        _modelPolicy = modelPolicy;
        _jobModelPolicies = jobModelPolicies;
        _logger = logger;
    }

    /// <summary>Loads brief + brand kit + PLAN's outline and builds the base context — shared by
    /// the initial WRITE pass and every later REPAIR call so they never drift.</summary>
    public async Task<GccV2WriteContext> PrepareAsync(GccV2JobDto job, CancellationToken ct)
    {
        var brief = await _repo.GetBriefAsync(job.BriefId, ct)
            ?? throw new InvalidOperationException($"Brief {job.BriefId} not found for job {job.Id}.");

        if ((job.ProjectSiteCrawlRunId ?? job.SiteAnalysisProfileId) is not { } profileId)
            throw new InvalidOperationException("WRITE requires a projectSiteCrawlRunId — start from a project-site crawl.");

        var (brandKit, kitDto) = await LoadAcceptedBrandKitAsync(profileId, ct);
        var create = await _repo.GetCreateAsync(job.CreateId, ct)
            ?? throw new InvalidOperationException($"Create {job.CreateId} not found for job {job.Id}.");
        var siteSection = GccV2SiteSection.ParseSiteSection(create.SiteSectionJson);
        if (siteSection is null || siteSection.RelatedPages is null || siteSection.RelatedPages.Count == 0)
            throw new InvalidOperationException("WRITE requires create.SiteSectionJson with non-empty relatedPages.");

        var outline = await LoadOutlineAsync(job.Id, ct);
        var provider = _providers.GetDefault();
        var baseContext = _contextAdapter.BuildContext(brief, brandKit, provider.ProviderType, siteSection);
        _ = kitDto;
        var generationBrief = GccV2GenerationBriefAssembler.Assemble(job, brief, create, brandKit);
        var jobModelPolicy = await _jobModelPolicies.LoadLatestAsync(job.Id, ct);
        return new GccV2WriteContext(
            job, brief, brandKit, outline, baseContext, provider, generationBrief, jobModelPolicy);
    }

    /// <summary>Rebuilds a <see cref="GccV2WriteOutput"/> from the job's persisted result + stage metadata —
    /// used by manual readiness repair on already-<c>ready</c> jobs.</summary>
    public async Task<GccV2WriteOutput?> ReconstructOutputAsync(GccV2JobDto job, CancellationToken ct)
    {
        var snapshots = await _repo.GetStageResultsAsync(job.Id, ct);
        var preferredSnapshotStage = string.Equals(job.Stage, "validate", StringComparison.OrdinalIgnoreCase)
            ? "final-synthesis-document"
            : "final-synthesis-input";
        var snapshot = snapshots
            .Where(r => string.Equals(r.Stage, preferredSnapshotStage, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.CompletedAtUtc)
            .FirstOrDefault();
        if (snapshot is not null)
        {
            try
            {
                var restored = JsonSerializer.Deserialize<GccV2WriteOutput>(snapshot.OutputJson, ContentDocJson);
                if (restored is not null) return restored;
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Could not parse {SnapshotStage} for job {JobId}.",
                    preferredSnapshotStage,
                    job.Id);
            }
        }

        JobResultPayload? payload = null;
        if (!string.IsNullOrWhiteSpace(job.ResultJson))
        {
            try
            {
                payload = JsonSerializer.Deserialize<JobResultPayload>(job.ResultJson, ContentDocJson);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Could not parse ResultJson for job {JobId}.", job.Id);
            }
        }

        var sectionMeta = await LoadLatestSectionMetaAsync(job.Id, ct);
        var outline = await LoadOutlineAsync(job.Id, ct);
        if (payload?.Document is not { } document)
        {
            if (!sectionMeta.TryGetValue("lede", out var stagedLede) || stagedLede.Section is null)
                return null;
            var stagedSections = outline.Sections
                .Where(s => sectionMeta.ContainsKey(s.Key))
                .Select(s =>
                {
                    var meta = sectionMeta[s.Key];
                    return new GccV2WriteSection(
                        s.Key, meta.Heading, meta.Job ?? s.Job, meta.Section!,
                        meta.UsedFallbackStub, meta.Citations, meta.Provenance, meta.Sources);
                })
                .ToList();
            var brief = await _repo.GetBriefAsync(job.BriefId, ct);
            var lede = new GccV2WriteSection(
                "lede", stagedLede.Heading, stagedLede.Job, stagedLede.Section,
                stagedLede.UsedFallbackStub, stagedLede.Citations, stagedLede.Provenance, stagedLede.Sources);
            return new GccV2WriteOutput
            {
                Title = brief?.TargetKeyword ?? "Untitled",
                MetaDescription = null,
                Lede = lede,
                Sections = stagedSections,
                TokensUsed = 0,
                Citations = GccV2WriteOutput.MergeCitations(new[] { lede }.Concat(stagedSections)),
                Provenance = GccV2WriteOutput.MergeProvenance(new[] { lede }.Concat(stagedSections)),
                Sources = GccV2WriteOutput.MergeSources(new[] { lede }.Concat(stagedSections)),
            };
        }

        sectionMeta.TryGetValue("lede", out var ledeMeta);
        var ledeWrite = new GccV2WriteSection(
            "lede",
            ledeMeta?.Heading ?? document.Lede.Heading,
            ledeMeta?.Job ?? "problem",
            document.Lede,
            ledeMeta?.UsedFallbackStub ?? false,
            ledeMeta?.Citations,
            ledeMeta?.Provenance,
            ledeMeta?.Sources);

        var sections = new List<GccV2WriteSection>();
        for (var i = 0; i < document.Sections.Count; i++)
        {
            var section = document.Sections[i];
            var outlineEntry = i < outline.Sections.Count ? outline.Sections[i] : null;
            var key = outlineEntry?.Key ?? $"section-{i}";
            sectionMeta.TryGetValue(key, out var meta);
            sections.Add(new GccV2WriteSection(
                key,
                meta?.Heading ?? section.Heading,
                meta?.Job ?? outlineEntry?.Job,
                section,
                meta?.UsedFallbackStub ?? false,
                meta?.Citations,
                meta?.Provenance,
                meta?.Sources));
        }

        return new GccV2WriteOutput
        {
            Title = payload.Title ?? "Untitled",
            MetaDescription = payload.MetaDescription,
            Lede = ledeWrite,
            Sections = sections,
            TokensUsed = 0,
            Citations = GccV2WriteOutput.MergeCitations(new[] { ledeWrite }.Concat(sections)),
            Provenance = GccV2WriteOutput.MergeProvenance(new[] { ledeWrite }.Concat(sections)),
            Sources = GccV2WriteOutput.MergeSources(new[] { ledeWrite }.Concat(sections)),
        };
    }

    /// <summary>Appends a trailing People Also Ask FAQ section from operator PAA questions.</summary>
    public async Task<GccV2WriteOutput> AppendFaqSectionAsync(
        GccV2WriteContext wc,
        Guid ownerUserId,
        GccV2WriteOutput current,
        IReadOnlyList<string> faqQuestions,
        CancellationToken ct)
    {
        var questions = (faqQuestions.Count > 0 ? faqQuestions : wc.BaseContext.PeopleAlsoAskQuestions)
            .Where(q => !string.IsNullOrWhiteSpace(q))
            .Take(12)
            .ToList();
        if (questions.Count == 0)
            throw new InvalidOperationException("Cannot append FAQ — no PAA questions in the brief.");

        var entry = new GccV2OutlineSection("people-also-ask", "People Also Ask", "faq", questions);
        var headings = current.Sections.Select(s => s.Heading).Append(entry.Heading).ToList();
        var metadata = new ArticleMetadataDraft(
            current.Title,
            current.MetaDescription ?? "",
            [wc.BaseContext.TargetKeyword],
            headings);

        var (write, tokens) = await DraftOutlineSectionAsync(
            wc,
            ownerUserId,
            entry,
            current.Sections.Count,
            current.Sections.Count + 1,
            headings,
            metadata,
            ct);

        var appended = current.WithAppendedSection(write);
        return new GccV2WriteOutput
        {
            Title = appended.Title,
            MetaDescription = appended.MetaDescription,
            Lede = appended.Lede,
            Sections = appended.Sections,
            TokensUsed = current.TokensUsed + tokens,
            ToolPage = current.ToolPage,
            Citations = appended.Citations,
            Provenance = appended.Provenance,
            Sources = appended.Sources,
        };
    }

    public Task<GccV2WriteOutput> WriteAsync(GccV2WriteContext wc, Guid ownerUserId, CancellationToken ct)
    {
        var contentType = GccV2LongFormTypes.Normalize(wc.Job.ContentType);
        return contentType switch
        {
            GccV2LongFormTypes.Pillar
                or GccV2LongFormTypes.Comparison
                or GccV2LongFormTypes.CaseStudy
                or GccV2LongFormTypes.Alternatives
                or GccV2LongFormTypes.TechArticle
                or GccV2LongFormTypes.Service
                or GccV2LongFormTypes.Local
                or GccV2LongFormTypes.Whitepaper => WritePillarAsync(wc, ownerUserId, ct),
            GccV2LongFormTypes.Blog
                or GccV2LongFormTypes.Guide
                or GccV2LongFormTypes.Listicle
                or GccV2ChannelTypes.LinkedInCarousel
                or GccV2ChannelTypes.LinkedInDocument => WriteBlogAsync(wc, ownerUserId, ct),
            GccV2LongFormTypes.Tool => WriteToolAsync(wc, ownerUserId, ct),
            "email" => WriteEmailAsync(wc, ownerUserId, ct),
            "social" => WriteSocialAsync(wc, ownerUserId, ct),
            "ads" => WriteAdsAsync(wc, ownerUserId, ct),
            "image-prompt" => WriteImagePromptAsync(wc, ownerUserId, ct),
            _ => WriteStubAsync(wc, ownerUserId, contentType, ct),
        };
    }

    public static bool RequiresFinalSynthesis(string? contentType) =>
        GccV2LongFormTypes.Normalize(contentType) is
            GccV2LongFormTypes.Pillar or GccV2LongFormTypes.Blog or GccV2LongFormTypes.Guide
            or GccV2LongFormTypes.TechArticle or GccV2LongFormTypes.CaseStudy
            or GccV2LongFormTypes.Whitepaper or GccV2LongFormTypes.Listicle
            or GccV2LongFormTypes.Comparison or GccV2LongFormTypes.Alternatives
            or GccV2LongFormTypes.Tool or GccV2LongFormTypes.Service or GccV2LongFormTypes.Local;

    public async Task<GccV2WriteOutput> FinalSynthesizeAsync(
        GccV2WriteContext wc,
        GccV2WriteOutput current,
        CancellationToken ct)
    {
        if (!RequiresFinalSynthesis(wc.Job.ContentType)) return current;

        var sources = current.Sources.Count > 0
            ? current.Sources
            : current.Citations.Select(c => new RagGenerateSourceDto
            {
                PageId = c.PageId,
                Url = c.Url,
                Title = c.Title,
                CrawlType = c.CrawlType,
                Kind = "page",
            }).DistinctBy(s => $"{s.PageId}|{s.Url}").ToList();
        if (sources.Count == 0)
            throw new InvalidOperationException(
                "Final synthesis requires persisted evidence sources from the WRITE stage.");

        var route = GccV2ContentTypeRagMapper.Map(wc.Job.ContentType);
        var selection = _modelPolicy.Select(
            ContentGenerationStage.FinalSynthesis,
            wc.GenerationBrief,
            wc.JobModelPolicyOverride);
        var draft = ToStableMarkdown(current);
        var stopwatch = Stopwatch.StartNew();
        var response = await _rag.GenerateAsync(
            wc.Job.OwnerUserId,
            new RagGenerateRequest
            {
                WritingIntent = route.WritingIntent,
                Topic = wc.GenerationBrief.TargetKeyword,
                GenerationStage = "finalSynthesis",
                DraftContent = draft,
                Sources = sources,
                CanonicalBrief = wc.GenerationBrief.ToCanonicalBrief(),
                ModelPolicyPreset = ContentModelPolicy.PresetValue(selection.Preset),
                ModelPolicyVersion = selection.PolicyVersion,
                StageModelOverrides = ContentModelPolicy.ProducerOverridesForRequest(
                    wc.GenerationBrief, selection, "finalSynthesis", wc.JobModelPolicyOverride),
                RequestedModel = selection.EffectiveModel,
                RequireCiteable = true,
            },
            ct);
        stopwatch.Stop();
        if (string.IsNullOrWhiteSpace(response.Content))
            throw new InvalidOperationException("Citeable RAG returned no final-synthesis content.");
        if (response.Citations is not { Count: > 0 })
            throw new InvalidOperationException("Final synthesis returned no verified citations.");
        if (response.Provenance is null)
            throw new InvalidOperationException("Final synthesis returned no provenance.");

        var parsed = ParseSynthesizedMarkdown(response.Content, current.AllSections);
        var evidenceIds = response.Citations.Select(c => c.PageId)
            .Where(id => !string.IsNullOrWhiteSpace(id)).Cast<string>()
            .Concat(response.Sources.Select(s => s.PageId).Where(id => !string.IsNullOrWhiteSpace(id)).Cast<string>())
            .Concat(response.Provenance.EvidenceIds)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var provenance = new GccV2GenerationProvenance(
            wc.GenerationBrief.Version,
            selection.PolicyVersion,
            response.PromptVersion,
            selection.RequestedModel,
            response.ModelUsed,
            response.RetrievalMode,
            evidenceIds,
            response.Warnings.Concat(response.EvidenceWarnings).Distinct().ToList(),
            stopwatch.ElapsedMilliseconds,
            selection);

        var knownHeadings = current.AllSections
            .Select(section => section.Heading)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknownCitationHeadings = response.Citations
            .Where(citation => string.IsNullOrWhiteSpace(citation.SectionTitle)
                || !knownHeadings.Contains(citation.SectionTitle.Trim()))
            .Select(citation => citation.SectionTitle ?? "(missing)")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (unknownCitationHeadings.Count > 0)
            throw new InvalidOperationException(
                "Final synthesis returned citation sectionTitle values that do not match the preserved headings: "
                + string.Join(", ", unknownCitationHeadings));

        var rebuilt = current.AllSections.Select((original, index) =>
        {
            var sectionCitations = response.Citations
                .Where(citation => string.Equals(
                    citation.SectionTitle?.Trim(),
                    original.Heading,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
            return original with
            {
                Section = parsed[index],
                Citations = sectionCitations,
                Provenance = index == 0 ? provenance : original.Provenance,
                Sources = response.Sources.Count > 0 ? response.Sources : sources,
            };
        }).ToList();
        return new GccV2WriteOutput
        {
            Title = current.Title,
            MetaDescription = current.MetaDescription,
            Lede = rebuilt[0],
            Sections = rebuilt.Skip(1).ToList(),
            TokensUsed = current.TokensUsed,
            Keywords = current.Keywords,
            ToolPage = current.ToolPage,
            Citations = response.Citations,
            Provenance = current.Provenance.Append(provenance).ToList(),
            Sources = response.Sources.Count > 0 ? response.Sources : sources,
        };
    }

    public async Task PersistFinalSynthesisAsync(
        GccV2WriteContext wc,
        Guid ownerUserId,
        GccV2WriteOutput output,
        CancellationToken ct)
    {
        await _repo.AddStageResultAsync(
            wc.Job.Id,
            new CreateGccV2StageResultCommand(
                "final-synthesis-document",
                null,
                JsonSerializer.Serialize(output, ContentDocJson),
                0),
            ct);
        foreach (var section in output.AllSections)
            await PersistAndEmitAsync(
                wc, ownerUserId, "final-synthesis", "SectionFinalSynthesized", section, 0, ct);
    }

    public async Task PersistFinalSynthesisInputAsync(
        GccV2WriteContext wc,
        GccV2WriteOutput output,
        CancellationToken ct)
    {
        await _repo.AddStageResultAsync(
            wc.Job.Id,
            new CreateGccV2StageResultCommand(
                "final-synthesis-input",
                null,
                JsonSerializer.Serialize(output, ContentDocJson),
                0),
            ct);
    }

    internal static string ToStableMarkdown(GccV2WriteOutput output)
    {
        var builder = new StringBuilder().Append("# ").AppendLine(output.Title).AppendLine();
        foreach (var write in output.AllSections)
        {
            builder.Append("## ").AppendLine(write.Heading).AppendLine();
            AppendParagraphs(builder, write.Section.Paragraphs);
            foreach (var child in write.Section.Children)
            {
                builder.Append("### ").AppendLine(child.Heading).AppendLine();
                AppendParagraphs(builder, child.Paragraphs);
            }
        }
        return builder.ToString().TrimEnd() + "\n";
    }

    internal static IReadOnlyList<Section> ParseSynthesizedMarkdown(
        string markdown,
        IReadOnlyList<GccV2WriteSection> expected)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var found = lines.Select((line, index) => (line, index))
            .Where(item => item.line.StartsWith("## ", StringComparison.Ordinal)
                           && !item.line.StartsWith("### ", StringComparison.Ordinal))
            .ToList();
        if (found.Count != expected.Count)
            throw new InvalidOperationException(
                $"Final synthesis changed document structure: expected {expected.Count} H2 headings, received {found.Count}.");

        var sections = new List<Section>(expected.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            var heading = found[i].line[3..].Trim();
            if (!string.Equals(heading, expected[i].Heading.Trim(), StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Final synthesis changed heading {i + 1}: expected '{expected[i].Heading}', received '{heading}'.");
            var end = i + 1 < found.Count ? found[i + 1].index : lines.Length;
            var body = lines[(found[i].index + 1)..end];
            var (paragraphs, children) = ParseSectionBody(body, expected[i].Section.Children);
            sections.Add(new Section(
                expected[i].Section.Tag,
                expected[i].Heading,
                paragraphs,
                expected[i].Section.Href,
                children,
                expected[i].Section.ImagePrompt,
                expected[i].Section.Id));
        }
        return sections;
    }

    private static (IReadOnlyList<Paragraph> Paragraphs, IReadOnlyList<Section> Children) ParseSectionBody(
        IReadOnlyList<string> lines,
        IReadOnlyList<Section> expectedChildren)
    {
        var headings = lines.Select((line, index) => (line, index))
            .Where(item => item.line.StartsWith("### ", StringComparison.Ordinal))
            .ToList();
        if (headings.Count != expectedChildren.Count)
            throw new InvalidOperationException(
                $"Final synthesis changed nested structure: expected {expectedChildren.Count} H3 headings, received {headings.Count}.");

        var topEnd = headings.Count > 0 ? headings[0].index : lines.Count;
        var topLines = lines.Take(topEnd).ToList();
        var paragraphs = topLines.Any(line => !string.IsNullOrWhiteSpace(line))
            ? ParseMarkdownParagraphs(topLines)
            : [];
        var children = new List<Section>(headings.Count);
        for (var i = 0; i < headings.Count; i++)
        {
            var heading = headings[i].line[4..].Trim();
            if (!string.Equals(heading, expectedChildren[i].Heading.Trim(), StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Final synthesis changed nested heading {i + 1}: expected '{expectedChildren[i].Heading}', received '{heading}'.");
            var end = i + 1 < headings.Count ? headings[i + 1].index : lines.Count;
            children.Add(expectedChildren[i] with
            {
                Paragraphs = ParseMarkdownParagraphs(
                    lines.Skip(headings[i].index + 1).Take(end - headings[i].index - 1).ToList()),
            });
        }
        return (paragraphs, children);
    }

    private static void AppendParagraphs(StringBuilder builder, IReadOnlyList<Paragraph> paragraphs)
    {
        foreach (var paragraph in paragraphs)
        {
            if (paragraph is TextParagraph text)
                builder.AppendLine(SerializeRuns(text.Runs)).AppendLine();
            else if (paragraph is ListParagraph list)
            {
                for (var i = 0; i < list.Items.Count; i++)
                    builder.Append(list.Ordered ? $"{i + 1}. " : "- ")
                        .AppendLine(SerializeRuns(list.Items[i]));
                builder.AppendLine();
            }
        }
    }

    private static string SerializeRuns(IReadOnlyList<Run> runs) =>
        string.Concat(runs.Select(run =>
        {
            var text = run.Href is null ? run.Text : $"[{run.Text}]({run.Href})";
            if (run.Bold) text = $"**{text}**";
            if (run.Italic) text = $"*{text}*";
            return text;
        }));

    private static IReadOnlyList<Paragraph> ParseMarkdownParagraphs(IReadOnlyList<string> lines)
    {
        var result = new List<Paragraph>();
        var text = new List<string>();
        void FlushText()
        {
            if (text.Count == 0) return;
            result.Add(new TextParagraph(ParseRuns(string.Join(" ", text))));
            text.Clear();
        }

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0) { FlushText(); continue; }
            var unordered = line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal);
            var ordered = Regex.Match(line, @"^\d+\.\s+");
            if (unordered || ordered.Success)
            {
                FlushText();
                var isOrdered = ordered.Success;
                var items = new List<IReadOnlyList<Run>>();
                while (i < lines.Count)
                {
                    line = lines[i].Trim();
                    var match = Regex.Match(line, @"^\d+\.\s+");
                    var matches = isOrdered ? match.Success : line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal);
                    if (!matches) { i--; break; }
                    items.Add(ParseRuns(isOrdered ? line[match.Length..] : line[2..]));
                    i++;
                }
                result.Add(new ListParagraph(isOrdered, items));
                continue;
            }
            text.Add(line);
        }
        FlushText();
        if (result.Count == 0) throw new InvalidOperationException("Final synthesis produced an empty section.");
        return result;
    }

    private static IReadOnlyList<Run> ParseRuns(string text)
    {
        var runs = new List<Run>();
        var cursor = 0;
        foreach (Match match in Regex.Matches(text, @"\[([^\]]+)\]\(([^)\s]+)\)"))
        {
            if (match.Index > cursor) runs.Add(new Run(text[cursor..match.Index]));
            runs.Add(new Run(match.Groups[1].Value, Href: match.Groups[2].Value));
            cursor = match.Index + match.Length;
        }
        if (cursor < text.Length) runs.Add(new Run(text[cursor..]));
        return runs.Count == 0 ? [new Run(text)] : runs;
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

        var outlineEntry = wc.Outline.Sections.FirstOrDefault(s => s.Key == target.SectionKey)
            ?? new GccV2OutlineSection(target.SectionKey, target.Heading, target.Job, []);
        GccV2WriteSection write;
        try
        {
            write = await GenerateRagSectionAsync(
                wc, outlineEntry, headings, completedSectionSummaries: [revisionNotes],
                ContentGenerationStage.Repair, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rewrite of section \"{Heading}\" failed for job {JobId}.", target.Heading, wc.Job.Id);
            throw;
        }

        await PersistAndEmitAsync(wc, ownerUserId, stage, eventType, write, 0, ct);
        return write;
    }

    private async Task<GccV2WriteOutput> WritePillarAsync(GccV2WriteContext wc, Guid ownerUserId, CancellationToken ct)
    {
        var outlineSections = wc.Outline.Sections;
        var headings = outlineSections.Select(s => s.Heading).ToList();

        var metadata = await GeneratePillarMetadataAsync(wc, headings, ct);

        // Pillar lede replaces outline section 0 — inherit its PLAN "problem" role + must-mentions.
        var bodyStart = outlineSections.Count > 0 ? 1 : 0;
        var ledeOutline = GccV2WriteOutlineRules.SkippedOutlineEntryForLede(outlineSections, bodyStart);
        GccV2WriteSection generatedLede;
        try
        {
            generatedLede = await GenerateRagSectionAsync(
                wc,
                ledeOutline ?? new GccV2OutlineSection("lede", "Introduction", "problem", []),
                headings,
                [],
                ContentGenerationStage.Section,
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pillar lede generation failed for job {JobId}.", wc.Job.Id);
            throw;
        }

        var ledeWrite = generatedLede with { SectionKey = "lede" };
        await PersistAndEmitAsync(wc, ownerUserId, "write", "SectionDrafted", ledeWrite, 0, ct);

        var sections = new List<GccV2WriteSection>();
        var tokensUsed = 0;
        bodyStart = GccV2WriteOutlineRules.FirstBodyOutlineIndex(ledeWrite.Heading, outlineSections, pillar: true);
        for (var i = bodyStart; i < outlineSections.Count; i++)
        {
            var entry = outlineSections[i];
            var (write, tokens) = await DraftOutlineSectionAsync(
                wc, ownerUserId, entry, i, outlineSections.Count, headings, metadata, ct);
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
            Keywords = metadata.Keywords,
            Citations = GccV2WriteOutput.MergeCitations(new[] { ledeWrite }.Concat(sections)),
            Provenance = GccV2WriteOutput.MergeProvenance(new[] { ledeWrite }.Concat(sections)),
            Sources = GccV2WriteOutput.MergeSources(new[] { ledeWrite }.Concat(sections)),
        };
    }

    private async Task<GccV2WriteOutput> WriteBlogAsync(GccV2WriteContext wc, Guid ownerUserId, CancellationToken ct)
    {
        var outlineSections = wc.Outline.Sections;
        var headings = outlineSections.Select(s => s.Heading).ToList();

        var blogMeta = await GenerateBlogMetadataAsync(wc, headings, ct);
        var articleMeta = new ArticleMetadataDraft(blogMeta.Title, blogMeta.MetaDescription, blogMeta.Keywords, headings);

        GccV2WriteSection generatedLede;
        try
        {
            var ledeEntry = outlineSections.FirstOrDefault()
                ?? new GccV2OutlineSection("lede", "Introduction", "problem", []);
            generatedLede = await GenerateRagSectionAsync(
                wc, ledeEntry, headings, [], ContentGenerationStage.Section, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Blog lede generation failed for job {JobId}.", wc.Job.Id);
            throw;
        }

        var bodyStart = GccV2WriteOutlineRules.FirstBodyOutlineIndex(generatedLede.Heading, outlineSections, pillar: false);
        var ledeOutline = GccV2WriteOutlineRules.SkippedOutlineEntryForLede(outlineSections, bodyStart);
        var ledeWrite = generatedLede with { SectionKey = "lede", Job = ledeOutline?.Job ?? "problem" };
        await PersistAndEmitAsync(wc, ownerUserId, "write", "SectionDrafted", ledeWrite, 0, ct);

        var sections = new List<GccV2WriteSection>();
        var tokensUsed = 0;
        for (var i = bodyStart; i < outlineSections.Count; i++)
        {
            var entry = outlineSections[i];
            var (write, tokens) = await DraftOutlineSectionAsync(
                wc, ownerUserId, entry, i, outlineSections.Count, headings, articleMeta, ct);
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
            Keywords = blogMeta.Keywords,
            Citations = GccV2WriteOutput.MergeCitations(new[] { ledeWrite }.Concat(sections)),
            Provenance = GccV2WriteOutput.MergeProvenance(new[] { ledeWrite }.Concat(sections)),
            Sources = GccV2WriteOutput.MergeSources(new[] { ledeWrite }.Concat(sections)),
        };
    }

    private async Task<GccV2WriteOutput> WriteToolAsync(GccV2WriteContext wc, Guid ownerUserId, CancellationToken ct)
    {
        var target = GccV2ToolPageTargetParser.Parse(wc.Brief.RawBriefJson);
        var headings = wc.Outline.Sections.Select(s => s.Heading).ToList();
        var ragDrafts = new List<GccV2WriteSection>();
        foreach (var entry in wc.Outline.Sections.Where(s =>
                     !string.Equals(s.Job, "faq", StringComparison.OrdinalIgnoreCase)))
        {
            ragDrafts.Add(await GenerateRagSectionAsync(
                wc, entry, headings, [], ContentGenerationStage.Section, ct));
        }
        if (ragDrafts.Count == 0)
            throw new InvalidOperationException("Tool job has no outline sections for citeable RAG generation.");

        var generatedBody = ragDrafts.Select(s => s.Section).ToList();
        GccV2WriteOutput output;
        if (target?.IsPartner == true)
        {
            output = await _partnerToolWrite.WriteAsync(wc, ownerUserId, target, ct, generatedBody);
        }
        else if (target?.IsOverview == true)
        {
            output = await _toolOverviewWrite.WriteAsync(wc, ownerUserId, target, ct, generatedBody);
        }
        else
        {
            throw new InvalidOperationException(
                "Tool job is missing toolPageTarget.kind — expected overview (at generate) or partner (after pillar spawn).");
        }

        var ragSections = new List<GccV2WriteSection>();
        var existingSections = output.AllSections;
        for (var i = 0; i < existingSections.Count; i++)
        {
            var existing = existingSections[i];
            var generated = ragDrafts[Math.Min(i, ragDrafts.Count - 1)];
            generated = generated with { SectionKey = existing.SectionKey, Heading = existing.Heading, Job = existing.Job };
            await PersistAndEmitAsync(wc, ownerUserId, "write", "SectionDrafted", generated, 0, ct);
            ragSections.Add(generated);
        }

        var lede = ragSections[0];
        var body = ragSections.Skip(1).ToList();
        return new GccV2WriteOutput
        {
            Title = output.Title,
            MetaDescription = output.MetaDescription,
            Lede = lede,
            Sections = body,
            TokensUsed = output.TokensUsed,
            Keywords = output.Keywords,
            ToolPage = output.ToolPage,
            Citations = GccV2WriteOutput.MergeCitations(ragSections),
            Provenance = GccV2WriteOutput.MergeProvenance(ragSections),
            Sources = GccV2WriteOutput.MergeSources(ragSections),
        };
    }

    private async Task<GccV2WriteOutput> WriteEmailAsync(GccV2WriteContext wc, Guid ownerUserId, CancellationToken ct)
    {
        return await WriteRagCompleteAsync(wc, ownerUserId, "email-body", "Email", ct);
    }

    private async Task<GccV2WriteOutput> WriteSocialAsync(GccV2WriteContext wc, Guid ownerUserId, CancellationToken ct)
    {
        var platform = ParseSocialPlatform(wc.Brief.RawBriefJson) ?? "LinkedIn";
        return await WriteRagCompleteAsync(wc, ownerUserId, "social-post", $"{platform} post", ct);
    }

    private async Task<GccV2WriteOutput> WriteAdsAsync(GccV2WriteContext wc, Guid ownerUserId, CancellationToken ct)
    {
        return await WriteRagCompleteAsync(wc, ownerUserId, "ads-body", "Advertising variations", ct);
    }

    private async Task<GccV2WriteOutput> WriteRagCompleteAsync(
        GccV2WriteContext wc,
        Guid ownerUserId,
        string sectionKey,
        string heading,
        CancellationToken ct,
        string? seedContext = null)
    {
        var route = GccV2ContentTypeRagMapper.Map(wc.Job.ContentType);
        var stage = route.IsImagePrompt ? ContentGenerationStage.ImagePrompt : ContentGenerationStage.Complete;
        var selection = _modelPolicy.Select(stage, wc.GenerationBrief, wc.JobModelPolicyOverride);
        var stopwatch = Stopwatch.StartNew();
        var response = await _rag.GenerateAsync(
            wc.Job.OwnerUserId,
            new RagGenerateRequest
            {
                WritingIntent = route.WritingIntent,
                Topic = string.IsNullOrWhiteSpace(seedContext)
                    ? $"{wc.GenerationBrief.Title}: {wc.GenerationBrief.TargetKeyword}"
                    : $"{wc.GenerationBrief.Title}: {wc.GenerationBrief.TargetKeyword}\nSpecialized source context:\n{seedContext}",
                TargetEntities = wc.GenerationBrief.TargetEntities.ToList(),
                GenerationStage = "complete",
                CanonicalBrief = wc.GenerationBrief.ToCanonicalBrief(),
                ModelPolicyPreset = ContentModelPolicy.PresetValue(selection.Preset),
                ModelPolicyVersion = selection.PolicyVersion,
                StageModelOverrides = ContentModelPolicy.ProducerOverridesForRequest(
                    wc.GenerationBrief, selection, "complete", wc.JobModelPolicyOverride),
                RequestedModel = selection.EffectiveModel,
                RequireCiteable = true,
            },
            ct);
        stopwatch.Stop();
        var content = response.Content ?? response.Variations?.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException($"Citeable RAG returned no content for {wc.Job.ContentType}.");

        var evidenceIds = (response.Citations ?? []).Select(c => c.PageId)
            .Where(id => !string.IsNullOrWhiteSpace(id)).Cast<string>()
            .Concat(response.Sources.Select(s => s.PageId).Where(id => !string.IsNullOrWhiteSpace(id)).Cast<string>())
            .Concat(response.Provenance?.EvidenceIds ?? [])
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var provenance = new GccV2GenerationProvenance(
            wc.GenerationBrief.Version, selection.PolicyVersion, response.PromptVersion,
            selection.RequestedModel, response.ModelUsed, response.RetrievalMode, evidenceIds,
            response.Warnings.Concat(response.EvidenceWarnings).Distinct().ToList(),
            stopwatch.ElapsedMilliseconds, selection);
        var section = MarkdownToSection(content, heading);
        var write = new GccV2WriteSection(
            sectionKey, heading, "problem", section, false, response.Citations ?? [], provenance, response.Sources);
        await PersistAndEmitAsync(wc, ownerUserId, "write", "SectionDrafted", write, 0, ct);
        return new GccV2WriteOutput
        {
            Title = wc.GenerationBrief.Title,
            MetaDescription = Truncate(content, 160),
            Lede = write,
            Sections = [],
            TokensUsed = 0,
            Citations = write.Citations ?? [],
            Provenance = [provenance],
            Sources = response.Sources,
        };
    }

    /// <summary>Writes the stable image-prompt draft that the shared RAG validation stage reviews.</summary>
    private async Task<GccV2WriteOutput> WriteImagePromptAsync(GccV2WriteContext wc, Guid ownerUserId, CancellationToken ct)
    {
        var topic = Capitalize(wc.BaseContext.TargetKeyword);
        var sectionMeta = GccV2ImagePromptSpawnService.ParseImagePromptSection(wc.Brief.RawBriefJson);
        var displayTitle = topic;
        string? sourceContext = wc.BaseContext.WritingNotes;

        if (sectionMeta is not null)
        {
            displayTitle = string.IsNullOrWhiteSpace(sectionMeta.Heading) ? topic : sectionMeta.Heading;
            var sourceJob = await _repo.GetJobAsync(sectionMeta.SourceJobId, ct)
                ?? throw new InvalidOperationException($"Source job {sectionMeta.SourceJobId} not found for image-prompt.");
            var sourcePayload = DeserializeJobResult(sourceJob.ResultJson);
            sourceContext = JsonSerializer.Serialize(new
            {
                sourceType = sectionMeta.SourceType,
                sourceHeading = sectionMeta.Heading,
                sourceOrder = sectionMeta.Order,
                title = sourcePayload.Title,
                metaDescription = sourcePayload.MetaDescription,
                body = sourcePayload.Document is null ? null : ContentDocumentText.Flatten(sourcePayload.Document),
                writingNotes = wc.BaseContext.WritingNotes,
            }, ContentDocJson);
        }

        return await WriteRagCompleteAsync(
            wc,
            ownerUserId,
            "image-prompt",
            displayTitle,
            ct,
            sourceContext);
    }

    private static JobResultSnapshot DeserializeJobResult(string? resultJson)
    {
        if (string.IsNullOrWhiteSpace(resultJson))
            throw new InvalidOperationException("Source job has no completed result.");
        try
        {
            return JsonSerializer.Deserialize<JobResultSnapshot>(resultJson, ContentDocJson)
                ?? throw new InvalidOperationException("Source job result could not be parsed.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Source job result could not be parsed.", ex);
        }
    }

    private sealed record JobResultSnapshot(string? Title, string? MetaDescription, ContentDocument? Document);

    /// <summary>Unknown content types fail the job — no stub drafts.</summary>
    private Task<GccV2WriteOutput> WriteStubAsync(GccV2WriteContext wc, Guid ownerUserId, string contentType, CancellationToken ct) =>
        throw new InvalidOperationException($"Unsupported content type for WRITE: {contentType}.");

    private async Task<(GccV2WriteSection Write, int Tokens)> DraftOutlineSectionAsync(
        GccV2WriteContext wc,
        Guid ownerUserId,
        GccV2OutlineSection entry,
        int index,
        int totalCount,
        IReadOnlyList<string> allHeadings,
        ArticleMetadataDraft metadata,
        CancellationToken ct)
    {
        var label = wc.Job.ContentType ?? "article";
        GccV2WriteSection write;
        try
        {
            var completedSummaries = await LoadCompletedSectionSummariesAsync(wc.Job.Id, ct);
            write = await GenerateRagSectionAsync(
                wc, entry, allHeadings,
                completedSectionSummaries: completedSummaries,
                ContentGenerationStage.Section, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Label} section \"{Heading}\" generation failed for job {JobId}.", label, entry.Heading, wc.Job.Id);
            throw;
        }

        await PersistAndEmitAsync(wc, ownerUserId, "write", "SectionDrafted", write, 0, ct);
        return (write, 0);
    }

    private async Task<IReadOnlyList<string>> LoadCompletedSectionSummariesAsync(Guid jobId, CancellationToken ct)
    {
        var results = await _repo.GetStageResultsAsync(jobId, ct);
        var summaries = new List<string>();
        foreach (var result in results.Where(r => r.Stage == "write").OrderBy(r => r.CompletedAtUtc))
        {
            try
            {
                var payload = JsonSerializer.Deserialize<StageSectionPayload>(result.OutputJson, ContentDocJson);
                if (payload?.Section is null) continue;
                var text = string.Join(" ", payload.Section.Paragraphs.OfType<TextParagraph>()
                    .SelectMany(p => p.Runs).Select(r => r.Text));
                if (!string.IsNullOrWhiteSpace(text))
                    summaries.Add($"{payload.Section.Heading}: {Truncate(text, 300)}");
            }
            catch (JsonException) { }
        }
        return summaries.TakeLast(12).ToList();
    }

    private async Task<GccV2WriteSection> GenerateRagSectionAsync(
        GccV2WriteContext wc,
        GccV2OutlineSection entry,
        IReadOnlyList<string> allHeadings,
        IReadOnlyList<string> completedSectionSummaries,
        ContentGenerationStage stage,
        CancellationToken ct)
    {
        var route = GccV2ContentTypeRagMapper.Map(wc.Job.ContentType);
        var selection = _modelPolicy.Select(stage, wc.GenerationBrief, wc.JobModelPolicyOverride);
        var stopwatch = Stopwatch.StartNew();
        var response = await _rag.GenerateAsync(
            wc.Job.OwnerUserId,
            new RagGenerateRequest
            {
                WritingIntent = route.WritingIntent,
                Topic = $"{wc.GenerationBrief.Title}: {wc.GenerationBrief.TargetKeyword}",
                TargetEntities = wc.GenerationBrief.TargetEntities.ToList(),
                GenerationStage = "section",
                Outline = wc.Outline.Sections.Select(s => new RagOutlineSectionDto
                {
                    Key = s.Key,
                    Heading = s.Heading,
                    Brief = BuildSectionBrief(s),
                    EvidenceIds = s.EvidenceIds ?? [],
                }).ToList(),
                SectionKey = entry.Key,
                SectionHeading = entry.Heading,
                SectionBrief = BuildSectionBrief(entry),
                CompletedSectionSummaries = completedSectionSummaries.ToList(),
                CanonicalBrief = wc.GenerationBrief.ToCanonicalBrief(),
                ModelPolicyPreset = ContentModelPolicy.PresetValue(selection.Preset),
                ModelPolicyVersion = selection.PolicyVersion,
                StageModelOverrides = ContentModelPolicy.ProducerOverridesForRequest(
                    wc.GenerationBrief, selection, "section", wc.JobModelPolicyOverride),
                RequestedModel = selection.EffectiveModel,
                RequireCiteable = true,
            },
            ct);
        stopwatch.Stop();
        if (string.IsNullOrWhiteSpace(response.Content))
            throw new InvalidOperationException($"Citeable RAG returned no content for section '{entry.Heading}'.");

        var section = MarkdownToSection(response.Content, entry.Heading);
        var evidenceIds = (response.Citations ?? []).Select(c => c.PageId)
            .Where(id => !string.IsNullOrWhiteSpace(id)).Cast<string>()
            .Concat(response.Sources.Select(s => s.PageId).Where(id => !string.IsNullOrWhiteSpace(id)).Cast<string>())
            .Concat(response.Provenance?.EvidenceIds ?? [])
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var provenance = new GccV2GenerationProvenance(
            wc.GenerationBrief.Version,
            selection.PolicyVersion,
            response.PromptVersion,
            selection.RequestedModel,
            response.ModelUsed,
            response.RetrievalMode,
            evidenceIds,
            response.Warnings.Concat(response.EvidenceWarnings).Distinct().ToList(),
            stopwatch.ElapsedMilliseconds,
            selection);
        return new GccV2WriteSection(
            entry.Key, entry.Heading, entry.Job, section, false, response.Citations ?? [], provenance, response.Sources);
    }

    private static string BuildSectionBrief(GccV2OutlineSection entry)
    {
        var mustMention = entry.HierarchyChildHeadings.Count == 0
            ? ""
            : $" Must cover: {string.Join("; ", entry.HierarchyChildHeadings)}.";
        return $"{entry.Brief ?? $"Section purpose: {entry.Job ?? "advance"}."}{mustMention}";
    }

    internal static Section MarkdownToSection(string markdown, string heading)
    {
        var paragraphs = markdown.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(block => string.Join(" ", block.Split('\n')
                .Select(line => line.Trim())
                .Where(line => !line.StartsWith('#'))
                .Select(line => line.TrimStart('-', '*', ' '))))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => (Paragraph)new TextParagraph([new Run(text)]))
            .ToList();
        if (paragraphs.Count == 0)
            paragraphs.Add(new TextParagraph([new Run(markdown.Trim())]));
        return new Section("h2", heading, paragraphs, null, []);
    }

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
            _logger.LogError(ex, "Pillar metadata generation failed for job {JobId}.", wc.Job.Id);
            throw;
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
            _logger.LogError(ex, "Blog metadata generation failed for job {JobId}.", wc.Job.Id);
            throw;
        }
    }

    private async Task PersistAndEmitAsync(
        GccV2WriteContext wc, Guid ownerUserId, string stage, string eventType, GccV2WriteSection write, int tokens, CancellationToken ct)
    {
        var jobId = wc.Job.Id;
        var stagePayload = new
        {
            heading = write.Heading,
            job = write.Job,
            section = write.Section,
            usedFallbackStub = write.UsedFallbackStub,
            citations = write.Citations,
            provenance = write.Provenance,
            sources = write.Sources,
        };
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
            citations = write.Citations,
            provenance = write.Provenance,
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
                    s.HierarchyChildHeadings ?? [],
                    s.Brief,
                    s.EvidenceIds))
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

    private async Task<Dictionary<string, SectionMeta>> LoadLatestSectionMetaAsync(Guid jobId, CancellationToken ct)
    {
        var results = await _repo.GetStageResultsAsync(jobId, ct);
        var map = new Dictionary<string, SectionMeta>(StringComparer.OrdinalIgnoreCase);
        foreach (var result in results
                     .Where(r => (r.Stage is "write" or "repair" or "canvas" or "final-synthesis")
                                 && !string.IsNullOrWhiteSpace(r.SectionKey))
                     .OrderByDescending(r => r.CompletedAtUtc))
        {
            if (map.ContainsKey(result.SectionKey!)) continue;
            try
            {
                var payload = JsonSerializer.Deserialize<StageSectionPayload>(result.OutputJson, ContentDocJson);
                if (payload?.Section is null) continue;
                map[result.SectionKey!] = new SectionMeta(
                    payload.Heading ?? payload.Section.Heading,
                    payload.Job,
                    payload.UsedFallbackStub ?? false,
                    payload.Section,
                    payload.Citations,
                    payload.Provenance,
                    payload.Sources);
            }
            catch (JsonException)
            {
                // skip malformed stage payloads
            }
        }

        return map;
    }

    private sealed record StageSectionPayload(
        string? Heading,
        string? Job,
        Section? Section,
        bool? UsedFallbackStub,
        IReadOnlyList<RagCitationDto>? Citations = null,
        GccV2GenerationProvenance? Provenance = null,
        IReadOnlyList<RagGenerateSourceDto>? Sources = null);

    private sealed record SectionMeta(
        string Heading,
        string? Job,
        bool UsedFallbackStub,
        Section? Section,
        IReadOnlyList<RagCitationDto>? Citations,
        GccV2GenerationProvenance? Provenance,
        IReadOnlyList<RagGenerateSourceDto>? Sources);

    private sealed record JobResultPayload(string? Title, string? MetaDescription, ContentDocument? Document);

    private sealed record OutlineJsonShape(List<OutlineSectionJsonShape>? Sections, List<string>? HierarchyChildHeadings);

    private sealed record OutlineSectionJsonShape(
        string? Key,
        string? Heading,
        string? Job,
        List<string>? HierarchyChildHeadings,
        string? Brief = null,
        IReadOnlyList<string>? EvidenceIds = null);
}
