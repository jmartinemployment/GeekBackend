using GeekAPI.Services.ContentCreatorV2.Generation;
using GeekAPI.Services.ContentCreatorV2.Write;
using GeekAPI.Services.Rag;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.ContentCreatorV2.Partner;

/// <summary>
/// Bridges Markdown-verified partner citables into WRITE citations for §P1 partner-mention VALIDATE.
/// </summary>
public static class GccV2PartnerCitableBridge
{
    /// <summary>
    /// For sections that mention a partner token without a verified partner citation, attach the
    /// preferred evidence shape: a Markdown-verified <see cref="GccPartnerCitableAsset"/> as <see cref="RagCitationDto"/>.
    /// </summary>
    public static GccV2WriteOutput AttachVerifiedCitables(
        GccV2WriteOutput output,
        GccPartnerExtractionDocument? extraction,
        IReadOnlyList<GccV2PartnerMentionGate.PartnerToken> partnerTokens)
    {
        if (extraction is null || partnerTokens.Count == 0)
            return output;

        var shipCitables = extraction.Citables
            .Where(c => c.Provenance.MarkdownVerified
                        && string.Equals(
                            c.Provenance.CrawlType,
                            GccPartnerExtractionDocument.CrawlTypePartner,
                            StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(c.IsolatedClaim)
                        && !string.IsNullOrWhiteSpace(c.OriginProofUrl))
            .ToList();
        if (shipCitables.Count == 0)
            return output;

        GccV2WriteSection Attach(GccV2WriteSection section)
        {
            if (section.Section is null) return section;
            var scan = GccV2PartnerMentionGate.BuildScanText(section.Section, section.Citations);
            if (string.IsNullOrWhiteSpace(scan)) return section;
            if (GccV2PartnerMentionGate.FindMention(scan, partnerTokens) is null) return section;

            var existing = section.Citations ?? [];
            if (existing.Any(c =>
                    c.Verified == true
                    && string.Equals((c.CrawlType ?? "").Trim(), "partner", StringComparison.OrdinalIgnoreCase)))
                return section;

            var preferred = PickCitableForSection(scan, shipCitables) ?? shipCitables[0];
            var citation = ToCitation(preferred, section.SectionKey);
            return section with { Citations = existing.Concat([citation]).ToList() };
        }

        var lede = Attach(output.Lede);
        var sections = output.Sections.Select(Attach).ToList();
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

    public static RagCitationDto ToCitation(GccPartnerCitableAsset citable, string sectionKey) =>
        new()
        {
            PageId = citable.Provenance.PageId,
            RunId = citable.Provenance.RunId,
            Url = citable.OriginProofUrl,
            Title = citable.Provenance.SectionTitle,
            SectionTitle = citable.Provenance.SectionTitle,
            SectionKey = sectionKey,
            Quote = citable.Provenance.Quote ?? citable.IsolatedClaim,
            CrawlType = GccPartnerExtractionDocument.CrawlTypePartner,
            SourceDigest = citable.Provenance.SourceDigest,
            Verified = citable.Provenance.MarkdownVerified ? true : null,
            SourceRights = citable.Provenance.SourceRights,
        };

    private static GccPartnerCitableAsset? PickCitableForSection(
        string normalizedScan,
        IReadOnlyList<GccPartnerCitableAsset> citables)
    {
        foreach (var citable in citables)
        {
            var claimNorm = GccV2PartnerMentionGate.Normalize(citable.IsolatedClaim);
            if (claimNorm.Length >= 12 && normalizedScan.Contains(claimNorm, StringComparison.Ordinal))
                return citable;
        }

        return null;
    }
}
