using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services;
using GeekAPI.Services.Workflow.Services.PromptBuilders;
using GeekAPI.Services.Workflow.Services.SchemaBuilders;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.ContentCreator.Guardrail;
using GeekAPI.Services.Gcw;
using GeekApplication.Interfaces.ContentWriterV3;
using GeekApplication.Models.ContentCreator;
using Microsoft.Extensions.Options;

using GeekAPI.Services.ContentCreator;

using GeekAPI.HttpClients;

namespace GeekAPI.Services.ContentCreator;

// Pillar Write -- single-source-of-responsibility extraction, step 2
// (plans/single-source-of-responsibility.md, section 6). A pure, behaviour-preserving
// move: GeneratePillarBodyAsync is physically relocated out of GccGenerateService.cs
// into this file via a partial-class split, with zero change to its body, its signature,
// or any caller. GccGenerateService stays one compiled type -- the coordinator,
// GccGenerationCoordinator, GccController and every existing test that references
// GccGenerateService see no difference at all.
//
// BuildPillarContext stays behind in GccGenerateService.cs despite its name:
// GenerateBlogBodyAsync calls it too (GccGenerateService.cs, ~2808), so it is shared
// Pillar+Blog setup, not Pillar-only, and moving it here would misrepresent this file as
// owning something Blog still needs. See the plan's 2026-10-09 progress note (section 6
// step 2) for this and the other cross-pipeline findings from this extraction.
public partial class GccGenerateService
{
    /// <summary>
    /// A pillar body as a <see cref="ContentDocument"/>, serialized. Never markup: the model
    /// returns typed sections, the document holds the structure, and tag characters are produced
    /// only by <c>SectionHtmlRenderer</c> at export.
    /// </summary>
    /// <remarks>
    /// This replaced a prompt that asked for a prose body and a caller that read structure back out
    /// of the string. The lede goes through <c>BuildPillarLedePrompt</c> so lede-type guidance
    /// applies here as it does on the orchestrator path.
    /// </remarks>
    public async Task<string> GeneratePillarBodyAsync(
        GccCreateDto create,
        SiteSectionContextDto? section,
        ContentGeneratorProvider provider,
        string? mustMentionBlock,
        CancellationToken ct)
    {
        var llm = GetLlm(provider);
        var competitorAnalyses = await ResolveCompetitorAnalysesAsync(create, ct);
        var partnerTools = await PartnerToolsAsync(create, ct);
        var context = BuildPillarContext(
            await _publisherProfile.ResolveAsync(create.ProjectId, ct),
            partnerTools.Linked,
            create, section, mustMentionBlock, provider);
        var evidence = BuildProvenanceEvidence(
            create, competitorAnalyses, mustMentionBlock, await PartnerUrlsForAsync(create, ct));
        var evidenceBlock = BuildEvidenceBlock(create, competitorAnalyses, partnerTools.Unlisted);
        // Prompts come from the type's own set, not from a switch over a flat builder -- see
        // content-creator-v2/plans/prompts-per-content-type.md.
        var pillarType = RequireType("pillar");
        // The operator's framing of the category: what the pillar argues from. Until 2026-10-06 only
        // the tool page received it, and the pillar's slots were filled from retrieved prose instead.
        var outlineCtx = new ContentTypes.ContentTypePromptContext(
            context, NicheFraming: GccNicheFramingReader.ForCategory(create.BriefJson));
        var metadata = new ArticleMetadataDraft(
            Title: create.Topic.Trim(),
            MetaDescription: Truncate((create.Notes ?? create.Topic).Trim(), 160),
            Keywords: [create.Topic.Trim()],
            SectionOutline: [.. pillarType.OutlineFor(outlineCtx).Select(sl => sl.Label)]);
        // The opening gets the retrieved evidence too. It did not until 2026-09-29: evidenceBlock
        // was built here and handed only to the body, so the lede and introduction -- the most-read
        // paragraphs on the page, and the ones that set every factual claim after them -- were
        // written from brief text alone while the evidence sat in a local three lines above. The
        // pillar lede prompt has always told the model "a number may appear only if it is in the
        // supplied evidence or published by this publisher"; no evidence was supplied, so that rule
        // could not be met or broken.
        //
        // The research half, not the whole block: see BuildPillarLedePrompt for why the competitor
        // headings stay out of a prompt that states no provenance rules.
        var ledeEvidence = WithPublisherPositions(BuildResearchBlock(create), create, partnerTools.Unlisted);
        // The amounts in this page's evidence that are not in US dollars, named for the opening and
        // for each part of the body.
        var pillarForeignAmounts = Guardrail.GccCurrencyGrammar.ForeignAmountsInstruction(
            $"{evidenceBlock}{Environment.NewLine}{ledeEvidence}");
        var pillarPromptCtx = outlineCtx with
        {
            Metadata = metadata,
            EvidenceBlock = WithForeignAmountsNamed(ledeEvidence, pillarForeignAmounts),
        };
        // BuildPillarLedePrompt asks for LedeAndIntroductionJsonContract -- {"lede": {...},
        // "introduction": {...}} -- so it must be read with ParseLedeAndIntroduction, the way
        // ContentGenerationOrchestrator reads the same prompt. Reading it as a sections array threw
        // "Model did not return a valid sections array for pillar lede" on every single pillar
        // generation, while the model was in fact complying exactly (Jeff, 2026-09-23, whose error
        // carried a perfectly good directAddress hook that this then discarded).
        var ledeResult = await llm.CompleteAsync(pillarType.Lede(pillarPromptCtx), ct);
        var (pillarLede, _, pillarIntroduction) =
            LlmResponseJsonParser.ParseLedeAndIntroduction(ledeResult.Content, "pillar lede");

        // The partner tools this page is obliged to name, stated to the model and checked against
        // the result below -- one list, so the instruction and the check cannot disagree.
        var requiredTools = partnerTools.Required;
        var toolInstruction = partnerTools.Instruction;
        // The valid tags are stated up front: with no retry to name them after a refusal, the first
        // attempt has to be told what resolves.
        var pillarEvidence = string.Join(
            Environment.NewLine,
            new[] { evidenceBlock, toolInstruction, GccHeadingProvenanceGuard.LicensedValues(evidence) }
                .Where(b => !string.IsNullOrWhiteSpace(b)));

        // In batches. One response cannot hold a 3,000-word floor in this JSON -- see
        // SectionsPerBatch -- so asking for the whole page in one call capped it by arithmetic.
        var pillarOutline = pillarType.OutlineFor(outlineCtx);

        // The People Also Ask section, written before the body and carried onto the draft. It depends
        // on the brief's questions, not on the body.
        //
        // The brief's PAA questions were parsed (ExtractBriefFields) and then silently dropped --
        // never fed to an FAQ section anywhere on this path -- until Stage 8c. Not "cluster PAA
        // again at generation time" (the questions are already operator-curated, by SerpIngestPanel's
        // own selection UI, before they ever reach BriefJson); just stop discarding them.
        Section? pillarFaq = null;
        var paaQuestions = ExtractBriefFields(create.BriefJson).PaaQuestions;
        if (paaQuestions is { Count: > 0 })
        {
            pillarFaq = await WriteFaqInBatchesAsync(
                llm,
                batch => _prompts.BuildArticleFaqSectionPrompt(context, metadata, batch, isRegeneration: false),
                paaQuestions,
                "the pillar's People Also Ask",
                ct);
        }

        // The lede IS the first H2, and the introduction is the lede continuing under its heading.
        // Always merged, never conditional. This used to compare the two headings and insert the
        // introduction as a separate first section when they differed -- so whether a reader got one
        // opening or two came down to whether the model happened to return matching strings. Jeff,
        // 2026-09-23: "This just feels wrong". The introduction carries no heading, so there is
        // nothing to compare and nothing to decide.
        var lede = pillarLede with { Tag = "h2" };
        lede = lede with
        {
            Paragraphs = [.. lede.Paragraphs, .. pillarIntroduction.Paragraphs],
            Children = [.. lede.Children, .. pillarIntroduction.Children],
        };

        var pillarGuardInputs = GuardInputsFor(
            create,
            context,
            evidence,
            requiredTools,
            pillarEvidence,
            appendedSections: pillarFaq is null ? 0 : 1,
            partnerTools: partnerTools);

        // Every check, once -- see GccDraftGuard. The FAQ is part of the document, so it is checked for
        // links, figures and quotations like the body is.
        async Task<GccDraft> WritePillarDraftAsync()
        {
            var shortfalls = new List<string>();
            var sections = await GenerateSectionsInBatchesAsync(
                llm,
                pillarType,
                pillarPromptCtx with
                {
                    EvidenceBlock = WithForeignAmountsNamed(pillarEvidence, pillarForeignAmounts),
                    Lede = pillarLede,
                },
                [.. pillarOutline.Skip(1)],
                "Pillar body",
                ct,
                shortfalls);
            // Partner tool pages on the body alone, then the page's own closing, then the section
            // written outside the outline: neither of the last two can carry a tool link.
            sections = await LinkToolsAsync("the pillar", sections, pillarPromptCtx.Context.KnownCrawlTools ?? []);
            sections = GccClosing.AppendTo(sections, ClosingFor(create));
            if (pillarFaq is not null) sections.Add(pillarFaq);
            var whole = new ContentDocument(lede, sections);
            return new GccDraft(ContentGuardrail.Apply(whole).Document, shortfalls);
        }

        var (document, pillarWarnings) = await GuardedDraftAsync(
            "the pillar", WritePillarDraftAsync, doc => Guardrail.GccDraftGuard.Pillar(doc, pillarGuardInputs));

        // Image prompts attach here rather than in the caller, matching Tool and Blog -- the caller
        // ran them over the returned JSON, which only worked while this returned a bare document.
        document = await WithSectionImagePromptsAsync(
            "pillar", create.Topic, document, section, provider, pillarWarnings, ct);

        // Title, standfirst, meta description and TechArticle JSON-LD. v1's orchestrator produced
        // all of it for a pillar; the Create reimplementation returned a bare document, leaving
        // BuildArticleMetadataPrompt and ArticleSchemaBuilder sitting here with no caller.
        var pillarMetaResult = await llm.CompleteAsync(_prompts.BuildArticleMetadataPrompt(context), ct);
        var pillarMeta = RequireCompleteMetadata(
            LlmResponseJsonParser.Parse<ArticleMetadataDraft>(pillarMetaResult.Content, "pillar metadata"), "pillar metadata");
        var pillarMetaDescription = pillarMeta.MetaDescription.Length > 160
            ? pillarMeta.MetaDescription[..160]
            : pillarMeta.MetaDescription;

        // GccContentPath, which is what GccArtifactExportService writes into the canonical tag for
        // this same artifact. This hand-assembled {base}/{department}/{slug} and the export emits
        // {base}/{department}/{descriptor}/{slug}, so the JSON-LD url and the canonical disagreed
        // on every pillar with a descriptor -- the disagreement GccContentPath exists to prevent.
        var pillarUrl = GccContentPath.For(_company.ArticleBaseUrl, create, Slugify(pillarMeta.Title));
        var pillarSchemaMeta = ContentMetadataFactory.For(
            context, pillarMeta.Title, pillarMetaDescription, pillarUrl, pillarMeta.Keywords, document);

        return JsonSerializer.Serialize(new
        {
            title = pillarMeta.Title,
            metaDescription = pillarMetaDescription,
            summary = pillarMeta.Summary,
            // What the draft ships with that the operator should see: a partner it never named, a
            // closing that never linked the scheduler. Read by GccGenerationCoordinator and pushed
            // to the workspace; ignored by GccBodyEnvelope.Read, so revise and export are unaffected.
            warnings = pillarWarnings,
            body = document,
            // No companion blog exists on this path, so there is nothing to cite as related -- an
            // invented URL would be a claim about a page that does not exist.
            jsonLdSchema = _articleSchema.Build(pillarSchemaMeta, relatedBlogPostUrl: string.Empty),
        }, CwDocumentJson);
    }
}
