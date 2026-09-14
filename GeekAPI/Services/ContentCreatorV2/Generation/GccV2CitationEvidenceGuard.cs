using GeekAPI.Services.ContentCreatorV2.ToolPages;
using GeekAPI.Services.ContentCreatorV2.Write;
using GeekAPI.Services.GeekCrawler;
using GeekAPI.Services.Rag;
using GeekAPI.Services.Workflow.Services;

namespace GeekAPI.Services.ContentCreatorV2.Generation;

/// <summary>
/// Citeable M2 guard: role-safe crawlType, quote↔source verify, section coverage.
/// </summary>
public static class GccV2CitationEvidenceGuard
{
    public sealed record AuditResult(
        IReadOnlyList<RagCitationDto> Citations,
        IReadOnlyList<string> EvidenceGaps);

    /// <summary>
    /// Audit WRITE output for blog/pillar-style long-form. Mutates citation <see cref="RagCitationDto.Verified"/>.
    /// </summary>
    public static async Task<AuditResult> AuditWriteOutputAsync(
        GccV2WriteOutput output,
        IReadOnlyList<Guid> partnerRunIds,
        IReadOnlyList<Guid> competitorRunIds,
        IGeekCrawlerRagClient? ragClient,
        CancellationToken ct,
        IReadOnlyList<GccV2PartnerMentionGate.PartnerToken>? partnerTokens = null)
    {
        var gaps = new List<string>();
        var audited = new List<RagCitationDto>();
        var markdownCache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        async Task<string?> LoadMarkdownForCitation(RagCitationDto citation)
        {
            if (string.IsNullOrWhiteSpace(citation.PageId)) return null;
            var cacheKey = $"{citation.RunId}|{citation.PageId}";
            if (markdownCache.TryGetValue(cacheKey, out var cached)) return cached;
            if (ragClient is null)
            {
                markdownCache[cacheKey] = null;
                return null;
            }

            var page = await ragClient.GetPageMarkdownAsync(citation.PageId!, ct, citation.RunId)
                .ConfigureAwait(false);
            var md = page?.Markdown;
            markdownCache[cacheKey] = md;
            markdownCache[citation.PageId!] = md;
            return md;
        }

        foreach (var section in output.AllSections)
        {
            var stamped = new List<RagCitationDto>();
            foreach (var citation in section.Citations ?? [])
            {
                var next = await AuditOneAsync(
                    citation,
                    section.SectionKey,
                    partnerRunIds,
                    competitorRunIds,
                    () => LoadMarkdownForCitation(citation),
                    gaps,
                    ct).ConfigureAwait(false);
                stamped.Add(next);
                audited.Add(next);
            }

            if (!RequiresCitationCoverage(section)) continue;

            if (!stamped.Any(c => c.Verified == true))
            {
                gaps.Add(
                    stamped.Count == 0
                        ? $"Section '{section.SectionKey}' lacks a verified citation (evidence gap)."
                        : $"Section '{section.SectionKey}' has citations that failed quote or role verification.");
            }
        }

        var withVerified = ApplyAuditedCitations(output, audited);
        if (partnerTokens is { Count: > 0 })
            gaps.AddRange(GccV2PartnerMentionGate.CollectGaps(withVerified, partnerTokens));

        return new AuditResult(audited, gaps.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <summary>Deterministic unit-test entry: no network; markdown supplied per pageId.</summary>
    public static AuditResult AuditWriteOutputForTests(
        GccV2WriteOutput output,
        IReadOnlyList<Guid> partnerRunIds,
        IReadOnlyList<Guid> competitorRunIds,
        IReadOnlyDictionary<string, string>? markdownByPageId,
        IReadOnlyList<GccV2PartnerMentionGate.PartnerToken>? partnerTokens = null)
    {
        var gaps = new List<string>();
        var audited = new List<RagCitationDto>();

        foreach (var section in output.AllSections)
        {
            var stamped = new List<RagCitationDto>();
            foreach (var citation in section.Citations ?? [])
            {
                string? markdown = null;
                if (!string.IsNullOrWhiteSpace(citation.PageId)
                    && markdownByPageId is not null
                    && markdownByPageId.TryGetValue(citation.PageId!, out var md))
                    markdown = md;

                var next = AuditOneSync(
                    citation,
                    section.SectionKey,
                    partnerRunIds,
                    competitorRunIds,
                    markdown,
                    gaps);
                stamped.Add(next);
                audited.Add(next);
            }

            if (!RequiresCitationCoverage(section)) continue;
            if (!stamped.Any(c => c.Verified == true))
            {
                gaps.Add(
                    stamped.Count == 0
                        ? $"Section '{section.SectionKey}' lacks a verified citation (evidence gap)."
                        : $"Section '{section.SectionKey}' has citations that failed quote or role verification.");
            }
        }

        var withVerified = ApplyAuditedCitations(output, audited);
        if (partnerTokens is { Count: > 0 })
            gaps.AddRange(GccV2PartnerMentionGate.CollectGaps(withVerified, partnerTokens));

        return new AuditResult(audited, gaps.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <summary>
    /// Apply sourceRights resolution + ship gaps after citation verify / partner-mention audit.
    /// </summary>
    public static AuditResult ApplySourceRights(
        GccV2WriteOutput outputAfterVerify,
        IReadOnlyList<RagCitationDto> auditedCitations,
        IReadOnlyList<string> existingGaps,
        string? rawBriefJson)
    {
        var overrides = GccV2SourceRightsGate.ParseBriefOverrides(rawBriefJson);
        var working = auditedCitations.Count > 0
            ? ApplyAuditedCitations(outputAfterVerify, auditedCitations)
            : outputAfterVerify;
        var (stamped, rightsGaps) = GccV2SourceRightsGate.ApplyAndCollectGaps(working, overrides);
        var gaps = existingGaps.Concat(rightsGaps).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return new AuditResult(stamped, gaps);
    }

    /// <summary>Stamp audited <see cref="RagCitationDto.Verified"/> back onto WRITE sections for ResultJson.</summary>
    public static GccV2WriteOutput ApplyAuditedCitations(
        GccV2WriteOutput output,
        IReadOnlyList<RagCitationDto> audited)
    {
        var bySection = audited
            .GroupBy(c => c.SectionKey ?? "", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<RagCitationDto>)g.ToList(), StringComparer.OrdinalIgnoreCase);

        GccV2WriteSection Stamp(GccV2WriteSection section)
        {
            if (!bySection.TryGetValue(section.SectionKey, out var citations))
                return section;
            return section with { Citations = citations };
        }

        var lede = Stamp(output.Lede);
        var sections = output.Sections.Select(Stamp).ToList();
        return new GccV2WriteOutput
        {
            Title = output.Title,
            MetaDescription = output.MetaDescription,
            Lede = lede,
            Sections = sections,
            TokensUsed = output.TokensUsed,
            Keywords = output.Keywords,
            ToolPage = output.ToolPage,
            Citations = GccV2WriteOutput.MergeCitations(new[] { lede }.Concat(sections)),
            Provenance = output.Provenance,
            Sources = output.Sources,
        };
    }

    internal static bool RequiresCitationCoverage(GccV2WriteSection section)
    {
        if (section.Section is null) return false;
        var words = ContentDocumentText.CountWords(section.Section);
        return words >= 40;
    }

    internal static bool CrawlTypeConflictsWithRun(
        string? crawlType,
        string? runId,
        IReadOnlyList<Guid> partnerRunIds,
        IReadOnlyList<Guid> competitorRunIds)
    {
        if (string.IsNullOrWhiteSpace(runId)) return false;
        var type = (crawlType ?? "").Trim().ToLowerInvariant();
        var isPartnerType = type is "partner";
        var isCompetitorType = type is "competitor" or "competitors";

        if (ContainsRunId(partnerRunIds, runId) && isCompetitorType)
            return true;

        if (ContainsRunId(competitorRunIds, runId) && isPartnerType)
            return true;

        return false;
    }

    private static bool ContainsRunId(IReadOnlyList<Guid> runIds, string runId)
    {
        foreach (var id in runIds)
        {
            if (string.Equals(runId, id.ToString("D"), StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static async Task<RagCitationDto> AuditOneAsync(
        RagCitationDto citation,
        string sectionKey,
        IReadOnlyList<Guid> partnerRunIds,
        IReadOnlyList<Guid> competitorRunIds,
        Func<Task<string?>> loadMarkdown,
        List<string> gaps,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var markdown = await loadMarkdown().ConfigureAwait(false);
        return AuditOneSync(citation, sectionKey, partnerRunIds, competitorRunIds, markdown, gaps);
    }

    private static RagCitationDto AuditOneSync(
        RagCitationDto citation,
        string sectionKey,
        IReadOnlyList<Guid> partnerRunIds,
        IReadOnlyList<Guid> competitorRunIds,
        string? markdown,
        List<string> gaps)
    {
        var sectionKeyBound = string.IsNullOrWhiteSpace(citation.SectionKey)
            ? sectionKey
            : citation.SectionKey;

        if (CrawlTypeConflictsWithRun(
                citation.CrawlType, citation.RunId, partnerRunIds, competitorRunIds))
        {
            gaps.Add(
                $"Citation on '{sectionKeyBound}' has crawlType '{citation.CrawlType}' inconsistent with runId.");
            return Clone(citation, sectionKeyBound, verified: false);
        }

        if (string.IsNullOrWhiteSpace(citation.Quote) || string.IsNullOrWhiteSpace(citation.Url))
        {
            gaps.Add($"Citation on '{sectionKeyBound}' missing quote or URL.");
            return Clone(citation, sectionKeyBound, verified: false);
        }

        if (string.IsNullOrWhiteSpace(citation.PageId) && string.IsNullOrWhiteSpace(citation.RunId))
        {
            gaps.Add($"Citation on '{sectionKeyBound}' missing pageId/runId for provenance.");
            return Clone(citation, sectionKeyBound, verified: false);
        }

        if (markdown is null)
        {
            // Cannot confirm span without source text — fail closed for ship-ready.
            gaps.Add($"Citation on '{sectionKeyBound}' could not load source Markdown for quote verify.");
            return Clone(citation, sectionKeyBound, verified: false);
        }

        var ok = GccV2ToolResearchExtractor.IsVerbatimFromPage(citation.Quote, markdown);
        if (!ok)
            gaps.Add($"Citation on '{sectionKeyBound}' quote is not an exact span of the source page.");

        return Clone(citation, sectionKeyBound, verified: ok);
    }

    private static RagCitationDto Clone(RagCitationDto c, string sectionKey, bool verified) =>
        new()
        {
            PageId = c.PageId,
            RunId = c.RunId,
            Url = c.Url,
            Title = c.Title,
            SectionTitle = c.SectionTitle,
            SectionKey = sectionKey,
            Quote = c.Quote,
            CrawlType = c.CrawlType,
            SourceDigest = c.SourceDigest,
            Verified = verified,
            SourceRights = c.SourceRights,
        };
}
