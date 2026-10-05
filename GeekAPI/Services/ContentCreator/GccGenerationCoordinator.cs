using System.Text.Json;
using GeekApplication.Interfaces.ContentWriterV3;
using GeekApplication.Models.ContentCreator;
using GeekAPI.Services.ContentCreatorV2;
using GeekAPI.HttpClients;
using GeekAPI.Services.Workflow.Providers;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// Orchestrates one Generate call: resolves grounding per content type, dispatches each requested
/// type to its real generator, and persists the artifact and version.
///
/// Lifted out of GccController so it can run somewhere other than inside an HTTP request. Generate
/// is minutes of work (partner extraction plus a multi-call write, multiplied by every selected
/// type), which no synchronous request survives -- see plans/generate-async-signalr.md. The
/// controller and the background job runner call the same method; nothing is duplicated between a
/// "sync path" and an "async path".
/// </summary>
public sealed class GccGenerationCoordinator
{
    private readonly GccGroundingResolver _grounding;
    private readonly ILogger<GccGenerationCoordinator> _logger;

    public GccGenerationCoordinator(
        GccGroundingResolver grounding,
        ILogger<GccGenerationCoordinator> logger)
    {
        _grounding = grounding;
        _logger = logger;
    }

    /// <summary>
    /// Folds retrieved, citable passages into the create's research so the existing quoteable
    /// prompt block (<c>GccGenerateService.cs:169</c>) carries them. Operator-uploaded quoteables
    /// are kept and retrieved ones appended; neither silently replaces the other.
    /// </summary>
    /// <remarks>internal so its contract can be asserted directly rather than through a copy of
    /// itself in a test — a duplicated merge is two implementations of one rule.</remarks>
    internal static GccCreateDto MergeRetrievedEvidence(GccCreateDto create, GccGroundingOutcome grounding)
    {
        if (grounding.Pages.Count == 0
            && grounding.CompetitorPages.Count == 0
            && grounding.SitePages.Count == 0)
        {
            return create;
        }

        var existing = GccResearchFetchService.Deserialize(create.ResearchJson);

        // Two lists, merged the same way and never into each other. A partner page is evidence to
        // cite; a competitor page is evidence to be different from, and the prompt blocks that
        // render them say opposite things about attribution.
        var quoteables = Merge(existing?.Quoteables, grounding.Pages);
        var competitors = Merge(existing?.CompetitorQuoteables, grounding.CompetitorPages);
        var site = Merge(existing?.SiteQuoteables, grounding.SitePages);

        var merged = existing is null
            ? new GccResearchDocument(
                null, quoteables, CompetitorQuoteables: competitors, SiteQuoteables: site)
            : existing with
            {
                Quoteables = quoteables,
                CompetitorQuoteables = competitors,
                SiteQuoteables = site,
            };

        return create with { ResearchJson = GccResearchFetchService.Serialize(merged) };
    }

    /// <summary>
    /// Additive by URL: what an operator already uploaded outranks what retrieval found at the same
    /// address, because the upload was a deliberate choice about this create.
    /// </summary>
    private static List<GccQuoteablePage> Merge(
        IReadOnlyList<GccQuoteablePage>? existing, IReadOnlyList<GccQuoteablePage> retrieved)
    {
        var pages = existing?.ToList() ?? [];
        var seen = new HashSet<string>(pages.Select(q => q.Url), StringComparer.OrdinalIgnoreCase);
        foreach (var page in retrieved)
        {
            if (seen.Add(page.Url))
            {
                pages.Add(page);
            }
        }
        return pages;
    }

    /// <summary>
    /// Resolves grounding evidence for every content type this generate will write, once, and
    /// merges it into the create -- refusing, never proceeding ungrounded, if the resolver says so.
    ///
    /// Returns the typed passages alongside the merged create because they do not fit in it. The
    /// create carries research as JSON, which is where the prompt's quoteable block reads from; a
    /// passage is a list of <c>Paragraph</c> records, so serializing it into that JSON would flatten
    /// the block structure the typed path exists to preserve. The tool page's block quotation needs
    /// the structure, so the passages travel beside the create rather than inside it.
    /// </summary>
    private async Task<(GccCreateDto Create, IReadOnlyList<GccGroundedPassage> PartnerPassages, IReadOnlyList<string> Warnings, IReadOnlyList<string> PartnersWithoutPassages)>
        ResolveAndMergeGroundingAsync(
            GccCreateDto create, IReadOnlyList<string> contentTypes, CancellationToken ct)
    {
        var grounding = await _grounding.ResolveAsync(create, contentTypes, ct);
        if (grounding.Refused)
            throw new InvalidOperationException($"Refused: {grounding.Refusal}");
        return (MergeRetrievedEvidence(create, grounding), grounding.PartnerPassages, grounding.Warnings,
            grounding.PartnersWithoutPassages ?? []);
    }

    /// <summary>
    /// The refusal a type takes for declared partners that returned no passage, or null.
    /// </summary>
    /// <remarks>
    /// Pillar and blog name every declared partner, so a partner with no evidence refuses the type,
    /// naming the partner, before any model call is paid for. Tool is not refused here: its fan-out
    /// refuses that partner's page by name and writes the rest.
    /// </remarks>
    internal static string? PartnerEvidenceRefusal(string contentType, IReadOnlyList<string> partnersWithoutPassages)
    {
        var type = new string((contentType ?? string.Empty).Where(char.IsLetter).ToArray()).ToLowerInvariant();
        if (partnersWithoutPassages.Count == 0 || type is not ("pillar" or "blog")) return null;

        return $"Refused: the {type} must name every declared partner, and "
            + string.Join(", ", partnersWithoutPassages)
            + " returned no passage for this topic. Re-crawl that partner, or remove it from the project, then retry.";
    }

    /// <summary>The label a retrieval warning travels under. It is about the create's evidence, which
    /// every requested type shares, so it is not attributed to any one of them.</summary>
    internal const string GroundingWarningLabel = "grounding";

    /// <summary>
    /// The Library's own warnings for this generate's retrieval, recorded and pushed like a piece's.
    /// </summary>
    /// <remarks>
    /// The resolver collected these and nothing read them: GccGroundingOutcome.Warnings had no
    /// consumer, so a degraded retrieval produced a draft indistinguishable from a clean one. They go
    /// where every other "saved, but with this" goes -- the result's <c>warnings</c> and the workspace
    /// event -- before any piece is written, so they are seen even if a piece then fails.
    /// </remarks>
    internal static async Task RecordGroundingWarningsAsync(
        IReadOnlyList<string> groundingWarnings,
        List<string> into,
        Func<string, string, Task>? onTypeWarning)
    {
        foreach (var warning in groundingWarnings)
        {
            into.Add($"{GroundingWarningLabel}: {warning}");
            if (onTypeWarning is not null) await onTypeWarning(GroundingWarningLabel, warning);
        }
    }

    /// <summary>Trimmed, de-duplicated, empty entries dropped. No default is ever substituted.</summary>
    public static List<string> NormalizeRequestedTypes(IReadOnlyList<string>? outputTypes) =>
        (outputTypes ?? [])
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// The refusals that cost nothing to decide, as a message or null. Public so Generate can
    /// answer them with a 400 before starting a background job -- a mistyped request should not
    /// become a job the operator has to watch fail. RunGenerateAsync calls the same method, so
    /// there is one definition rather than a controller copy drifting from the real gate.
    /// </summary>
    public static string? ValidateRequestedTypes(IReadOnlyList<string> requested)
    {
        // No default, no fallback -- an empty/omitted outputTypes used to silently fall back to
        // create.StartingContentType (the type the create happened to be minted with), which is
        // exactly the default-content-type pattern removed everywhere else. Refuse instead.
        if (requested.Count == 0)
            return "Refused: at least one content type must be requested -- Generate has no "
                + "default or fallback type.";

        // Enforced, not just hidden in the picker -- checked before any generation starts, for
        // every item in the request (single or multi-select alike).
        var disabled = requested
            .Where(GccGenerateService.IsContentTypeDisabledPendingImplementation)
            .ToList();
        if (disabled.Count > 0)
            return $"Refused: '{string.Join("', '", disabled)}' "
                + "is disabled pending a written, approved resolve plan for its content-type quality.";

        return null;
    }

    public async Task<object> RunGenerateAsync(
        HttpGccRepository repo,
        GccGenerateService gen,
        GccCreateDto create,
        SiteSectionContextDto? section,
        ContentGeneratorProvider provider,
        IReadOnlyList<string>? outputTypes,
        string? mustMentionBlock,
        CancellationToken ct,
        Func<string, object?, string?, Task>? onTypeOutcome = null,
        // The tool pre-flight's verdicts, pushed the moment they are known and before any tool page is
        // drafted. Separate from onTypeOutcome because that callback's contract is terminal -- a type
        // either produced an artifact or failed -- and a readiness report is neither.
        Func<string, IReadOnlyList<GccGenerateService.GccPartnerToolReadiness>, Task>? onReadiness = null,
        // A piece that was written with a gap the operator should see: a partner it never named, a
        // closing without the scheduler link. The piece is saved either way; this says what it ships
        // with, by type, the moment it is saved. Neither an outcome (the piece exists) nor a refusal.
        Func<string, string, Task>? onTypeWarning = null,
        // The project brief revision this run read, stamped on every version it writes (J7). Null on
        // the create-keyed path.
        GccBriefRevisionStamp? briefRevision = null)
    {
        var requested = NormalizeRequestedTypes(outputTypes);
        var refusal = ValidateRequestedTypes(requested);
        if (refusal is not null) throw new InvalidOperationException(refusal);

        // Decided here, where it costs nothing, rather than at the save, where it would cost the run.
        if (create.ProjectId is null) throw new InvalidOperationException(NoProjectRefusal);

        // Recorded as well as pushed. A hub event is live-only: an operator who reloads or reconnects
        // after the push would have no way back to the pre-flight, which is the same shape of loss as
        // a refusal that only ever reached a log. The job result carries it instead.
        var preflight = new List<GccGenerateService.GccPartnerToolReadiness>();
        Func<string, IReadOnlyList<GccGenerateService.GccPartnerToolReadiness>, Task> recordReadiness =
            async (type, verdicts) =>
            {
                preflight.AddRange(verdicts);
                if (onReadiness is not null) await onReadiness(type, verdicts);
            };

        if (requested.Count > 1)
        {
            // Every selected type is generated independently -- no "primary," nothing derived by
            // rewriting or repurposing another type's finished text. 2026-09-22 (Jeff, after this
            // came up twice: "Multi generate methods should not exist... you have created
            // spaghetti") -- the prior design picked one type as real and faked the rest from it
            // (rewrite-derivation for a second long-form type, a repurpose-pack for email/social/
            // ads, sourceContext contamination for Tool), which is the same defect class as
            // "Repurpose" itself (disabled entirely for it, GeekBackend 08187d9). Each call below
            // gets its own grounding resolution and its own real generator, exactly as if it were
            // the only thing selected -- literally the same method single-select calls once.
            //
            // Parallel, not sequential: every call is genuinely independent now (no shared mutable
            // state, nothing waits on another type's output), so awaiting them one at a time only
            // summed their durations for no reason. Real consequence of today's own redesign --
            // several selected types, previously one full generation plus cheap single-call
            // rewrites, now each run their own full generation sequence (Tool alone is up to four
            // sequential LLM calls) -- summed sequentially that's long enough to trip a timeout
            // somewhere between the browser and here, surfacing as an empty-body 500 with no
            // exception message at all (the connection dies before any response is written, so
            // neither of Generate's own catch blocks below ever gets the chance to run).
            // Generate every requested type first, persisting none of them. One failure fails the
            // whole request and leaves nothing behind -- Jeff, 2026-09-23, after three selected
            // types produced one saved page and a single error: "do not incur changes on failures.
            // One failure fails all, for now."
            //
            // Every failure is collected rather than the first one thrown, because Task.WhenAll
            // surfaces only whichever lost the race and discards the rest -- that is how Blog's
            // error vanished behind Pillar's. The refusal names every type that failed.
            // Resolved ONCE for the whole generate, not per type. Evidence is a property of the
            // create: every live content type retrieves the same three crawl types over the same
            // runs (RetrieveCrawlTypes), so resolving per type issued the same 21 vector queries
            // three times and discarded two of the answers. It also let one URL land in different
            // lists for different drafts -- see ResolveAsync's remarks.
            //
            // Before the fan-out rather than inside it, so a refusal costs nothing: the generate
            // stops before any paid model call instead of after two of three types have written.
            var resolved = await ResolveAndMergeGroundingAsync(create, requested, ct);
            create = resolved.Create;
            var groundingWarnings = new List<string>();
            await RecordGroundingWarningsAsync(resolved.Warnings, groundingWarnings, onTypeWarning);

            var attempts = await Task.WhenAll(requested.Select(async type =>
            {
                try
                {
                    if (PartnerEvidenceRefusal(type, resolved.PartnersWithoutPassages) is { } evidenceRefusal)
                        throw new InvalidOperationException(evidenceRefusal);
                    var generated = await GenerateOneAsync(
                        repo, gen, create, section, provider, type, mustMentionBlock,
                        resolved.PartnerPassages, ct, recordReadiness);
                    return (Type: type, Outcome: (TypeOutcome?)generated, Error: (string?)null);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex, "Generate failed for type {ContentType} on create {CreateId}", type, create.Id);
                    return (Type: type, Outcome: (TypeOutcome?)null, Error: (string?)ex.Message);
                }
            }));

            var failures = attempts.Where(a => a.Error is not null).ToList();
            if (failures.Count > 0)
                throw new InvalidOperationException(
                    string.Join(" | ", failures.Select(f => $"{f.Type}: {f.Error}")));

            // What the run wrote, with a link to a tool page the project does not have named on the
            // piece that carries it -- see WithMissingToolPagesNamedAsync.
            var pieces = await WithMissingToolPagesNamedAsync(
                repo, gen, create, [.. attempts.SelectMany(a => a.Outcome!.Pieces)], requested, ct);

            // Every piece of every type, in one write: all of them saved or none. Announced after it
            // succeeds, so nothing on the page says a piece exists that was not kept.
            var created = await PersistAllAsync(repo, create, pieces, provider, briefRevision, onTypeOutcome, ct);
            var refusals = new List<string>();
            var warnings = new List<string>(groundingWarnings);
            foreach (var attempt in attempts)
            {
                foreach (var piece in pieces.Where(p => p.ContentType == attempt.Type))
                {
                    foreach (var warning in WarningsOf(piece.BodyJson))
                    {
                        warnings.Add($"{attempt.Type}: {warning}");
                        if (onTypeWarning is not null) await onTypeWarning(attempt.Type, warning);
                    }
                }

                // Named, never swallowed: a partner whose page was not written is reported alongside the
                // ones that were, so five declared partners and four pages is visible rather than
                // something the operator has to count.
                foreach (var partnerRefusal in attempt.Outcome.SoftFailures)
                {
                    refusals.Add(partnerRefusal);
                    if (onTypeOutcome is not null) await onTypeOutcome(attempt.Type, null, partnerRefusal);
                }
            }

            return BuildGenerateResult(created, refusals, preflight, warnings);
        }

        // requested.Count is guaranteed 1 here: 0 was refused above, >1 returned above.
        var resolvedSingle = await ResolveAndMergeGroundingAsync(create, requested, ct);
        create = resolvedSingle.Create;
        var singleWarnings = new List<string>();
        await RecordGroundingWarningsAsync(resolvedSingle.Warnings, singleWarnings, onTypeWarning);
        if (PartnerEvidenceRefusal(requested[0], resolvedSingle.PartnersWithoutPassages) is { } singleRefusal)
            throw new InvalidOperationException(singleRefusal);
        var single = await GenerateOneAsync(
            repo, gen, create, section, provider, requested[0], mustMentionBlock,
            resolvedSingle.PartnerPassages, ct, recordReadiness);

        var singlePieces = await WithMissingToolPagesNamedAsync(repo, gen, create, single.Pieces, requested, ct);
        var singleCreated = await PersistAllAsync(
            repo, create, singlePieces, provider, briefRevision, onTypeOutcome, ct);
        foreach (var piece in singlePieces)
        {
            foreach (var warning in WarningsOf(piece.BodyJson))
            {
                singleWarnings.Add($"{requested[0]}: {warning}");
                if (onTypeWarning is not null) await onTypeWarning(requested[0], warning);
            }
        }

        foreach (var partnerRefusal in single.SoftFailures)
        {
            if (onTypeOutcome is not null) await onTypeOutcome(requested[0], null, partnerRefusal);
        }

        // One requested type can still be several artifacts -- tool is five. A bare object is returned
        // when it is one, so the existing single-select contract is unchanged for every other type.
        return single.Pieces.Count == 1 && single.SoftFailures.Count == 0 && preflight.Count == 0 && singleWarnings.Count == 0
            ? singleCreated[0]
            : BuildGenerateResult(singleCreated, single.SoftFailures, preflight, singleWarnings);
    }

    /// <summary>
    /// The warnings a generator wrote into its envelope -- <c>{ ..., "warnings": [string, ...] }</c>
    /// -- or none for a body that carries none, a bare document, or one that will not parse.
    /// </summary>
    internal static IReadOnlyList<string> WarningsOf(string? bodyJson)
    {
        if (string.IsNullOrWhiteSpace(bodyJson)) return [];
        try
        {
            using var doc = JsonDocument.Parse(bodyJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("warnings", out var warnings)
                || warnings.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return [.. warnings.EnumerateArray()
                .Where(w => w.ValueKind == JsonValueKind.String)
                .Select(w => w.GetString()!)
                .Where(w => !string.IsNullOrWhiteSpace(w))];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// The multi-artifact generate result: what was created, what was refused by name, and the
    /// pre-flight that decided it. One builder for both call sites so the two cannot disagree about
    /// the shape the frontend reads.
    /// </summary>
    /// <remarks>
    /// <c>refusals</c> and <c>preflight</c> are always present, empty included. An absent field and an
    /// empty one mean the same thing to a reader but need two code paths to handle, and the frontend
    /// dropped the refusals entirely once already by having no field to put them in.
    /// </remarks>
    private static object BuildGenerateResult(
        IReadOnlyList<object> created,
        IReadOnlyList<string> refusals,
        IReadOnlyList<GccGenerateService.GccPartnerToolReadiness> preflight,
        IReadOnlyList<string> warnings) =>
        new { created, refusals, preflight, warnings };

    /// <summary>
    /// Generates and persists exactly one content type, fully independently -- the single unit both
    /// a single-select and a multi-select generate call use, once per requested type. Dispatches to
    /// that type's real generator; nothing here ever reads another type's output as input.
    ///
    /// Grounding is NOT resolved here any more. It is resolved once for the whole generate by the
    /// caller and the merged create handed down, because the evidence belongs to the create rather
    /// than to the draft -- and every live type retrieves the same crawl types anyway. The typed
    /// passages come down beside it for the same reason, and for the one in
    /// ResolveAndMergeGroundingAsync's remarks: they cannot be carried inside the create.
    /// </summary>
    /// <summary>One artifact-to-be: a body, and the name the artifact carries.</summary>
    /// <param name="ArtifactName">
    /// The create's Topic for every type except tool, where it is the partner's product name — a tool
    /// page is about one product, and five pages all named after the keyword would be indistinguishable.
    /// </param>
    internal sealed record GeneratedPiece(string ContentType, string BodyJson, string ArtifactName);

    /// <summary>
    /// What one requested content type produced: usually one piece, five for tool.
    /// </summary>
    /// <param name="SoftFailures">
    /// Per-partner refusals that must not fail the generate. Reported to the operator, named, while the
    /// partners that did produce a page still persist — Jeff, 2026-10-02: each page stands alone. Empty
    /// for every other type, which keeps "one failure fails all" intact across types.
    /// </param>
    private sealed record TypeOutcome(
        IReadOnlyList<GeneratedPiece> Pieces, IReadOnlyList<string> SoftFailures);

    private async Task<TypeOutcome> GenerateOneAsync(
        HttpGccRepository repo,
        GccGenerateService gen,
        GccCreateDto create,
        SiteSectionContextDto? section,
        ContentGeneratorProvider provider,
        string requestedType,
        string? mustMentionBlock,
        IReadOnlyList<GccGroundedPassage> passages,
        CancellationToken ct,
        Func<string, IReadOnlyList<GccGenerateService.GccPartnerToolReadiness>, Task>? onReadiness = null)
    {
        var contentType = requestedType.Trim().ToLowerInvariant();
        // Strips hyphens/spaces so "tech-article"/"techArticle" and "email-cold-outreach"/"email"
        // (CONTENT_TYPES' real values once the picker unified onto it, Jeff: "they should be
        // identical") each reach one case, the same normalization the disabled-type check uses.
        var normalizedType = new string(contentType.Where(char.IsLetter).ToArray());

        string bodyJson;
        switch (normalizedType)
        {
            case "pillar":
                // Image prompts, metadata and JSON-LD all attach inside, as they do for Blog and
                // Tool -- the method returns a finished envelope, not a bare document. The same
                // StartingContentType override as every other branch: without it a create first
                // started as "tool" told the pillar writer "Starting content type: tool".
                bodyJson = await gen.GeneratePillarBodyAsync(
                    create with { StartingContentType = "pillar" }, section, provider, mustMentionBlock, ct);
                break;

            case "blog":
                // Same StartingContentType override tool/image-prompt already use below, extended
                // to every branch that reaches BuildAudience -- without it, a create originally
                // started as (say) "tool" would tell the model "Starting content type: tool" while
                // it was actually generating this blog post. The starting type is a mint-time fact
                // about the create; what BuildAudience should describe is what's being generated
                // right now, which is exactly what normalizedType/platform already say below.
                // Image prompts, metadata and JSON-LD are all attached inside, the way the tool
                // page does it -- the method returns a finished envelope, not a bare document.
                bodyJson = await gen.GenerateBlogBodyAsync(
                    create with { StartingContentType = "blog" }, section, provider, mustMentionBlock, ct);
                break;

            case "email" or "emailcoldoutreach":
                bodyJson = await gen.GenerateEmailAsync(
                    create with { StartingContentType = "email" }, section, provider, mustMentionBlock, ct);
                bodyJson = await AddImagePromptForContentAsync(gen, "email", create.Topic, bodyJson, section, provider, ct);
                break;

            // Every social/ads channel is its own independent post now, not one bundled into a
            // shared "pack" artifact with whichever other channels happened to be checked --
            // GenerateSocialPostAsync already takes any platform string, with dedicated style
            // guidance for linkedin/facebook and a generic fallback for the rest.
            case "linkedin" or "x" or "instagram" or "facebook" or "metaads" or "googleads" or "social" or "ads":
            {
                var platform = normalizedType switch
                {
                    "x" => "X",
                    "instagram" => "Instagram",
                    "metaads" => "MetaAds",
                    "googleads" => "GoogleAds",
                    _ => normalizedType, // "linkedin"/"facebook"/"social"/"ads" as-is
                };
                bodyJson = await gen.GenerateSocialPostAsync(
                    create with { StartingContentType = platform }, platform, section, provider, mustMentionBlock, ct);
                bodyJson = await AddImagePromptForContentAsync(gen, platform, create.Topic, bodyJson, section, provider, ct);
                break;
            }

            // Both route through GenerateStartingContentAsync's own internal dispatch, which already
            // fully owns tool's partner grounding + FAQ + per-H2 images and image-prompt's topic/
            // notes validation -- overriding StartingContentType guarantees the right internal
            // branch fires regardless of what the create was originally started as.
            case "aitool" or "tool":
            {
                // One page per declared partner, each about that product. The set-of-five path.
                var toolOutcomes = await gen.GenerateToolPagesPerPartnerAsync(
                    create, section, provider, ct, mustMentionBlock, passages,
                    onReadiness: onReadiness is null
                        ? null
                        : verdicts => onReadiness(contentType, verdicts));

                var written = toolOutcomes.Where(o => o.Written).ToList();
                var refused = toolOutcomes.Where(o => !o.Written).ToList();

                // Every partner refusing means the type produced nothing, which is a failure of the
                // type like any other. Some refusing is the expected shape of a real project.
                if (written.Count == 0)
                {
                    throw new InvalidOperationException(
                        string.Join(" | ", refused.Select(o => $"{o.ProductName}: {o.Refusal}")));
                }

                return new TypeOutcome(
                    [.. written.Select(o => new GeneratedPiece(contentType, o.BodyJson!, o.ProductName))],
                    [.. refused.Select(o => $"{o.ProductName}: {o.Refusal}")]);
            }

            case "imageprompt":
                bodyJson = await gen.GenerateStartingContentAsync(
                    create with { StartingContentType = "image-prompt" }, section, provider, ct, mustMentionBlock);
                break;

            default:
                // Generic fallback -- every type still routed here is disabled pending
                // content-type-dispatch-and-richness.md, kept only so a future re-enabled type
                // doesn't need this method touched again just to stop erroring. Same
                // StartingContentType override as every branch above, for the same reason.
                bodyJson = await gen.GenerateStartingContentAsync(
                    create with { StartingContentType = contentType }, section, provider, ct, mustMentionBlock);
                bodyJson = await gen.GenerateSectionImagePromptsAsync(
                    contentType, create.Topic, bodyJson, section, provider, ct);
                break;
        }

        // Nothing is persisted here. Generation and persistence are separate phases so that a
        // failure in any requested type leaves no artifacts behind at all (Jeff, 2026-09-23: "do
        // not incur changes on failures. One failure fails all, for now."). Persisting per type as
        // it finished is what left one page on disk when two other types failed.
        return new TypeOutcome([new GeneratedPiece(contentType, bodyJson, create.Topic)], []);
    }

    /// <summary>
    /// The run's pieces, with each pillar or blog that links a tool page the project does not have
    /// saying so in its own warnings.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The 2:32 PM run of 2026-10-05 wrote a pillar and a blog that both link
    /// <c>/tools/accounting/accounts-payable/approvalmax</c>, and refused the Approvalmax tool page in
    /// the same run. Each type is written on its own, at once, so the pillar cannot know which tool
    /// pages will pass; the link check allows every declared partner's path because every one is a
    /// page the project is meant to have. Whether it has it is known only when the run is over.
    /// </para>
    /// <para>
    /// A gap, not a refusal: the piece is sound and one link in it leads nowhere yet. It is saved
    /// with that written on it, and the operator generates the tool page or takes the link out.
    /// </para>
    /// </remarks>
    private static async Task<IReadOnlyList<GeneratedPiece>> WithMissingToolPagesNamedAsync(
        HttpGccRepository repo,
        GccGenerateService gen,
        GccCreateDto create,
        IReadOnlyList<GeneratedPiece> pieces,
        IReadOnlyList<string> requestedTypes,
        CancellationToken ct)
    {
        // Only a piece that is not itself a tool page links tool pages, and only a project has pages.
        if (create.ProjectId is not Guid projectId || pieces.All(p => IsToolType(p.ContentType))) return pieces;

        var partnerPages = await gen.PartnerToolPagesAsync(create, ct);
        if (partnerPages.Count == 0) return pieces;

        var onProject = (await repo.ListProjectArtifactsAsync(projectId, ct))
            .Where(a => IsToolType(a.Type))
            .Select(a => a.Name)
            .ToList();
        return NameMissingToolPages(pieces, partnerPages, onProject, requestedTypes.Any(IsToolType));
    }

    /// <summary>
    /// The decision <see cref="WithMissingToolPagesNamedAsync"/> makes, with nothing to read: which
    /// partner tool pages each piece links, and which of those the project will not have once this
    /// run is saved.
    /// </summary>
    /// <param name="toolPagesOnProject">The names of the tool pages the project already has.</param>
    /// <param name="toolPagesAskedFor">Whether this run was asked for tool pages, which decides what a
    /// missing one is called: refused by this run, or never generated.</param>
    internal static IReadOnlyList<GeneratedPiece> NameMissingToolPages(
        IReadOnlyList<GeneratedPiece> pieces,
        IReadOnlyList<GccPartnerToolPage> partnerPages,
        IReadOnlyCollection<string> toolPagesOnProject,
        bool toolPagesAskedFor)
    {
        var have = new HashSet<string>(toolPagesOnProject.Select(n => n.Trim()), StringComparer.OrdinalIgnoreCase);
        foreach (var written in pieces.Where(p => IsToolType(p.ContentType))) have.Add(written.ArtifactName.Trim());

        var result = new List<GeneratedPiece>(pieces.Count);
        foreach (var piece in pieces)
        {
            if (IsToolType(piece.ContentType))
            {
                result.Add(piece);
                continue;
            }

            var linked = LinksIn(piece.BodyJson);
            var missing = partnerPages
                .Where(page => linked.Contains(page.Path.TrimEnd('/')) && !have.Contains(page.ProductName))
                .Select(page => page.ProductName)
                .ToList();
            if (missing.Count == 0)
            {
                result.Add(piece);
                continue;
            }

            var why = toolPagesAskedFor
                ? "this run did not write it -- see what was not written"
                : "it has not been generated";
            var warning = missing.Count == 1
                ? $"Links to the {missing[0]} tool page, and this project has no {missing[0]} tool page: {why}. "
                  + "Generate it, or take the link out, before this is published."
                : $"Links to {missing.Count} tool pages this project does not have ({string.Join(", ", missing)}): "
                  + (toolPagesAskedFor
                      ? "this run did not write them -- see what was not written. "
                      : "they have not been generated. ")
                  + "Generate them, or take the links out, before this is published.";
            result.Add(piece with { BodyJson = WithWarning(piece.BodyJson, warning) });
        }

        return result;
    }

    private static bool IsToolType(string? type) =>
        string.Equals(type?.Trim(), "tool", StringComparison.OrdinalIgnoreCase);

    /// <summary>Every link in a body: each "href" in it, without a trailing slash.</summary>
    private static HashSet<string> LinksIn(string? bodyJson)
    {
        var links = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(bodyJson)) return links;
        try
        {
            using var doc = JsonDocument.Parse(bodyJson);
            Collect(doc.RootElement);
        }
        catch (JsonException)
        {
            // A body that is not JSON carries no links this can read; it is saved as it is.
        }

        return links;

        void Collect(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        if (property.NameEquals("href") && property.Value.ValueKind == JsonValueKind.String
                            && property.Value.GetString() is { Length: > 0 } href)
                        {
                            links.Add(href.Trim().TrimEnd('/'));
                        }
                        else
                        {
                            Collect(property.Value);
                        }
                    }

                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray()) Collect(item);
                    break;
            }
        }
    }

    /// <summary>
    /// A body with one more warning in its envelope's <c>warnings</c>, where <see cref="WarningsOf"/>
    /// reads them and the page shows them beside the version. A body that is not a JSON object has
    /// no envelope to carry one and is returned as it was.
    /// </summary>
    internal static string WithWarning(string bodyJson, string warning)
    {
        System.Text.Json.Nodes.JsonNode? root;
        try
        {
            root = System.Text.Json.Nodes.JsonNode.Parse(bodyJson);
        }
        catch (JsonException)
        {
            return bodyJson;
        }

        if (root is not System.Text.Json.Nodes.JsonObject envelope) return bodyJson;
        if (envelope["warnings"] is not System.Text.Json.Nodes.JsonArray warnings)
        {
            warnings = [];
            envelope["warnings"] = warnings;
        }

        warnings.Add(warning);
        return envelope.ToJsonString();
    }

    /// <summary>
    /// Every piece of one Generate, saved to the project's pages in one write.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A page, not a new draft.</b> Each piece was written as a new artifact with a first version,
    /// so a second Generate left a second pillar beside the first, under the same name
    /// (fix-project-persistence J1 says "Generate adds versions to the project"). GeekRepository now
    /// finds the project's page of that type and name and gives it its next version, or creates the
    /// page where there is none.
    /// </para>
    /// <para>
    /// <b>All or nothing.</b> The pieces were written one at a time, an artifact call and a version
    /// call each, so "one failure fails all" stopped being true at the first write: a fault on the
    /// fourth tool page left three saved under a run reported as failed. One call, one save; a
    /// refusal throws with what GeekRepository said, and nothing was kept.
    /// </para>
    /// <para>
    /// The page takes the piece's own name. It was always <c>create.Topic</c>, which was wrong even
    /// for the single tool page -- a tool page is about a product, not about the keyword -- and would
    /// make five partner pages one page.
    /// </para>
    /// </remarks>
    internal static async Task<List<object>> PersistAllAsync(
        HttpGccRepository repo,
        GccCreateDto create,
        IReadOnlyList<GeneratedPiece> pieces,
        ContentGeneratorProvider provider,
        GccBriefRevisionStamp? briefRevision,
        Func<string, object?, string?, Task>? onTypeOutcome,
        CancellationToken ct)
    {
        // A tool run whose every partner was refused wrote nothing; there is nothing to save, and
        // the refusals are reported by the caller.
        if (pieces.Count == 0) return [];

        var projectId = create.ProjectId ?? throw new InvalidOperationException(NoProjectRefusal);
        var metadata = GccVersionProvenance.For(provider, briefRevision);
        var result = await repo.SaveGeneratedPiecesAsync(
            projectId,
            new SaveGccGeneratedPiecesCommand(
                create.Id,
                [.. pieces.Select(p => new GccGeneratedPiece(p.ContentType, p.ArtifactName, p.BodyJson, metadata))]),
            ct);
        if (result is null)
        {
            throw new InvalidOperationException(
                $"None of the {pieces.Count} piece(s) was saved: the project no longer exists.");
        }

        if (result.Refusal is not null || result.Saved is null)
        {
            throw new InvalidOperationException(
                result.Refusal ?? $"None of the {pieces.Count} piece(s) was saved, and no reason was given.");
        }

        if (result.Saved.Count != pieces.Count)
        {
            throw new InvalidOperationException(
                $"The run wrote {pieces.Count} piece(s) and {result.Saved.Count} were reported saved. "
                + "What is on the project is not what this run can vouch for.");
        }

        var produced = new List<object>(result.Saved.Count);
        for (var i = 0; i < result.Saved.Count; i++)
        {
            var item = new { artifact = result.Saved[i].Artifact, version = result.Saved[i].Version };
            produced.Add(item);
            if (onTypeOutcome is not null) await onTypeOutcome(pieces[i].ContentType, item, null);
        }

        return produced;
    }

    /// <summary>What a run on a create with no project is told, before anything is written or spent.</summary>
    internal const string NoProjectRefusal =
        "Refused: this create is on no project. What a Generate writes is saved to a project's pages, "
        + "so there is nowhere to save it. Nothing was generated.";

    /// <summary>
    /// Attaches an <c>imagePrompt</c> field to a short-form body (email/social — a flat JSON
    /// object, not a <see cref="ContentDocument"/>, so there's no Section to merge into the way
    /// pillar/blog do). Previously computed the prompt via a real, paid LLM call and discarded
    /// it unconditionally ("image prompts can be stored separately") -- every email/LinkedIn/
    /// Facebook generation paid for a prompt nobody ever saw. Image-prompt failure still doesn't
    /// fail the whole generation (the primary content already succeeded), but it's now logged
    /// rather than silently swallowed, and cancellation propagates instead of being caught.
    /// </summary>
    private async Task<string> AddImagePromptForContentAsync(
        GccGenerateService gen,
        string contentType,
        string topic,
        string contentJson,
        SiteSectionContextDto? section,
        ContentGeneratorProvider provider,
        CancellationToken ct)
    {
        try
        {
            var imagePromptJson = await gen.GenerateImagePromptJsonAsync(
                topic, null, contentJson, provider, ct);
            return GccGenerateService.MergeImagePromptField(contentJson, imagePromptJson);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Image prompt generation is optional -- the primary content already succeeded and
            // must still be returned -- but a failure here should be visible, not silent.
            _logger.LogWarning(ex, "Image prompt generation failed for {ContentType}; content saved without one.", contentType);
            return contentJson;
        }
    }
}
