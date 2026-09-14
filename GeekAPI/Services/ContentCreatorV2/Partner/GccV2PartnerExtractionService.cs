using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.ContentCreatorV2.Partner;

/// <summary>
/// Deterministic partner library extraction (partner-extraction plan §2–§8).
/// Every claim string must be substring-grounded in page paragraphs/headings — no invented marketing.
/// Empty asset lists are honest (nothing found); they are not a soft success substitute for missing runs.
/// </summary>
public static partial class GccV2PartnerExtractionService
{
    public static GccPartnerExtractionDocument ExtractFromPages(
        IReadOnlyList<GccQuoteablePage> pages,
        IReadOnlyList<string>? knownPartnerToolNames = null)
    {
        ArgumentNullException.ThrowIfNull(pages);
        if (pages.Count == 0)
        {
            return EmptyDocument();
        }

        var citables = new List<GccPartnerCitableAsset>();
        var ads = new List<GccPartnerAdvertisementAsset>();
        var comparisons = new List<GccPartnerComparisonAsset>();
        var alternatives = new List<GccPartnerAlternativesAsset>();
        var pricing = new List<GccPartnerPricingTierAsset>();
        var icp = new List<GccPartnerIcpAsset>();
        var integrations = new List<GccPartnerIntegrationAsset>();
        var faqs = new List<GccPartnerFaqAsset>();
        var proofs = new List<GccPartnerProofAsset>();
        var offers = new List<GccPartnerOfferCtaAsset>();
        var disqualifiers = new List<GccPartnerDisqualifierAsset>();
        var playbooks = new List<GccPartnerUseCasePlaybookAsset>();
        var categories = new List<GccPartnerCategoryAsset>();
        var freshness = new List<GccPartnerFreshnessAsset>();
        var battlecards = new List<GccPartnerBattlecardSliceAsset>();
        var demoBeats = new List<GccPartnerDemoBeatAsset>();
        var compliance = new List<GccPartnerComplianceSnippetAsset>();
        var disclosures = new List<GccPartnerAffiliateDisclosureAsset>();

        var partnerNames = CollectPartnerNames(pages, knownPartnerToolNames);

        foreach (var page in pages)
        {
            var corpus = BuildCorpus(page);
            if (string.IsNullOrWhiteSpace(corpus.Joined))
                continue;

            var provenance = BuildProvenance(page);
            ExtractCitables(page, corpus, provenance, citables);
            ExtractAdvertisements(page, corpus, provenance, ads);
            ExtractComparisons(page, corpus, provenance, comparisons);
            ExtractAlternatives(page, corpus, provenance, alternatives, partnerNames);
            ExtractPricing(page, corpus, provenance, pricing);
            ExtractIcp(page, corpus, provenance, icp);
            ExtractIntegrations(page, corpus, provenance, integrations);
            ExtractFaq(page, corpus, provenance, faqs);
            ExtractProof(page, corpus, provenance, proofs);
            ExtractOffers(page, corpus, provenance, offers);
            ExtractDisqualifiers(page, corpus, provenance, disqualifiers);
            ExtractPlaybooks(page, corpus, provenance, playbooks, offers);
            ExtractCategories(page, corpus, provenance, categories);
            ExtractFreshness(page, corpus, provenance, freshness);
            ExtractBattlecard(page, corpus, provenance, battlecards);
            ExtractDemoBeats(page, corpus, provenance, demoBeats);
            ExtractCompliance(page, corpus, provenance, compliance);
            ExtractAffiliateDisclosures(page, corpus, provenance, disclosures);
        }

        var doc = new GccPartnerExtractionDocument(
            GccPartnerExtractionDocument.CurrentExtractorVersion,
            DateTimeOffset.UtcNow,
            DedupCitables(citables),
            DedupAds(ads),
            DedupComparisons(comparisons),
            DedupAlternatives(alternatives),
            DedupPricing(pricing),
            DedupIcp(icp),
            DedupIntegrations(integrations),
            DedupFaqs(faqs),
            DedupProofs(proofs),
            DedupOffers(offers),
            DedupDisqualifiers(disqualifiers),
            DedupPlaybooks(playbooks),
            DedupCategories(categories),
            DedupFreshness(freshness),
            DedupBattlecards(battlecards),
            DedupDemoBeats(demoBeats),
            DedupCompliance(compliance),
            DedupDisclosures(disclosures),
            SoftwareApplicationJsonLd: null);

        return doc;
    }

    public static GccPartnerExtractionDocument EmptyDocument() =>
        new(
            GccPartnerExtractionDocument.CurrentExtractorVersion,
            DateTimeOffset.UtcNow,
            [], [], [], [], [], [], [], [], [], [], [],
            [], [], [], [], [], [], []);

    private static void ExtractCitables(
        GccQuoteablePage page,
        PageCorpus corpus,
        GccPartnerExtractionProvenance provenance,
        List<GccPartnerCitableAsset> sink)
    {
        foreach (var paragraph in page.Paragraphs)
        {
            if (!MetricClaimRegex().IsMatch(paragraph)) continue;
            var claim = Truncate(NormalizeWhitespace(paragraph), 280);
            if (!IsGrounded(claim, corpus.Joined)) continue;
            sink.Add(new GccPartnerCitableAsset(
                claim,
                provenance.OriginProofUrl,
                provenance.TemporalAnchorUtc,
                provenance));
        }
    }

    private static void ExtractAdvertisements(
        GccQuoteablePage page,
        PageCorpus corpus,
        GccPartnerExtractionProvenance provenance,
        List<GccPartnerAdvertisementAsset> sink)
    {
        var hook = FirstGroundedSentence(
            corpus,
            p => p.Length is >= 24 and <= 160
                 && (BenefitHintRegex().IsMatch(p) || ImperativeHintRegex().IsMatch(p)));
        if (hook is null) return;

        var pain = FirstGroundedSentence(
            corpus,
            p => PainHintRegex().IsMatch(p) && p.Length is >= 16 and <= 200);
        pain ??= FirstGroundedSentence(
            corpus,
            p => p.Contains("without", StringComparison.OrdinalIgnoreCase) && p.Length is >= 16 and <= 200);

        var cta = FirstGroundedSentence(
            corpus,
            p => CtaLabelRegex().IsMatch(p) && p.Length <= 120);
        cta ??= page.Title is { Length: > 0 } t && IsGrounded(t, corpus.Joined)
            ? Truncate(t, 120)
            : null;

        if (pain is null || cta is null) return;

        sink.Add(new GccPartnerAdvertisementAsset(hook, pain, cta, provenance));
    }

    private static void ExtractComparisons(
        GccQuoteablePage page,
        PageCorpus corpus,
        GccPartnerExtractionProvenance provenance,
        List<GccPartnerComparisonAsset> sink)
    {
        foreach (var (featureId, pattern) in FeatureIdPatterns)
        {
            foreach (var paragraph in page.Paragraphs)
            {
                if (!pattern.IsMatch(paragraph)) continue;
                var payload = Truncate(NormalizeWhitespace(paragraph), 240);
                if (!IsGrounded(payload, corpus.Joined)) continue;
                string? cost = null;
                var money = MoneyRegex().Match(paragraph);
                if (money.Success)
                    cost = Truncate(NormalizeWhitespace(money.Value), 80);
                sink.Add(new GccPartnerComparisonAsset(featureId, payload, cost, provenance));
                break;
            }
        }
    }

    private static void ExtractAlternatives(
        GccQuoteablePage page,
        PageCorpus corpus,
        GccPartnerExtractionProvenance provenance,
        List<GccPartnerAlternativesAsset> sink,
        IReadOnlyList<string> partnerNames)
    {
        foreach (var paragraph in page.Paragraphs)
        {
            if (!DeficitHintRegex().IsMatch(paragraph)) continue;
            var deficit = Truncate(NormalizeWhitespace(paragraph), 240);
            if (!IsGrounded(deficit, corpus.Joined)) continue;

            var swaps = partnerNames
                .Where(n => !string.Equals(n, page.Title, StringComparison.OrdinalIgnoreCase))
                .Where(n => !deficit.Contains(n, StringComparison.OrdinalIgnoreCase))
                .Take(5)
                .ToList();
            if (swaps.Count == 0) continue;

            var pivot = Truncate(
                $"If you need an alternative when facing this limit — {Truncate(deficit, 120)} — consider {string.Join(", ", swaps)}.",
                320);
            // Pivot is assembly copy; deficit remains the grounded claim.
            sink.Add(new GccPartnerAlternativesAsset(deficit, swaps, pivot, provenance));
        }
    }

    private static void ExtractPricing(
        GccQuoteablePage page,
        PageCorpus corpus,
        GccPartnerExtractionProvenance provenance,
        List<GccPartnerPricingTierAsset> sink)
    {
        foreach (var paragraph in page.Paragraphs)
        {
            var money = MoneyRegex().Match(paragraph);
            if (!money.Success) continue;
            if (!IsGrounded(paragraph, corpus.Joined)) continue;

            decimal? listPrice = null;
            if (decimal.TryParse(
                    money.Groups["amount"].Value,
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out var parsed))
                listPrice = parsed;

            var currency = money.Groups["currency"].Success
                ? money.Groups["currency"].Value.ToUpperInvariant() switch
                {
                    "$" or "USD" => "USD",
                    "€" or "EUR" => "EUR",
                    "£" or "GBP" => "GBP",
                    var other => other.Length == 3 ? other.ToUpperInvariant() : "USD",
                }
                : "USD";

            var period = BillingPeriodRegex().Match(paragraph) is { Success: true } bp
                ? bp.Groups["period"].Value.ToLowerInvariant()
                : null;
            if (period is "mo" or "month") period = "monthly";
            if (period is "yr" or "year" or "annually") period = "annual";

            var tier = TierNameRegex().Match(paragraph) is { Success: true } tm
                ? Truncate(tm.Groups["tier"].Value.Trim(), 80)
                : InferTierFromHeading(page) ?? "Listed";

            string? trial = null;
            if (TrialRegex().IsMatch(paragraph))
                trial = Truncate(NormalizeWhitespace(TrialRegex().Match(paragraph).Value), 120);

            string? overage = null;
            if (OverageRegex().IsMatch(paragraph))
                overage = Truncate(NormalizeWhitespace(OverageRegex().Match(paragraph).Value), 160);

            string? gates = null;
            if (FeatureGateHintRegex().IsMatch(paragraph))
                gates = Truncate(NormalizeWhitespace(paragraph), 200);

            string? effective = null;
            if (AsOfRegex().Match(paragraph) is { Success: true } asOf)
                effective = Truncate(asOf.Groups["asof"].Value.Trim(), 80);

            sink.Add(new GccPartnerPricingTierAsset(
                tier,
                listPrice,
                currency,
                period,
                gates,
                trial,
                overage,
                effective,
                provenance.OriginProofUrl,
                provenance));
        }
    }

    private static void ExtractIcp(
        GccQuoteablePage page,
        PageCorpus corpus,
        GccPartnerExtractionProvenance provenance,
        List<GccPartnerIcpAsset> sink)
    {
        var served = new List<string>();
        var excluded = new List<string>();
        var industries = new List<string>();
        var roles = new List<string>();
        string? sizeBand = null;

        foreach (var paragraph in page.Paragraphs)
        {
            if (!IsGrounded(paragraph, corpus.Joined)) continue;

            foreach (Match m in BuiltForRegex().Matches(paragraph))
            {
                var segment = Truncate(NormalizeWhitespace(m.Groups["seg"].Value), 120);
                if (segment.Length >= 3) served.Add(segment);
            }

            foreach (Match m in NotForRegex().Matches(paragraph))
            {
                var segment = Truncate(NormalizeWhitespace(m.Groups["seg"].Value), 120);
                if (segment.Length >= 3) excluded.Add(segment);
            }

            if (sizeBand is null && CompanySizeRegex().Match(paragraph) is { Success: true } size)
                sizeBand = Truncate(NormalizeWhitespace(size.Value), 80);

            foreach (var industry in IndustryKeywords)
            {
                if (paragraph.Contains(industry, StringComparison.OrdinalIgnoreCase)
                    && !industries.Contains(industry, StringComparer.OrdinalIgnoreCase))
                    industries.Add(industry);
            }

            foreach (var role in BuyerRoleKeywords)
            {
                if (paragraph.Contains(role, StringComparison.OrdinalIgnoreCase)
                    && !roles.Contains(role, StringComparer.OrdinalIgnoreCase))
                    roles.Add(role);
            }
        }

        if (served.Count == 0 && excluded.Count == 0 && sizeBand is null
            && industries.Count == 0 && roles.Count == 0)
            return;

        sink.Add(new GccPartnerIcpAsset(
            served.Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToList(),
            excluded.Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToList(),
            sizeBand,
            industries.Take(12).ToList(),
            roles.Take(12).ToList(),
            provenance));
    }

    private static void ExtractIntegrations(
        GccQuoteablePage page,
        PageCorpus corpus,
        GccPartnerExtractionProvenance provenance,
        List<GccPartnerIntegrationAsset> sink)
    {
        foreach (var paragraph in page.Paragraphs)
        {
            if (!IsGrounded(paragraph, corpus.Joined)) continue;

            foreach (Match m in IntegratesWithRegex().Matches(paragraph))
            {
                var name = Truncate(NormalizeWhitespace(m.Groups["name"].Value.Trim().TrimEnd('.', ',')), 80);
                if (name.Length < 2) continue;
                var type = paragraph.Contains("Zapier", StringComparison.OrdinalIgnoreCase) ? "Zapier"
                    : paragraph.Contains("native", StringComparison.OrdinalIgnoreCase) ? "native"
                    : paragraph.Contains("API", StringComparison.OrdinalIgnoreCase) ? "API"
                    : "named";
                string? api = null;
                if (ApiSdkRegex().Match(paragraph) is { Success: true } apiMatch)
                    api = Truncate(NormalizeWhitespace(apiMatch.Value), 80);
                string? marketplace = null;
                if (MarketplaceRegex().Match(paragraph) is { Success: true } mp)
                    marketplace = Truncate(NormalizeWhitespace(mp.Value), 120);
                sink.Add(new GccPartnerIntegrationAsset(name, type, api, marketplace, provenance));
            }

            if (ApiSdkRegex().IsMatch(paragraph) && !IntegratesWithRegex().IsMatch(paragraph))
            {
                var api = Truncate(NormalizeWhitespace(ApiSdkRegex().Match(paragraph).Value), 80);
                sink.Add(new GccPartnerIntegrationAsset("Public API", "API", api, null, provenance));
            }
        }
    }

    private static void ExtractFaq(
        GccQuoteablePage page,
        PageCorpus corpus,
        GccPartnerExtractionProvenance provenance,
        List<GccPartnerFaqAsset> sink)
    {
        var paragraphs = page.Paragraphs;
        for (var i = 0; i < paragraphs.Count; i++)
        {
            var q = paragraphs[i];
            if (!LooksLikeQuestion(q)) continue;
            if (i + 1 >= paragraphs.Count) continue;
            var a = paragraphs[i + 1];
            if (LooksLikeQuestion(a) || a.Length < 12) continue;
            var question = Truncate(NormalizeWhitespace(q), 200);
            var answer = Truncate(NormalizeWhitespace(a), 400);
            if (!IsGrounded(question, corpus.Joined) || !IsGrounded(answer, corpus.Joined)) continue;
            sink.Add(new GccPartnerFaqAsset(question, answer, provenance.OriginProofUrl, provenance));
        }

        foreach (var heading in page.Headings)
        {
            if (!LooksLikeQuestion(heading.Text)) continue;
            var answer = page.Paragraphs.FirstOrDefault(p =>
                p.Length >= 12 && IsGrounded(p, corpus.Joined));
            if (answer is null) continue;
            var question = Truncate(NormalizeWhitespace(heading.Text), 200);
            if (!IsGrounded(question, corpus.Joined)) continue;
            sink.Add(new GccPartnerFaqAsset(
                question,
                Truncate(NormalizeWhitespace(answer), 400),
                provenance.OriginProofUrl,
                provenance));
        }
    }

    private static void ExtractProof(
        GccQuoteablePage page,
        PageCorpus corpus,
        GccPartnerExtractionProvenance provenance,
        List<GccPartnerProofAsset> sink)
    {
        foreach (var paragraph in page.Paragraphs)
        {
            if (!IsGrounded(paragraph, corpus.Joined)) continue;
            if (CertificationRegex().IsMatch(paragraph))
            {
                sink.Add(new GccPartnerProofAsset(
                    "certification",
                    Truncate(NormalizeWhitespace(CertificationRegex().Match(paragraph).Value), 160),
                    provenance.OriginProofUrl,
                    provenance));
            }

            if (MetricClaimRegex().IsMatch(paragraph) && CaseMetricHintRegex().IsMatch(paragraph))
            {
                sink.Add(new GccPartnerProofAsset(
                    "case_metric",
                    Truncate(NormalizeWhitespace(paragraph), 200),
                    provenance.OriginProofUrl,
                    provenance));
            }

            if (AwardHintRegex().IsMatch(paragraph))
            {
                sink.Add(new GccPartnerProofAsset(
                    "award",
                    Truncate(NormalizeWhitespace(paragraph), 200),
                    provenance.OriginProofUrl,
                    provenance));
            }
        }
    }

    private static void ExtractOffers(
        GccQuoteablePage page,
        PageCorpus corpus,
        GccPartnerExtractionProvenance provenance,
        List<GccPartnerOfferCtaAsset> sink)
    {
        foreach (var paragraph in page.Paragraphs)
        {
            if (!CtaLabelRegex().IsMatch(paragraph)) continue;
            var labelMatch = CtaLabelRegex().Match(paragraph);
            var label = Truncate(NormalizeWhitespace(labelMatch.Value), 80);
            if (!IsGrounded(label, corpus.Joined) && !IsGrounded(paragraph, corpus.Joined)) continue;

            var dest = provenance.OriginProofUrl;
            if (AbsoluteUrlRegex().Match(paragraph) is { Success: true } urlMatch
                && Uri.TryCreate(urlMatch.Value, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                dest = uri.GetLeftPart(UriPartial.Query);
            }

            var offerType = label.Contains("trial", StringComparison.OrdinalIgnoreCase) ? "trial"
                : label.Contains("demo", StringComparison.OrdinalIgnoreCase) ? "demo"
                : label.Contains("buy", StringComparison.OrdinalIgnoreCase)
                  || label.Contains("purchase", StringComparison.OrdinalIgnoreCase)
                  || label.Contains("pricing", StringComparison.OrdinalIgnoreCase) ? "purchase"
                : label.Contains("affiliate", StringComparison.OrdinalIgnoreCase) ? "affiliate"
                : "cta";

            sink.Add(new GccPartnerOfferCtaAsset(label, dest, offerType, Truncate(paragraph, 160), provenance));
        }
    }

    private static void ExtractDisqualifiers(
        GccQuoteablePage page,
        PageCorpus corpus,
        GccPartnerExtractionProvenance provenance,
        List<GccPartnerDisqualifierAsset> sink)
    {
        foreach (var paragraph in page.Paragraphs)
        {
            if (!IsGrounded(paragraph, corpus.Joined)) continue;
            if (SeatLimitRegex().IsMatch(paragraph))
            {
                sink.Add(new GccPartnerDisqualifierAsset(
                    "seats",
                    Truncate(NormalizeWhitespace(paragraph), 200),
                    provenance.OriginProofUrl,
                    provenance));
            }

            if (RegionLimitRegex().IsMatch(paragraph))
            {
                sink.Add(new GccPartnerDisqualifierAsset(
                    "region",
                    Truncate(NormalizeWhitespace(paragraph), 200),
                    provenance.OriginProofUrl,
                    provenance));
            }

            if (NotForRegex().IsMatch(paragraph))
            {
                sink.Add(new GccPartnerDisqualifierAsset(
                    "feature",
                    Truncate(NormalizeWhitespace(paragraph), 200),
                    provenance.OriginProofUrl,
                    provenance));
            }

            if (LanguageLimitRegex().IsMatch(paragraph))
            {
                sink.Add(new GccPartnerDisqualifierAsset(
                    "language",
                    Truncate(NormalizeWhitespace(paragraph), 200),
                    provenance.OriginProofUrl,
                    provenance));
            }
        }
    }

    private static void ExtractPlaybooks(
        GccQuoteablePage page,
        PageCorpus corpus,
        GccPartnerExtractionProvenance provenance,
        List<GccPartnerUseCasePlaybookAsset> sink,
        IReadOnlyList<GccPartnerOfferCtaAsset> offers)
    {
        foreach (var heading in page.Headings)
        {
            if (!UseCaseHintRegex().IsMatch(heading.Text)) continue;
            var jtbd = Truncate(NormalizeWhitespace(heading.Text), 160);
            if (jtbd.Length < 8) continue;

            var steps = page.Paragraphs
                .Where(p => p.Length >= 20 && IsGrounded(p, corpus.Joined))
                .Take(6)
                .Select(p => Truncate(NormalizeWhitespace(p), 200))
                .ToList();
            if (steps.Count == 0) continue;

            var cta = offers.FirstOrDefault(o =>
                    string.Equals(o.Provenance.OriginProofUrl, provenance.OriginProofUrl, StringComparison.OrdinalIgnoreCase))
                ?.DestinationUrl;

            sink.Add(new GccPartnerUseCasePlaybookAsset(jtbd, steps, cta, provenance));
        }
    }

    private static void ExtractCategories(
        GccQuoteablePage page,
        PageCorpus corpus,
        GccPartnerExtractionProvenance provenance,
        List<GccPartnerCategoryAsset> sink)
    {
        foreach (var paragraph in page.Paragraphs)
        {
            if (!CategoryHintRegex().IsMatch(paragraph) || !IsGrounded(paragraph, corpus.Joined)) continue;
            var primary = Truncate(NormalizeWhitespace(CategoryHintRegex().Match(paragraph).Groups["cat"].Value), 80);
            if (primary.Length < 3) continue;
            string? vs = null;
            if (VsCategoryRegex().Match(paragraph) is { Success: true } vsMatch)
                vs = Truncate(NormalizeWhitespace(vsMatch.Groups["vs"].Value), 80);
            sink.Add(new GccPartnerCategoryAsset(primary, [], vs, provenance));
        }
    }

    private static void ExtractFreshness(
        GccQuoteablePage page,
        PageCorpus corpus,
        GccPartnerExtractionProvenance provenance,
        List<GccPartnerFreshnessAsset> sink)
    {
        foreach (var paragraph in page.Paragraphs)
        {
            if (!AsOfRegex().IsMatch(paragraph) && !ChangeHintRegex().IsMatch(paragraph)) continue;
            if (!IsGrounded(paragraph, corpus.Joined)) continue;
            var kind = paragraph.Contains("price", StringComparison.OrdinalIgnoreCase) ? "price"
                : paragraph.Contains("policy", StringComparison.OrdinalIgnoreCase) ? "policy"
                : "feature";
            string? asOf = AsOfRegex().Match(paragraph) is { Success: true } m
                ? Truncate(m.Groups["asof"].Value.Trim(), 80)
                : null;
            sink.Add(new GccPartnerFreshnessAsset(
                kind,
                Truncate(NormalizeWhitespace(paragraph), 240),
                asOf,
                provenance.OriginProofUrl,
                provenance));
        }
    }

    private static void ExtractBattlecard(
        GccQuoteablePage page,
        PageCorpus corpus,
        GccPartnerExtractionProvenance provenance,
        List<GccPartnerBattlecardSliceAsset> sink)
    {
        var win = FirstGroundedSentence(corpus, p => WinThemeHintRegex().IsMatch(p) && p.Length is >= 20 and <= 220);
        var landmine = FirstGroundedSentence(corpus, p => DeficitHintRegex().IsMatch(p) && p.Length is >= 20 and <= 220);
        if (win is null || landmine is null) return;
        var coaching = Truncate($"Lead with: {Truncate(win, 100)}. Watch for: {Truncate(landmine, 100)}.", 280);
        sink.Add(new GccPartnerBattlecardSliceAsset(win, landmine, coaching, provenance));
    }

    private static void ExtractDemoBeats(
        GccQuoteablePage page,
        PageCorpus corpus,
        GccPartnerExtractionProvenance provenance,
        List<GccPartnerDemoBeatAsset> sink)
    {
        foreach (var heading in page.Headings.Where(h => h.Level is >= 2 and <= 3))
        {
            var title = Truncate(NormalizeWhitespace(heading.Text), 100);
            var claim = page.Paragraphs.FirstOrDefault(p =>
                p.Length is >= 24 and <= 220 && IsGrounded(p, corpus.Joined));
            if (claim is null) continue;
            sink.Add(new GccPartnerDemoBeatAsset(
                title,
                Truncate(NormalizeWhitespace(claim), 200),
                title,
                provenance.OriginProofUrl,
                provenance));
        }
    }

    private static void ExtractCompliance(
        GccQuoteablePage page,
        PageCorpus corpus,
        GccPartnerExtractionProvenance provenance,
        List<GccPartnerComplianceSnippetAsset> sink)
    {
        foreach (var paragraph in page.Paragraphs)
        {
            if (!IsGrounded(paragraph, corpus.Joined)) continue;
            string? kind = null;
            if (paragraph.Contains("refund", StringComparison.OrdinalIgnoreCase)) kind = "refund";
            else if (paragraph.Contains("residency", StringComparison.OrdinalIgnoreCase)
                     || paragraph.Contains("data region", StringComparison.OrdinalIgnoreCase))
                kind = "data_residency";
            else if (Regex.IsMatch(paragraph, @"\bSLA\b", RegexOptions.IgnoreCase)) kind = "sla";
            else if (paragraph.Contains("SOC 2", StringComparison.OrdinalIgnoreCase)
                     || paragraph.Contains("security", StringComparison.OrdinalIgnoreCase)
                        && CertificationRegex().IsMatch(paragraph))
                kind = "security";
            if (kind is null) continue;
            sink.Add(new GccPartnerComplianceSnippetAsset(
                kind,
                Truncate(NormalizeWhitespace(paragraph), 280),
                provenance.OriginProofUrl,
                provenance));
        }
    }

    private static void ExtractAffiliateDisclosures(
        GccQuoteablePage page,
        PageCorpus corpus,
        GccPartnerExtractionProvenance provenance,
        List<GccPartnerAffiliateDisclosureAsset> sink)
    {
        foreach (var paragraph in page.Paragraphs)
        {
            if (!AffiliateDisclosureRegex().IsMatch(paragraph)) continue;
            if (!IsGrounded(paragraph, corpus.Joined)) continue;
            sink.Add(new GccPartnerAffiliateDisclosureAsset(
                Truncate(NormalizeWhitespace(paragraph), 320),
                null,
                provenance.OriginProofUrl,
                provenance));
        }
    }

    private static GccPartnerExtractionProvenance BuildProvenance(GccQuoteablePage page) =>
        new(
            page.Url,
            GccPartnerExtractionDocument.CrawlTypePartner,
            page.RunId,
            page.PageId,
            page.SectionTitle,
            page.SourceDigest,
            page.CrawledAtUtc);

    private static PageCorpus BuildCorpus(GccQuoteablePage page)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(page.Title)) parts.Add(page.Title);
        foreach (var h in page.Headings)
            parts.Add(h.Text);
        foreach (var p in page.Paragraphs)
            parts.Add(p);
        var joined = string.Join('\n', parts.Where(s => !string.IsNullOrWhiteSpace(s)));
        return new PageCorpus(joined, parts);
    }

    private static IReadOnlyList<string> CollectPartnerNames(
        IReadOnlyList<GccQuoteablePage> pages,
        IReadOnlyList<string>? knownPartnerToolNames)
    {
        var names = new List<string>();
        if (knownPartnerToolNames is not null)
        {
            foreach (var n in knownPartnerToolNames)
            {
                if (!string.IsNullOrWhiteSpace(n)
                    && !names.Contains(n.Trim(), StringComparer.OrdinalIgnoreCase))
                    names.Add(n.Trim());
            }
        }

        foreach (var page in pages)
        {
            if (!string.IsNullOrWhiteSpace(page.Title)
                && !names.Contains(page.Title.Trim(), StringComparer.OrdinalIgnoreCase))
                names.Add(page.Title.Trim());
        }

        return names;
    }

    private static string? InferTierFromHeading(GccQuoteablePage page)
    {
        foreach (var h in page.Headings)
        {
            if (TierNameRegex().Match(h.Text) is { Success: true } m)
                return Truncate(m.Groups["tier"].Value.Trim(), 80);
            if (h.Text.Contains("pricing", StringComparison.OrdinalIgnoreCase)
                || h.Text.Contains("plans", StringComparison.OrdinalIgnoreCase))
                return Truncate(h.Text, 80);
        }

        return null;
    }

    private static string? FirstGroundedSentence(PageCorpus corpus, Func<string, bool> predicate)
    {
        foreach (var part in corpus.Parts)
        {
            foreach (var sentence in SplitSentences(part))
            {
                var normalized = Truncate(NormalizeWhitespace(sentence), 220);
                if (normalized.Length < 12) continue;
                if (!predicate(normalized)) continue;
                if (!IsGrounded(normalized, corpus.Joined)) continue;
                return normalized;
            }
        }

        return null;
    }

    private static IEnumerable<string> SplitSentences(string text)
    {
        foreach (var piece in SentenceSplitRegex().Split(text))
        {
            var t = piece.Trim();
            if (t.Length > 0) yield return t;
        }
    }

    private static bool LooksLikeQuestion(string text)
    {
        var t = text.Trim();
        return t.EndsWith('?') || QuestionStartRegex().IsMatch(t);
    }

    internal static bool IsGrounded(string claim, string corpus)
    {
        if (string.IsNullOrWhiteSpace(claim) || string.IsNullOrWhiteSpace(corpus)) return false;
        var needle = CompactForCompare(claim);
        var hay = CompactForCompare(corpus);
        return needle.Length >= 8 && hay.Contains(needle, StringComparison.Ordinal);
    }

    private static string CompactForCompare(string value)
    {
        var sb = new StringBuilder(value.Length);
        var prevSpace = false;
        foreach (var ch in value.Normalize(NormalizationForm.FormKC))
        {
            if (char.IsWhiteSpace(ch))
            {
                if (prevSpace) continue;
                sb.Append(' ');
                prevSpace = true;
                continue;
            }

            sb.Append(char.ToLowerInvariant(ch));
            prevSpace = false;
        }

        return sb.ToString().Trim();
    }

    private static string NormalizeWhitespace(string value) =>
        WhitespaceRegex().Replace(value.Trim(), " ");

    private static string Truncate(string value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max) return value;
        return value[..max].TrimEnd() + "…";
    }

    private sealed record PageCorpus(string Joined, IReadOnlyList<string> Parts);

    private static readonly (string Id, Regex Pattern)[] FeatureIdPatterns =
    [
        ("pricing_model", PricingModelHintRegex()),
        ("api_access", ApiSdkRegex()),
        ("seat_limits", SeatLimitRegex()),
        ("integrations", IntegratesWithRegex()),
        ("security", CertificationRegex()),
    ];

    private static readonly string[] IndustryKeywords =
    [
        "Accounting", "SaaS", "Finance", "Healthcare", "Retail", "Manufacturing", "Education", "Legal",
    ];

    private static readonly string[] BuyerRoleKeywords =
    [
        "Controller", "RevOps", "CFO", "Marketing", "Sales", "Founder", "Engineer", "Developer",
    ];

    private static List<GccPartnerCitableAsset> DedupCitables(List<GccPartnerCitableAsset> items) =>
        items.GroupBy(i => CompactForCompare(i.IsolatedClaim)).Select(g => g.First()).Take(40).ToList();

    private static List<GccPartnerAdvertisementAsset> DedupAds(List<GccPartnerAdvertisementAsset> items) =>
        items.GroupBy(i => CompactForCompare(i.MarketingHook)).Select(g => g.First()).Take(20).ToList();

    private static List<GccPartnerComparisonAsset> DedupComparisons(List<GccPartnerComparisonAsset> items) =>
        items.GroupBy(i => i.StandardizedFeatureId + "|" + CompactForCompare(i.CapabilityPayload))
            .Select(g => g.First()).Take(40).ToList();

    private static List<GccPartnerAlternativesAsset> DedupAlternatives(List<GccPartnerAlternativesAsset> items) =>
        items.GroupBy(i => CompactForCompare(i.TriggerDeficit)).Select(g => g.First()).Take(20).ToList();

    private static List<GccPartnerPricingTierAsset> DedupPricing(List<GccPartnerPricingTierAsset> items) =>
        items.GroupBy(i => $"{i.TierName}|{i.ListPrice}|{i.BillingPeriod}")
            .Select(g => g.First()).Take(30).ToList();

    private static List<GccPartnerIcpAsset> DedupIcp(List<GccPartnerIcpAsset> items) =>
        items.Take(10).ToList();

    private static List<GccPartnerIntegrationAsset> DedupIntegrations(List<GccPartnerIntegrationAsset> items) =>
        items.GroupBy(i => CompactForCompare(i.IntegrationName)).Select(g => g.First()).Take(40).ToList();

    private static List<GccPartnerFaqAsset> DedupFaqs(List<GccPartnerFaqAsset> items) =>
        items.GroupBy(i => CompactForCompare(i.Question)).Select(g => g.First()).Take(40).ToList();

    private static List<GccPartnerProofAsset> DedupProofs(List<GccPartnerProofAsset> items) =>
        items.GroupBy(i => i.ProofKind + "|" + CompactForCompare(i.ProofClaim))
            .Select(g => g.First()).Take(30).ToList();

    private static List<GccPartnerOfferCtaAsset> DedupOffers(List<GccPartnerOfferCtaAsset> items) =>
        items.GroupBy(i => CompactForCompare(i.CtaLabel) + "|" + i.DestinationUrl)
            .Select(g => g.First()).Take(20).ToList();

    private static List<GccPartnerDisqualifierAsset> DedupDisqualifiers(List<GccPartnerDisqualifierAsset> items) =>
        items.GroupBy(i => i.LimitType + "|" + CompactForCompare(i.LimitDetail))
            .Select(g => g.First()).Take(30).ToList();

    private static List<GccPartnerUseCasePlaybookAsset> DedupPlaybooks(List<GccPartnerUseCasePlaybookAsset> items) =>
        items.GroupBy(i => CompactForCompare(i.JobToBeDone)).Select(g => g.First()).Take(20).ToList();

    private static List<GccPartnerCategoryAsset> DedupCategories(List<GccPartnerCategoryAsset> items) =>
        items.GroupBy(i => CompactForCompare(i.PrimaryCategory)).Select(g => g.First()).Take(10).ToList();

    private static List<GccPartnerFreshnessAsset> DedupFreshness(List<GccPartnerFreshnessAsset> items) =>
        items.GroupBy(i => CompactForCompare(i.ChangeSummary)).Select(g => g.First()).Take(20).ToList();

    private static List<GccPartnerBattlecardSliceAsset> DedupBattlecards(List<GccPartnerBattlecardSliceAsset> items) =>
        items.GroupBy(i => CompactForCompare(i.WinTheme)).Select(g => g.First()).Take(10).ToList();

    private static List<GccPartnerDemoBeatAsset> DedupDemoBeats(List<GccPartnerDemoBeatAsset> items) =>
        items.GroupBy(i => CompactForCompare(i.BeatTitle)).Select(g => g.First()).Take(30).ToList();

    private static List<GccPartnerComplianceSnippetAsset> DedupCompliance(List<GccPartnerComplianceSnippetAsset> items) =>
        items.GroupBy(i => i.TermKind + "|" + CompactForCompare(i.TermText))
            .Select(g => g.First()).Take(20).ToList();

    private static List<GccPartnerAffiliateDisclosureAsset> DedupDisclosures(
        List<GccPartnerAffiliateDisclosureAsset> items) =>
        items.GroupBy(i => CompactForCompare(i.DisclosureText)).Select(g => g.First()).Take(10).ToList();

    [GeneratedRegex(@"\b(\d{1,3}(?:\.\d+)?%|\d{1,3}(?:,\d{3})+(?:\.\d+)?|\b99\.9%\b|\buptime\b.{0,40}\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MetricClaimRegex();

    [GeneratedRegex(@"\b(save|reduces?|automate|faster|instantly|without manual|in seconds|streamline)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BenefitHintRegex();

    [GeneratedRegex(@"\b(stop|start|get|try|build|create|launch)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ImperativeHintRegex();

    [GeneratedRegex(@"\b(tired of|frustrated|hit-or-miss|manually|waste|struggle|pain)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PainHintRegex();

    [GeneratedRegex(@"\b(start free trial|free trial|book a demo|request demo|get started|sign up|buy now|see pricing|start trial)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CtaLabelRegex();

    [GeneratedRegex(@"\b(lacks?|does not (include|support)|missing|limited to|no native|not available|cannot)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DeficitHintRegex();

    [GeneratedRegex(@"(?<currency>\$|USD|EUR|GBP|€|£)\s?(?<amount>\d{1,3}(?:,\d{3})*(?:\.\d{1,2})?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MoneyRegex();

    [GeneratedRegex(@"\b(?<period>monthly|annual|annually|yearly|per\s+month|per\s+year|usage|mo|yr|month|year)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BillingPeriodRegex();

    [GeneratedRegex(@"\b(?<tier>Free|Starter|Basic|Pro|Professional|Business|Enterprise|Team|Plus|Premium)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TierNameRegex();

    [GeneratedRegex(@"\b(\d+[-\s]?day\s+free\s+trial|free\s+trial|freemium|free\s+plan)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TrialRegex();

    [GeneratedRegex(@"\$?\d+(?:\.\d+)?\s*/\s*(1k|1,000|thousand)?\s*(words?|tokens?|seats?|minutes?|requests?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OverageRegex();

    [GeneratedRegex(@"\b(includes?|unlocks?|available on|only on|gated)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FeatureGateHintRegex();

    [GeneratedRegex(@"\b(as of|effective)\s+(?<asof>[A-Za-z0-9,\s\-/]{4,40})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AsOfRegex();

    [GeneratedRegex(@"\b(built for|designed for|made for|ideal for)\s+(?<seg>[^.!?\n]{3,80})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BuiltForRegex();

    [GeneratedRegex(@"\b(not for|isn't for|is not for)\s+(?<seg>[^.!?\n]{3,80})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NotForRegex();

    [GeneratedRegex(@"\b\d{1,5}\s*[-–to]+\s*\d{1,5}\s+(employees|seats|users)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CompanySizeRegex();

    [GeneratedRegex(@"\b(integrates? with|integration with|works with)\s+(?<name>[A-Z][\w.+-]*(?:\s+[A-Z][\w.+-]*){0,3})", RegexOptions.CultureInvariant)]
    private static partial Regex IntegratesWithRegex();

    [GeneratedRegex(@"\b(REST API|GraphQL|public API|[A-Za-z#.+]+ SDK)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ApiSdkRegex();

    [GeneratedRegex(@"\b(AppExchange|Azure Marketplace|AWS Marketplace|Google Cloud Marketplace|Zapier)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MarketplaceRegex();

    [GeneratedRegex(@"\b(SOC\s*2(?:\s+Type\s*II)?|ISO\s*27001|HIPAA|GDPR|PCI[\s-]?DSS)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CertificationRegex();

    [GeneratedRegex(@"\b(customers?|case study|roi|faster|reduced|increased)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CaseMetricHintRegex();

    [GeneratedRegex(@"\b(award|G2|Capterra|leader|named)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AwardHintRegex();

    [GeneratedRegex(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AbsoluteUrlRegex();

    [GeneratedRegex(@"\b(max(?:imum)?\s+\d+\s+seats?|up to\s+\d+\s+(seats?|users?))\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SeatLimitRegex();

    [GeneratedRegex(@"\b(US-?only|United States only|EEA only|available in)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RegionLimitRegex();

    [GeneratedRegex(@"\b(English only|Spanish only|languages?:|available in English|not available in)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LanguageLimitRegex();

    [GeneratedRegex(@"\b(how to|use case|playbook|workflow|for teams that)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UseCaseHintRegex();

    [GeneratedRegex(@"\b(category|software|platform|tool for)\s+(?<cat>[A-Za-z][A-Za-z0-9 /\-]{2,60})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CategoryHintRegex();

    [GeneratedRegex(@"\bvs\.?\s+(?<vs>[A-Za-z][A-Za-z0-9 /\-]{2,40})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VsCategoryRegex();

    [GeneratedRegex(@"\b(new pricing|price change|updated|changelog|now includes|no longer)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ChangeHintRegex();

    [GeneratedRegex(@"\b(best for|wins? when|ideal when|strong(?:est)? when)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WinThemeHintRegex();

    [GeneratedRegex(@"\b(pricing model|subscription|per[- ]seat|usage[- ]based|flat[- ]rate)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PricingModelHintRegex();

    [GeneratedRegex(@"\b(affiliate|commission|may earn|disclosure)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AffiliateDisclosureRegex();

    [GeneratedRegex(@"\b(how|what|why|when|do|does|can|is|are)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QuestionStartRegex();

    [GeneratedRegex(@"[\.!\?]+\s+")]
    private static partial Regex SentenceSplitRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
