using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using GeekAPI.Services.ContentCreatorV2.Generation;
using GeekAPI.Services.Workflow.Providers;
using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.ContentCreatorV2.Competitor;

/// <summary>
/// Competitor extraction — a rival <b>service business</b>, extracted with competitor-native signals.
///
/// This service deliberately does NOT reuse <c>GccV2PartnerExtractionService</c>. The previous
/// implementation ran the partner extractor over competitor pages using <c>partnerToolNames</c> as its
/// search vocabulary and relabelled the result, which made every downstream competitor job either
/// SaaS-shaped or blind (plans/rag-foundation-rewrite.md §3.1).
///
/// Extraction is schema-constrained (<see cref="IGccV2SchemaConstrainedGenerator"/>) rather than regex:
/// the signals that matter here — coverage depth, positioning, specialisms, claim risk — are semantic,
/// and a pattern matcher cannot identify them. Every asset carries the exact source quote so
/// <c>GccV2CompetitorExtractionVerify</c> can check it against source Markdown and stamp
/// <c>MarkdownVerified</c>; the downstream citation and claim-risk gates decide what may ship.
///
/// Fail-closed and silent per repo rules: a page that yields nothing usable contributes nothing. No
/// fallback path, no partner-shaped substitute, no fabricated fields.
/// </summary>
public sealed class GccV2CompetitorExtractionService(
    IGccV2SchemaConstrainedGenerator generator,
    IContentProviderFactory providers,
    ILogger<GccV2CompetitorExtractionService> logger)
{
    /// <summary>Sent as response_format.json_schema.name. OpenAI rejects anything outside
    /// [a-zA-Z0-9_-] with a 400, so this must not pick up the dotted ".v4" version style.</summary>
    public const string ProviderSchemaName = "competitor-extraction-v4";

    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
            // Explicit resolver required: JsonSchemaExporter marks the options read-only, and
            // reflection-based resolution is not picked up implicitly - without this every call
            // throws "must specify a TypeInfoResolver setting before being marked as read-only".
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };

    private const int MaxParagraphsPerPage = 40;
    private const int MaxParagraphChars = 1_200;

    private const string SystemPrompt = """
        You extract structured intelligence about a COMPETITOR from one crawled web page.

        A competitor competes for the operator's clients or for the same search attention. It is NEVER a
        product: software is a PARTNER to recommend and implement, never a rival. There are two kinds.

          CONSULTANCY  - a rival professional services firm competing for the same engagements
                         (schema.org Organization / ProfessionalService).
          CONTENT      - a publisher, review site or directory competing for the same search results.
                         Media, not a service business.

        Never apply software metrics to either: no seat tiers, no per-seat pricing, no billing cycles,
        no overage terms, no feature matrices, no API rate limits, no version numbers. Those describe
        partner products, not competitors.

        A CONSULTANCY fills serviceOfferings, clients, presence, credentials and positioning.
        A CONTENT competitor fills mediaProfile and coverage. Do not force a publisher through the
        consultancy fields or a consultancy through mediaProfile — leave what does not apply empty.

        Consultancies rarely publish rates. Do not record, estimate or infer pricing for a competitor.

        Extract only what this page states. Omit any field the page does not support. Never infer,
        never generalise from industry knowledge, never fill a gap with a plausible value. Returning an
        empty list is correct and expected when the page does not cover that signal.

        Every item must carry "quote": the exact verbatim span from the page that supports it, copied
        character-for-character. An item whose quote is not literally present on the page is invalid.

        Deficits and boundaries — the highest-risk output, because these get published next to a named
        company. A deficit is admissible ONLY when the page states a boundary in its own words, for
        example "we work exclusively with enterprise clients" or "serving the UK only". The ABSENCE of a
        mention is NOT a deficit: a firm that does not list a service may still offer it. If you cannot
        quote the stated boundary, return no deficit. Never write "they do not offer X" from silence.

        competitorType must be exactly one of:
          "direct"  - sells the same services to the same buyers
          "content" - does not sell these services but competes for the same search attention
                      (publishers, directories, review sites)
          "both"    - sells the services and publishes competing editorial
        """;

    public static GccCompetitorExtractionDocument EmptyDocument() =>
        new(GccCompetitorExtractionDocument.CurrentExtractorVersion,
            [], [], [], [], [], [], [], [], [], [], [], [], [], [], [], [], []);

    /// <summary>
    /// Extract competitor payloads from crawled rival pages. Returns an empty document when nothing
    /// verifiable is found; never throws.
    /// </summary>
    public async Task<GccCompetitorExtractionDocument> ExtractFromPagesAsync(
        IReadOnlyList<GccQuoteablePage> pages,
        IReadOnlyList<string>? competitorSeeds,
        CancellationToken ct)
    {
        if (pages is null || pages.Count == 0) return EmptyDocument();

        IContentGenerationProvider provider;
        try
        {
            provider = providers.GetDefault();
        }
        catch (Exception cause)
        {
            logger.LogWarning(cause, "Competitor extraction skipped: no content provider available.");
            return EmptyDocument();
        }

        var coverage = new List<GccCompetitorCoverageAsset>();
        var gaps = new List<GccCompetitorGapMapAsset>();
        var axes = new List<GccCompetitorComparisonAxisAsset>();
        var deficits = new List<GccCompetitorDeficitRouterAsset>();
        var faqs = new List<GccCompetitorFaqAsset>();
        var proof = new List<GccCompetitorProofAsset>();
        var framing = new List<GccCompetitorFramingAsset>();
        var claimRisks = new List<GccCompetitorClaimRiskAsset>();
        var demand = new List<GccCompetitorDemandSignalAsset>();
        var typeLabels = new List<GccCompetitorTypeLabelAsset>();
        var services = new List<GccCompetitorServiceAsset>();
        var clients = new List<GccCompetitorClientProofAsset>();
        var presence = new List<GccCompetitorPresenceAsset>();
        var credentials = new List<GccCompetitorCredentialAsset>();
        var positioning = new List<GccCompetitorPositioningAsset>();
        var boundaries = new List<GccCompetitorBoundaryAsset>();
        var mediaProfiles = new List<GccCompetitorMediaProfileAsset>();

        var schema = GccV2AdHocJsonSchema.For<CompetitorPageExtraction>(JsonOpts);

        foreach (var page in pages)
        {
            var extraction = await ExtractOnePageAsync(page, competitorSeeds, provider, schema, ct)
                .ConfigureAwait(false);
            if (extraction is null) continue;

            GccCompetitorExtractionProvenance Prov(string? quote) => new(
                OriginProofUrl: page.Url,
                CrawlType: GccCompetitorExtractionDocument.CrawlTypeCompetitor,
                RunId: page.RunId,
                PageId: page.PageId,
                SectionTitle: page.SectionTitle,
                SourceDigest: page.SourceDigest,
                TemporalAnchorUtc: page.CrawledAtUtc,
                Quote: string.IsNullOrWhiteSpace(quote) ? null : quote);

            if (extraction.CompetitorType is { Length: > 0 } type
                && extraction.TypeRationale is { Length: > 0 } rationale)
            {
                typeLabels.Add(new GccCompetitorTypeLabelAsset(
                    type, rationale, extraction.EntityName ?? page.Title, page.Url,
                    Prov(extraction.TypeQuote)));
            }

            foreach (var c in extraction.Coverage ?? [])
            {
                if (string.IsNullOrWhiteSpace(c.TopicPath) || string.IsNullOrWhiteSpace(c.DepthAssessment))
                    continue;
                coverage.Add(new GccCompetitorCoverageAsset(
                    c.TopicPath, c.DepthAssessment, c.EvidenceHeadings ?? [], page.Url, Prov(c.Quote)));
            }

            foreach (var g in extraction.Gaps ?? [])
            {
                if (string.IsNullOrWhiteSpace(g.GapTopic) || string.IsNullOrWhiteSpace(g.OpportunityForUs))
                    continue;
                gaps.Add(new GccCompetitorGapMapAsset(
                    g.GapTopic, g.DepthAssessment ?? "unstated", g.OpportunityForUs, Prov(g.Quote)));
            }

            foreach (var a in extraction.ComparisonAxes ?? [])
            {
                if (string.IsNullOrWhiteSpace(a.AxisId) || string.IsNullOrWhiteSpace(a.RivalCapabilityPayload))
                    continue;
                axes.Add(new GccCompetitorComparisonAxisAsset(
                    a.AxisId, a.AxisLabel ?? a.AxisId, a.RivalCapabilityPayload, page.Url, Prov(a.Quote)));
            }

            foreach (var d in extraction.Deficits ?? [])
            {
                if (string.IsNullOrWhiteSpace(d.AxisId) || string.IsNullOrWhiteSpace(d.TriggerDeficit))
                    continue;
                // RecommendedSwap / PartnerStrengthChunkId are filled by the counterweight join once
                // partner strengths are resolved — never guessed here.
                deficits.Add(new GccCompetitorDeficitRouterAsset(
                    d.AxisId, d.TriggerDeficit, page.Url, [], null,
                    CompetitorDeficitChunkId: page.PageId, PartnerStrengthChunkId: null,
                    Provenance: Prov(d.Quote)));
            }

            foreach (var f in extraction.Faqs ?? [])
            {
                if (string.IsNullOrWhiteSpace(f.Question) || string.IsNullOrWhiteSpace(f.Answer)) continue;
                faqs.Add(new GccCompetitorFaqAsset(f.Question, f.Answer, page.Url, Prov(f.Quote)));
            }

            foreach (var p in extraction.Proof ?? [])
            {
                if (string.IsNullOrWhiteSpace(p.ProofKind) || string.IsNullOrWhiteSpace(p.ProofClaim)) continue;
                proof.Add(new GccCompetitorProofAsset(p.ProofKind, p.ProofClaim, page.Url, Prov(p.Quote)));
            }

            foreach (var f in extraction.Framing ?? [])
            {
                if (string.IsNullOrWhiteSpace(f.FrameType) || string.IsNullOrWhiteSpace(f.FrameExcerpt)) continue;
                framing.Add(new GccCompetitorFramingAsset(
                    f.FrameType, f.FrameExcerpt, f.Sentiment ?? "neutral", page.Url, Prov(f.Quote)));
            }

            foreach (var c in extraction.ClaimRisks ?? [])
            {
                if (string.IsNullOrWhiteSpace(c.ClaimText) || string.IsNullOrWhiteSpace(c.RiskKind)) continue;
                claimRisks.Add(new GccCompetitorClaimRiskAsset(
                    c.ClaimText, c.RiskKind, page.Url,
                    c.WriteGuidance ?? "do_not_echo_as_fact", Prov(c.Quote)));
            }

            foreach (var d in extraction.DemandSignals ?? [])
            {
                if (string.IsNullOrWhiteSpace(d.ContentFormat)) continue;
                demand.Add(new GccCompetitorDemandSignalAsset(
                    d.PrimaryKeywordFocus, d.ContentFormat, d.SearchIntentCategory, d.AdOrCopyTheme,
                    Prov(d.Quote)));
            }

            foreach (var s in extraction.Services ?? [])
            {
                if (string.IsNullOrWhiteSpace(s.ServiceName)) continue;
                services.Add(new GccCompetitorServiceAsset(
                    s.ServiceName, s.EngagementModel, s.Specialism, page.Url, Prov(s.Quote)));
            }

            foreach (var c in extraction.Clients ?? [])
            {
                if (string.IsNullOrWhiteSpace(c.ClientName)) continue;
                clients.Add(new GccCompetitorClientProofAsset(
                    c.ClientName, c.Sector, c.OutcomeClaim, page.Url, Prov(c.Quote)));
            }

            foreach (var p in extraction.Presence ?? [])
            {
                if (string.IsNullOrWhiteSpace(p.Location)) continue;
                presence.Add(new GccCompetitorPresenceAsset(
                    p.Location, p.PresenceKind ?? "stated", page.Url, Prov(p.Quote)));
            }

            foreach (var c in extraction.Credentials ?? [])
            {
                if (string.IsNullOrWhiteSpace(c.CredentialName)) continue;
                credentials.Add(new GccCompetitorCredentialAsset(
                    c.CredentialName, c.CredentialKind ?? "stated", page.Url, Prov(c.Quote)));
            }

            foreach (var p in extraction.Positioning ?? [])
            {
                if (string.IsNullOrWhiteSpace(p.PositioningStatement)) continue;
                positioning.Add(new GccCompetitorPositioningAsset(
                    p.PositioningStatement, p.AudienceFocus, page.Url, Prov(p.Quote)));
            }

            foreach (var b in extraction.Boundaries ?? [])
            {
                // Admissible only when the page states the boundary; silence is never a boundary.
                if (string.IsNullOrWhiteSpace(b.BoundaryDetail) || string.IsNullOrWhiteSpace(b.Quote))
                    continue;
                boundaries.Add(new GccCompetitorBoundaryAsset(
                    b.BoundaryKind ?? "stated", b.BoundaryDetail, page.Url, Prov(b.Quote)));
            }

            if (extraction.MediaProfile is { } media
                && (media.PublicationCadence is not null || media.MonetisationModel is not null
                    || (media.FormatMix?.Count ?? 0) > 0))
            {
                mediaProfiles.Add(new GccCompetitorMediaProfileAsset(
                    media.PublicationCadence, media.FormatMix ?? [], media.MonetisationModel,
                    media.TopicalAuthorityNote, page.Url, Prov(media.Quote)));
            }
        }

        return new GccCompetitorExtractionDocument(
            GccCompetitorExtractionDocument.CurrentExtractorVersion,
            Dedupe(coverage, a => a.TopicPath),
            Dedupe(gaps, a => a.GapTopic),
            Dedupe(axes, a => a.AxisId + "|" + a.RivalCapabilityPayload),
            Dedupe(deficits, a => a.AxisId + "|" + a.TriggerDeficit),
            Dedupe(faqs, a => a.Question),
            Dedupe(proof, a => a.ProofKind + "|" + a.ProofClaim),
            Dedupe(framing, a => a.FrameType + "|" + a.FrameExcerpt),
            Dedupe(claimRisks, a => a.ClaimText),
            Dedupe(demand, a => (a.PrimaryKeywordFocus ?? "") + "|" + a.ContentFormat),
            Dedupe(typeLabels, a => a.EntityName),
            Dedupe(services, a => a.ServiceName),
            Dedupe(clients, a => a.ClientName),
            Dedupe(presence, a => a.Location),
            Dedupe(credentials, a => a.CredentialName),
            Dedupe(positioning, a => a.PositioningStatement),
            Dedupe(boundaries, a => a.BoundaryKind + "|" + a.BoundaryDetail),
            Dedupe(mediaProfiles, a => (a.PublicationCadence ?? "") + "|" + (a.MonetisationModel ?? "")));
    }

    private async Task<CompetitorPageExtraction?> ExtractOnePageAsync(
        GccQuoteablePage page,
        IReadOnlyList<string>? competitorSeeds,
        IContentGenerationProvider provider,
        string schema,
        CancellationToken ct)
    {
        var userPrompt = BuildUserPrompt(page, competitorSeeds);
        if (userPrompt.Length == 0) return null;

        try
        {
            var completion = await generator.CompleteAsync<CompetitorPageExtraction>(
                new GccV2SchemaConstrainedRequest(
                    SystemPrompt: SystemPrompt,
                    UserPrompt: userPrompt,
                    JsonSchema: schema,
                    SchemaName: ProviderSchemaName,
                    Temperature: 0.1),
                provider,
                JsonOpts,
                ct).ConfigureAwait(false);
            return completion.Value;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception cause)
        {
            // Fail closed and silent: this page contributes nothing rather than contributing guesses.
            logger.LogWarning(cause, "Competitor extraction failed for {Url}; page skipped.", page.Url);
            return null;
        }
    }

    private static string BuildUserPrompt(GccQuoteablePage page, IReadOnlyList<string>? competitorSeeds)
    {
        var body = new StringBuilder();
        body.Append("URL: ").AppendLine(page.Url);
        body.Append("Title: ").AppendLine(page.Title);
        if (!string.IsNullOrWhiteSpace(page.SectionTitle))
            body.Append("Section: ").AppendLine(page.SectionTitle);

        if (competitorSeeds is { Count: > 0 })
        {
            body.Append("Known competitor entities for this research run: ")
                .AppendLine(string.Join(", ", competitorSeeds.Take(12)));
        }

        if (page.Headings.Count > 0)
        {
            body.AppendLine().AppendLine("Headings:");
            foreach (var h in page.Headings.Take(GccResearchCaps.MaxHeadingsPerPage))
                body.Append("  H").Append(h.Level).Append(": ").AppendLine(h.Text);
        }

        var paragraphs = page.Paragraphs
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Take(MaxParagraphsPerPage)
            .ToList();
        if (paragraphs.Count == 0 && page.Headings.Count == 0) return "";

        body.AppendLine().AppendLine("Page text:");
        foreach (var p in paragraphs)
        {
            var text = p.Length > MaxParagraphChars ? p[..MaxParagraphChars] : p;
            body.AppendLine(text);
        }

        return body.ToString();
    }

    private static IReadOnlyList<T> Dedupe<T>(List<T> items, Func<T, string> key) =>
        items.GroupBy(i => key(i).Trim().ToLowerInvariant())
            .Where(g => g.Key.Length > 0)
            .Select(g => g.First())
            .ToList();
}

/* ------------------------------------------------------------------ *
 * Schema-constrained response shape. Every item carries the verbatim  *
 * source quote so verify can check it against source Markdown.        *
 * ------------------------------------------------------------------ */

internal sealed record CompetitorPageExtraction(
    string? CompetitorType,
    string? TypeRationale,
    string? EntityName,
    string? TypeQuote,
    List<CoverageItem>? Coverage,
    List<GapItem>? Gaps,
    List<AxisItem>? ComparisonAxes,
    List<DeficitItem>? Deficits,
    List<FaqItem>? Faqs,
    List<ProofItem>? Proof,
    List<FramingItem>? Framing,
    List<ClaimRiskItem>? ClaimRisks,
    List<DemandItem>? DemandSignals,
    List<ServiceItem>? Services,
    List<ClientItem>? Clients,
    List<PresenceItem>? Presence,
    List<CredentialItem>? Credentials,
    List<PositioningItem>? Positioning,
    List<BoundaryItem>? Boundaries,
    MediaProfileItem? MediaProfile);

internal sealed record CoverageItem(string TopicPath, string DepthAssessment, List<string>? EvidenceHeadings, string? Quote);
internal sealed record GapItem(string GapTopic, string? DepthAssessment, string OpportunityForUs, string? Quote);
internal sealed record AxisItem(string AxisId, string? AxisLabel, string RivalCapabilityPayload, string? Quote);
internal sealed record DeficitItem(string AxisId, string TriggerDeficit, string? Quote);
internal sealed record FaqItem(string Question, string Answer, string? Quote);
internal sealed record ProofItem(string ProofKind, string ProofClaim, string? Quote);
internal sealed record FramingItem(string FrameType, string FrameExcerpt, string? Sentiment, string? Quote);
internal sealed record ClaimRiskItem(string ClaimText, string RiskKind, string? WriteGuidance, string? Quote);
internal sealed record DemandItem(string? PrimaryKeywordFocus, string ContentFormat, string? SearchIntentCategory, string? AdOrCopyTheme, string? Quote);
internal sealed record ServiceItem(string ServiceName, string? EngagementModel, string? Specialism, string? Quote);
internal sealed record ClientItem(string ClientName, string? Sector, string? OutcomeClaim, string? Quote);
internal sealed record PresenceItem(string Location, string? PresenceKind, string? Quote);
internal sealed record CredentialItem(string CredentialName, string? CredentialKind, string? Quote);
internal sealed record PositioningItem(string PositioningStatement, string? AudienceFocus, string? Quote);
internal sealed record BoundaryItem(string BoundaryKind, string BoundaryDetail, string? Quote);
internal sealed record MediaProfileItem(string? PublicationCadence, List<string>? FormatMix, string? MonetisationModel, string? TopicalAuthorityNote, string? Quote);
