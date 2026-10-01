using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using GeekAPI.Services.ContentCreatorV2.Generation;
using GeekAPI.Services.GeekCrawler;
using GeekAPI.Services.Workflow.Providers;
using GeekApplication.Models.GeekCrawler;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// Can this partner's crawl answer the question the brief's Angle demands of the block quotation?
/// </summary>
/// <remarks>
/// <para>
/// Validation asks a volume question — is this URL indexed, with enough pages and chunks — and
/// volume is not fitness. A partner can carry nine thousand chunks and still say nothing that
/// answers <c>problem_solution</c> for this keyword. The Angle lives on the brief, so the real
/// question could not be asked where validation runs, and went unasked until Generate refused —
/// by which point the answer cost a paid draft and the operator could do nothing about it.
/// </para>
/// <para>
/// <b>The probe that validates is the probe that writes.</b> Same retrieval, same candidates, same
/// selection as the tool page will use, so a partner that validates cannot then fail generation for
/// want of a quote. Two implementations of "find the quote" would be two answers to one question,
/// which is how the halves of this pipeline have drifted before.
/// </para>
/// <para>
/// <b>The model selects a number.</b> Candidate spans are cut from the retrieved pages by
/// <see cref="GccQuoteCandidates"/>, numbered, and the selector returns the number of the one that
/// answers the angle. So the quotation is the system's own string and the cite is the page that
/// candidate came from — there is nothing to verify afterwards, because nothing was retyped and no
/// URL was ever supplied by the model.
/// </para>
/// <para>
/// Fail closed, with the distinction kept: <see cref="GccAngleQuoteOutcome.Unavailable"/> means the
/// question could not be asked — retrieval down, no provider — which is not the same answer as
/// <see cref="GccAngleQuoteOutcome.NoAnswer"/>, this partner having nothing that fits. Reporting
/// the first as the second sends an operator to re-crawl a partner whose evidence is fine.
/// </para>
/// </remarks>
public sealed class GccAngleQuoteProbe(
    IGeekCrawlerRagClient rag,
    GccTypedPassageReader typedPassages,
    IGccV2SchemaConstrainedGenerator generator,
    IContentProviderFactory providers,
    ILogger<GccAngleQuoteProbe> logger)
{
    private const string SchemaName = "angle-quote-selection-v1";

    /// <summary>
    /// Enough pages to give the selector a real choice without turning one validation click into a
    /// large prompt. Retrieval is already ranked, so the eighth page is rarely the one that fits.
    /// </summary>
    private const int TopK = 8;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    private const string SystemPrompt =
        "You are an SEO content strategist choosing ONE block quotation for a page about a partner's "
        + "product.\n\n"
        + "You are given a required angle and a numbered list of candidate sentences taken from that "
        + "partner's own published pages. Choose the ONE candidate that best answers what the angle "
        + "requires.\n\n"
        + "RULES:\n"
        + "1. Answer with the candidate's number. Do not write out the sentence, and do not edit, "
        + "shorten or combine candidates — the text is taken from the list, not from your reply.\n"
        + "2. Prefer a customer or named third party speaking. The partner's own published claim is "
        + "acceptable when no such candidate answers the angle.\n"
        + "3. A candidate that is merely true about the partner does not qualify. It has to answer "
        + "the angle's question.\n"
        + "4. If no candidate answers it, set hasQualifyingQuote to false and say why in whyItFits. "
        + "Returning false is a correct and expected answer — never stretch an unrelated candidate "
        + "to fill the field.";

    public async Task<GccAngleQuoteFinding> ProbeAsync(
        GccAngleQuoteSpec spec,
        string partnerUrl,
        Guid runId,
        CancellationToken ct)
    {
        if (runId == Guid.Empty)
        {
            return GccAngleQuoteFinding.Unavailable(
                partnerUrl, "no crawl run is known for this partner, so nothing could be retrieved");
        }

        IContentGenerationProvider provider;
        try
        {
            provider = providers.GetDefault();
        }
        catch (Exception cause)
        {
            logger.LogWarning(cause, "Angle quote probe skipped: no content provider available.");
            return GccAngleQuoteFinding.Unavailable(
                partnerUrl, "no content provider is available, so the question could not be asked");
        }

        var host = HostOf(partnerUrl);
        var retrieved = await rag.QueryAsync(
                spec.Need, runId, crawlType: CrawlTypes.Partner, host: host, topK: TopK, ct: ct)
            .ConfigureAwait(false);

        // Null or Failed is the index not answering. Empty pages on a successful call is the index
        // answering "nothing matches", which is a verdict about this partner and reads differently.
        if (retrieved is null || retrieved.Failed)
        {
            logger.LogWarning(
                "Angle quote probe could not retrieve for {Host} run {RunId}: {Error}",
                host, runId, retrieved?.Error ?? "no result");
            return GccAngleQuoteFinding.Unavailable(
                partnerUrl, "the index could not be reached, so this partner was not checked");
        }

        // Cut from the typed blocks, not from retrieved.Pages, because that is what the tool page
        // does -- see this class's remarks. Retrieval's page text is the prompt projection, labels
        // and all; the generate stopped reading it when the quote source became the blocks, so a
        // probe still reading it would be answering a different question than the one it reports on.
        var passages = await typedPassages
            .ReadAsync(runId, retrieved.Pages, ct)
            .ConfigureAwait(false);

        // Pages retrieved but none readable back is the crawl store not answering, not this partner
        // having nothing to say -- the Unavailable/NoAnswer distinction this class exists to keep.
        // Sending an operator to re-crawl a partner whose evidence is fine is the failure mode.
        if (retrieved.Pages.Count > 0 && passages.Count == 0)
        {
            logger.LogWarning(
                "Angle quote probe read no typed blocks for {Host} run {RunId} behind "
                + "{PageCount} retrieved page(s).",
                host, runId, retrieved.Pages.Count);
            return GccAngleQuoteFinding.Unavailable(
                partnerUrl,
                "this partner's crawled pages could not be read back, so the question could not "
                    + "be asked");
        }

        var candidates = GccQuoteCandidates.From(passages);
        if (candidates.Count == 0)
        {
            return GccAngleQuoteFinding.NoAnswer(
                partnerUrl,
                "nothing retrieved from this partner's site is shaped like a quotation — no "
                    + "complete sentence outside boilerplate");
        }

        GccAngleQuoteSelection selection;
        try
        {
            var completion = await generator.CompleteAsync<GccAngleQuoteSelection>(
                    new GccV2SchemaConstrainedRequest(
                        SystemPrompt: SystemPrompt,
                        UserPrompt: BuildUserPrompt(spec, candidates),
                        JsonSchema: GccV2AdHocJsonSchema.For<GccAngleQuoteSelection>(JsonOpts),
                        SchemaName: SchemaName,
                        Temperature: 0.1),
                    provider,
                    JsonOpts,
                    ct)
                .ConfigureAwait(false);
            selection = completion.Value;
        }
        catch (Exception cause) when (cause is not OperationCanceledException)
        {
            logger.LogWarning(cause, "Angle quote probe selection call failed for {Host}.", host);
            return GccAngleQuoteFinding.Unavailable(
                partnerUrl, $"selecting a quotation failed: {cause.GetType().Name}");
        }

        if (!selection.HasQualifyingQuote)
        {
            return GccAngleQuoteFinding.NoAnswer(
                partnerUrl,
                string.IsNullOrWhiteSpace(selection.WhyItFits)
                    ? "no retrieved passage answers this angle"
                    : selection.WhyItFits!.Trim());
        }

        if (selection.CandidateId is not { } id || id < 1 || id > candidates.Count)
        {
            return GccAngleQuoteFinding.NoAnswer(
                partnerUrl, "a quotation was claimed without naming which candidate it is");
        }

        // Read back by number. The text is the page's and the cite is that page — nothing here came
        // from the model but the number, so there is nothing to verify.
        var chosen = candidates[id - 1];
        return GccAngleQuoteFinding.Answered(partnerUrl, chosen.Text, chosen.PageUrl);
    }

    private static string BuildUserPrompt(GccAngleQuoteSpec spec, IReadOnlyList<GccQuoteCandidate> candidates)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Angle: {spec.Angle}");
        sb.AppendLine($"Required: choose {spec.Rule}.");
        sb.AppendLine();
        sb.AppendLine("<candidates>");
        foreach (var candidate in candidates)
        {
            sb.AppendLine($"{candidate.Id}. {candidate.Text}");
        }

        sb.AppendLine("</candidates>");
        return sb.ToString();
    }

    private static string? HostOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var parsed) ? parsed.Host : null;
}

/// <summary>What the selector returns: a number, or an explicit nothing.</summary>
/// <param name="HasQualifyingQuote">
/// False is a correct, expected answer. Without it, a model told to choose a quotation always
/// chooses one — which would make validation pass for every partner regardless of the evidence,
/// the exact failure this check exists to prevent.
/// </param>
/// <param name="CandidateId">The chosen candidate's number, from the list it was given.</param>
/// <param name="WhyItFits">Why it answers the angle — or, when none does, why nothing did.</param>
public sealed record GccAngleQuoteSelection(
    bool HasQualifyingQuote,
    int? CandidateId,
    string? WhyItFits);

public enum GccAngleQuoteOutcome
{
    /// <summary>A span answering the angle, with the page it is on.</summary>
    Answered,

    /// <summary>The question was asked and this partner has nothing that answers it.</summary>
    NoAnswer,

    /// <summary>The question could not be asked. Not a verdict on the partner.</summary>
    Unavailable,
}

/// <summary>One partner's answer to the angle's question.</summary>
public sealed record GccAngleQuoteFinding(
    string PartnerUrl,
    GccAngleQuoteOutcome Outcome,
    string? QuoteText,
    string? CiteUrl,
    string? Reason)
{
    public bool CanAnswer => Outcome == GccAngleQuoteOutcome.Answered;

    public static GccAngleQuoteFinding Answered(string partnerUrl, string quote, string citeUrl) =>
        new(partnerUrl, GccAngleQuoteOutcome.Answered, quote, citeUrl, null);

    public static GccAngleQuoteFinding NoAnswer(string partnerUrl, string reason) =>
        new(partnerUrl, GccAngleQuoteOutcome.NoAnswer, null, null, reason);

    public static GccAngleQuoteFinding Unavailable(string partnerUrl, string reason) =>
        new(partnerUrl, GccAngleQuoteOutcome.Unavailable, null, null, reason);
}
