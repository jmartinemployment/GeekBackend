using System.Text.Json;
using GeekAPI.Services.ContentCreator.ContentTypes;
using GeekAPI.Services.ContentCreator.Guardrail;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.DTOs;
using GeekApplication.Interfaces.ContentWriterV3;
using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.ContentCreator;

// "PDF / LinkedIn document" (plans/enable-pdf.md): the deck's writer, beside the Pillar's and following
// its sequence with the same pieces. A part of the one GccGenerateService, like the Pillar's file.
//
// Two requests, the cover with the post and then every slide between the cover and the last. Each goes
// through RequestThatFitsAsync with the page's whole research, counted before it is sent: the one way
// every type receives research (Jeff, 2026-10-12). Code builds the last slide, makes the page's
// document from the replies, runs every check once, and lists the links for the first comment.
//
// Not done for a deck: the keyword remap and the keyword checks (a deck is not scored for search), an
// FAQ, image prompts, a meta description, structured data, a canonical address, a link placed in the
// text (GccToolLinker is not run: LinkedIn does not make a link inside a document clickable).
public partial class GccGenerateService
{
    /// <summary>
    /// Why a deck cannot be written for this project's partners, or null when it can
    /// (<see cref="GccLinkedInDocument.PartnerRefusal"/>). Asked by the run before a page of the
    /// project is deleted, and by this writer before a request is built: one rule in both places.
    /// </summary>
    public async Task<string?> LinkedInDocumentRefusalAsync(GccCreateDto create, CancellationToken ct) =>
        GccLinkedInDocument.PartnerRefusal(
            GccRequiredToolMentions.For(create.BriefJson, await PartnerUrlsForAsync(create, ct)));

    /// <summary>
    /// A deck and the LinkedIn post that carries it, as the envelope a page is saved in:
    /// <c>{ title, caption, firstComment, links, slides, warnings, body }</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>body</c> is the document every page is (<see cref="ContentDocument"/>): the cover is the
    /// opening and each slide a top-level section whose heading is the slide's title. <c>slides</c>
    /// says what each is, in order, so the PDF and the form need not infer it from position.
    /// <c>caption</c> is the post, <c>firstComment</c> the links as text to paste under it, and
    /// <c>links</c> the same links one by one.
    /// </para>
    /// <para>
    /// A deck that fails a check is refused on that attempt and nothing is sent again (Jeff,
    /// 2026-10-06). What it ships with instead is in <c>warnings</c>.
    /// </para>
    /// </remarks>
    public async Task<string> GenerateLinkedInDocumentAsync(
        GccCreateDto create,
        SiteSectionContextDto? section,
        ContentGeneratorProvider provider,
        string? mustMentionBlock,
        CancellationToken ct)
    {
        var label = $"the {GccLinkedInDocument.Label}";
        var partnerUrls = await PartnerUrlsForAsync(create, ct);
        // Before anything is resolved or paid for: a deck has a slide for each partner.
        if (GccLinkedInDocument.PartnerRefusal(GccRequiredToolMentions.For(create.BriefJson, partnerUrls)) is { } partnerRefusal)
        {
            throw new InvalidOperationException(partnerRefusal);
        }

        var llm = GetLlm(provider);
        var competitorAnalyses = await ResolveCompetitorAnalysesAsync(create, ct);
        var partnerTools = await PartnerToolsAsync(create, ct);
        var partners = partnerTools.Required;
        // The context every long page is written from, with three things left out: the tools a page
        // links, a deck linking nothing; the Brief's length band, which is a page's, a deck's size
        // being its slides; and the note on weaving a four-phase method through a narrative.
        var context = BuildPillarContext(
            await _publisherProfile.ResolveAsync(create.ProjectId, ct),
            knownTools: [],
            create, section, mustMentionBlock, provider,
            withConsultantAppendix: false) with { LengthBand = null };
        await RecordCompetitorPagesWithoutOutlineAsync(label, create, competitorAnalyses);

        var deckType = RequireType(GccChannelTypes.LinkedInDocument);
        var promptCtx = new ContentTypePromptContext(
            context,
            NicheFraming: GccNicheFramingReader.ForEveryPartner(create.BriefJson, partnerTools.FailuresAreUnderThePartners),
            Partners: partners,
            PartnerAnswers: partnerTools.Answers);
        // The slides a request writes: the problem, one for each partner, the approach.
        var assigned = deckType.OutlineFor(promptCtx);

        // What each request reads, with all of the research or with what gave way left out of it. The
        // slides read what a page's body reads: the partner research, the publisher's positions, the
        // competitor pages with the outline of each, and the site's own coverage. The competitor
        // outlines are worded without the rule for tagging a heading, a slide carrying no tag. The
        // cover and the post read what a page's opening reads: the research and the publisher's
        // positions, and no competitor page.
        string SlidesEvidenceWith(GccRequestCarries carries) =>
            BuildEvidenceBlock(create, competitorAnalyses, partnerTools.Unlisted, carries, tagged: false);
        string CoverEvidenceWith(GccRequestCarries carries) =>
            WithPublisherPositions(BuildResearchBlock(create, carries.Passages), create, partnerTools.Unlisted);
        // All of the research: what a request carries when nothing gives way, and what the deck's
        // checks read whatever a request carried. The cover's evidence is a part of the slides'.
        var slidesEvidence = SlidesEvidenceWith(GccRequestCarries.Everything);
        var foreignAmounts = GccCurrencyGrammar.ForeignAmountsInstruction(slidesEvidence);

        var research = new GccResearchPerRequest(
            GccPageResearch.PagesOf(GccResearchFetchService.Deserialize(create.ResearchJson)), partnerUrls);
        await GccRunLog.RecordIfAnyAsync("evidence-labels", research.LabelsRecord());

        // (1) The cover and the post, one request.
        var coverResult = await llm.CompleteAsync(
            await RequestThatFitsAsync(
                llm,
                research,
                // The cover opens on the problem, so the passages about it are taken first when not
                // all of the research fits.
                GccResearchPerRequest.LabelsOf([.. assigned.Take(1)]),
                "LinkedIn document cover and post",
                call: 0,
                of: 1,
                // It shows no competitor page, so only its partner research can give way.
                carries => deckType.Lede(promptCtx with
                {
                    EvidenceBlock = WithForeignAmountsNamed(CoverEvidenceWith(carries), foreignAmounts),
                })),
            ct);
        var coverAndPost = GccLinkedInDocument.ReadCoverAndPost(coverResult.Content);

        // (2) Every slide between the cover and the last, one request. It is shown the cover without
        // the em dashes code takes out of the deck, so it continues the cover the deck will carry.
        var coverAsShown = GccLinkedInDocument.CoverSection(new GccLinkedInDocument.CoverReply(
            GccEmDash.Clean(coverAndPost.Cover.Title).Text,
            GccEmDash.Clean(coverAndPost.Cover.Line).Text));
        var slidesResult = await llm.CompleteAsync(
            await RequestThatFitsAsync(
                llm,
                research,
                GccResearchPerRequest.LabelsOf(assigned),
                "LinkedIn document slides",
                call: 1,
                of: 1,
                carries => deckType.Body(promptCtx with
                {
                    EvidenceBlock = WithForeignAmountsNamed(SlidesEvidenceWith(carries), foreignAmounts),
                    Lede = coverAsShown,
                    SectionBatch = assigned,
                }),
                CompetitorPagesThatGiveWay(create)),
            ct);
        var written = GccLinkedInDocument.ReadSlides(slidesResult.Content, assigned.Count);

        // (3) The last slide, built by code. The writer was never shown the questions.
        var next = GccLinkedInDocument.NextSlide(_company, GccNicheFramingReader.DiagnosisQuestions(create.BriefJson));
        IReadOnlyList<GccDeckSlidePlan> slides = next is null
            ? [.. GccLinkedInDocument.Slides(partners).Where(slide => !slide.BuiltByCode)]
            : GccLinkedInDocument.Slides(partners);

        var guardInputs = GuardInputsFor(
            create,
            context,
            GccChannelTypes.LinkedInDocument,
            provenance: null,
            requiredTools: partners,
            // What the operator entered under each partner is in the Brief, which the figure and
            // currency checks read whole as the operator's own statements.
            evidenceText: slidesEvidence);

        // (4) Every check, once. The post goes through them with the deck, as one more section, so the
        // em dash, a sentence with a number the research does not hold and money that is not in US
        // dollars are handled in it exactly as they are on a slide.
        Task<GccDraft> WriteDeckAsync()
        {
            var sections = written.Select(GccLinkedInDocument.SlideSection).ToList();
            if (next is not null) sections.Add(next);
            var whole = GccLinkedInDocument.WithPost(
                new ContentDocument(GccLinkedInDocument.CoverSection(coverAndPost.Cover), sections),
                coverAndPost.Post);
            return Task.FromResult(new GccDraft(ContentGuardrail.Apply(whole).Document, [], []));
        }

        var (checkedDocument, warnings) = await GuardedDraftAsync(
            label,
            WriteDeckAsync,
            document => GccDraftGuard.LinkedInDocument(document, guardInputs, slides),
            type: GccLinkedInDocument.Label,
            numberEvidence: guardInputs.NumberEvidence);
        var (deck, post) = GccLinkedInDocument.SplitPost(checkedDocument, slides.Count);
        if (GccDraftGuard.PostLength(post) is { } tooLong) warnings.Add(tooLong);

        // (5) The links, for the first comment: the booking link and each partner's tool page on the
        // publisher's site. Whether the project has each of those pages is known when the run is over,
        // and a page it does not have is named then (GccGenerationCoordinator.NameMissingToolPages).
        var links = GccLinkedInDocument.Links(
            _company, GccPartnerToolPages.For(create, partnerUrls, _company.ToolBaseUrl));
        // What a slide holds against the deck's numbers, on the run's record and not in the operator's
        // list: a line a word long is not something to act on. See GccDraftGuard.SlideSizes.
        await GccRunLog.RecordIfAnyAsync("deck", new
        {
            label,
            slides = GccDraftGuard.SlideSizes(deck, slides),
            postWords = GccDraftGuard.WordCount(post),
            postCharacters = post.Length,
            links = links.Select(link => new { link.Label, link.Url }).ToList(),
        });

        return JsonSerializer.Serialize(new
        {
            title = deck.Lede.Heading,
            caption = post,
            firstComment = GccLinkedInDocument.FirstComment(links),
            links = links.Select(link => new { label = link.Label, href = link.Href, url = link.Url }).ToList(),
            slides = slides
                .Select(slide => new { kind = slide.Kind.ToString().ToLowerInvariant(), partner = slide.Partner })
                .ToList(),
            warnings,
            body = deck,
        }, CwDocumentJson);
    }
}
