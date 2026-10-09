using System.Text.Json;
using GeekApplication.Interfaces.ContentWriterV3;
using GeekApplication.Models.ContentCreator;
using GeekAPI.Services.ContentCreatorV2;
using GeekAPI.HttpClients;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services;

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
            && grounding.SitePages.Count == 0
            && grounding.PublisherPositions is not { Count: > 0 })
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
        // Replaced, not merged: the publisher's positions are read from the site page on every run, and
        // nothing an operator uploads stands in for what their own site says.
        var positions = grounding.PublisherPositions is { Count: > 0 } read ? read : existing?.PublisherPositions;

        var merged = existing is null
            ? new GccResearchDocument(
                null, quoteables, CompetitorQuoteables: competitors, SiteQuoteables: site, PublisherPositions: positions)
            : existing with
            {
                Quoteables = quoteables,
                CompetitorQuoteables = competitors,
                SiteQuoteables = site,
                PublisherPositions = positions,
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
        GccBriefRevisionStamp? briefRevision = null,
        // When tool pages are requested: only these partners, by declared URL. Null is every partner.
        // Jeff, 2026-10-09: "Seeing as a single tool can fail, need a way to select just one tool."
        IReadOnlyList<string>? toolPartners = null)
    {
        var requested = NormalizeRequestedTypes(outputTypes);
        var refusal = ValidateRequestedTypes(requested);
        if (refusal is not null) throw new InvalidOperationException(refusal);

        // Decided here, where it costs nothing, rather than at the save, where it would cost the run.
        if (create.ProjectId is null) throw new InvalidOperationException(NoProjectRefusal);

        // The old pages of every requested type go now, before a word is written (Jeff, 2026-10-06:
        // "delete should happen first"). A run that is then refused leaves the project with no page of
        // that type and a failed job that says why -- not the previous run's page, read as this one's.
        var deleted = await DeleteOldPagesAsync(repo, create, requested, ct);
        _logger.LogInformation(
            "Generate on project {ProjectId} deleted {Pages} page(s) and {Derived} derived page(s) of {Types} before writing",
            create.ProjectId, deleted.Pages, deleted.DerivedPages, string.Join(", ", requested));

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
            // gets its own real generator, exactly as if it were the only thing selected -- literally
            // the same method single-select calls once.
            //
            // Parallel, not sequential: every call is genuinely independent (no shared mutable state,
            // nothing waits on another type's output), so awaiting them one at a time only summed
            // their durations for no reason, long enough to trip a timeout somewhere between the
            // browser and here.
            //
            // Evidence is resolved ONCE for the whole generate, not per type. It is a property of the
            // create: every live content type retrieves the same three crawl types over the same
            // runs (RetrieveCrawlTypes), so resolving per type issued the same 21 vector queries
            // three times and discarded two of the answers. It also let one URL land in different
            // lists for different drafts -- see ResolveAsync's remarks. Before the fan-out rather
            // than inside it, so a refusal costs nothing: the generate stops before any paid model
            // call instead of after two of three types have written.
            var resolved = await ResolveAndMergeGroundingAsync(create, requested, ct);
            create = resolved.Create;
            var groundingWarnings = new List<string>();
            await RecordGroundingWarningsAsync(resolved.Warnings, groundingWarnings, onTypeWarning);
            await GccRunLog.RecordIfAnyAsync("grounding", GroundingRecord(resolved), piece: null);

            // Every type is attempted, and what a type cannot write is that type's refusal, not the
            // run's (Jeff, 2026-10-06 and 2026-10-07; AGENTS.md "Content Creator pages"). Every failure
            // is kept rather than the first, because Task.WhenAll surfaces only whichever lost the race
            // and discards the rest -- that is how Blog's error vanished behind Pillar's.
            var createId = create.Id;
            var attempts = await Task.WhenAll(requested.Select(type => AttemptAsync(_logger, type, createId, async () =>
            {
                if (PartnerEvidenceRefusal(type, resolved.PartnersWithoutPassages) is { } evidenceRefusal)
                    throw new InvalidOperationException(evidenceRefusal);
                return await GenerateOneAsync(
                    repo, gen, create, section, provider, type, mustMentionBlock,
                    resolved.PartnerPassages, ct, recordReadiness, toolPartners);
            })));

            return await SettleAndSaveAsync(
                repo, gen, create, requested, attempts, groundingWarnings, preflight, provider,
                briefRevision, onTypeOutcome, onTypeWarning, _logger, ct);
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
            resolvedSingle.PartnerPassages, ct, recordReadiness, toolPartners);

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
    /// What a multi-type run does once every type has been attempted: settle, save what wrote in one
    /// write, say what was not written. Apart from the fan-out so the decision to keep what wrote is
    /// tested through the real save rather than inferred from the pieces of it.
    /// </summary>
    internal static async Task<object> SettleAndSaveAsync(
        HttpGccRepository repo,
        GccGenerateService gen,
        GccCreateDto create,
        IReadOnlyList<string> requested,
        IReadOnlyList<GccRunSettlement.TypeAttempt> attempts,
        IReadOnlyList<string> groundingWarnings,
        IReadOnlyList<GccGenerateService.GccPartnerToolReadiness> preflight,
        ContentGeneratorProvider provider,
        GccBriefRevisionStamp? briefRevision,
        Func<string, object?, string?, Task>? onTypeOutcome,
        Func<string, string, Task>? onTypeWarning,
        ILogger logger,
        CancellationToken ct)
    {
        // Throws, saying why for each type, only when no type wrote anything. Before any repository
        // read, so a run with nothing to save asks the repository for nothing.
        var settled = GccRunSettlement.Settle(attempts);

        // What the run wrote, with a link to a tool page the project does not have named on the
        // piece that carries it -- see WithMissingToolPagesNamedAsync. Advisory: it reads the project's
        // pages to add a warning, and a read that fails must not discard pages that were written.
        var runWarnings = new List<string>(groundingWarnings);
        IReadOnlyList<GeneratedPiece> pieces;
        try
        {
            pieces = await WithMissingToolPagesNamedAsync(repo, gen, create, settled.Pieces, requested, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The link check against the project's tool pages failed on create {CreateId}", create.Id);
            await GccRunLog.RecordIfAnyAsync("warning", new { step = "tool-page link check", fault = GccRunFault.Describe(ex) });
            runWarnings.Add($"Links to tool pages were not checked ({ex.Message}); check them before publishing.");
            pieces = settled.Pieces;
        }

        // Every piece that was written, in one write: all of them saved or none. Announced after it
        // succeeds, so nothing on the page says a piece exists that was not kept.
        var created = await PersistAllAsync(repo, create, pieces, provider, briefRevision, onTypeOutcome, ct);
        foreach (var piece in pieces)
        {
            foreach (var warning in WarningsOf(piece.BodyJson))
            {
                runWarnings.Add($"{piece.ContentType}: {warning}");
                if (onTypeWarning is not null) await onTypeWarning(piece.ContentType, warning);
            }
        }

        // Named, never swallowed: a type that was not written, and a partner whose page was not, are
        // reported alongside what was, so five requested types and three pages is visible rather
        // than something the operator has to count.
        var refusals = new List<string>();
        foreach (var refused in settled.Refusals)
        {
            refusals.Add(refused.Line);
            if (onTypeOutcome is not null) await onTypeOutcome(refused.Type, null, refused.Text);
        }

        // After the save, so it never claims a piece that was not kept.
        await GccRunLog.RecordIfAnyAsync("settled", new
        {
            saved = pieces.Select(p => new { type = p.ContentType, name = p.ArtifactName }).ToList(),
            refused = settled.Refusals.Select(r => new { type = r.Type, reason = r.Text }).ToList(),
        });

        return BuildGenerateResult(created, refusals, preflight, runWarnings);
    }

    /// <summary>
    /// One requested type's attempt. A type that cannot be written is that type's refusal, recorded in
    /// the run's log, not an exception for the run to die of; cancellation is the one thing that is.
    /// </summary>
    /// <remarks>
    /// The record keeps what happened, not only what was said: a refusal ("Refused: the pillar. ...")
    /// carries its message, and any other exception carries its type, its stack and its inner exceptions
    /// (<see cref="GccRunFault"/>), so a fault in the code can be found from the run's record without a
    /// reproduction.
    /// </remarks>
    internal static async Task<GccRunSettlement.TypeAttempt> AttemptAsync(
        ILogger logger, string type, Guid createId, Func<Task<TypeOutcome>> write)
    {
        using var piece = GccRunLog.ForPiece(type);
        using var repairs = JsonRepairTrace.Begin();
        try
        {
            var generated = await write();
            await RecordRepairsAsync(repairs);
            await GccRunLog.RecordIfAnyAsync("outcome", new
            {
                type,
                written = generated.Pieces.Select(p => new { p.ArtifactName, words = WordsOf(p.BodyJson) }).ToList(),
                refused = generated.SoftFailures,
            });
            return GccRunSettlement.TypeAttempt.Wrote(type, generated);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (GccRunFault.IsRefusal(ex))
            {
                logger.LogWarning("Generate refused type {ContentType} on create {CreateId}: {Reason}", type, createId, ex.Message);
            }
            else
            {
                logger.LogError(ex, "Generate failed for type {ContentType} on create {CreateId}", type, createId);
            }

            await RecordRepairsAsync(repairs);
            await GccRunLog.RecordIfAnyAsync("outcome", new { type, error = ex.Message, fault = GccRunFault.Describe(ex) });
            return GccRunSettlement.TypeAttempt.Refused(type, ex.Message);
        }
    }

    /// <summary>
    /// Writes the replies this piece needed a repair for, if any, as one <c>repaired</c> event naming the
    /// call and the repairs. A reply that parsed only after a repair read in the record exactly like one
    /// that was well formed; the model's own text is in the <c>call</c> event beside it.
    /// </summary>
    private static Task RecordRepairsAsync(JsonRepairTrace.Scope repairs)
    {
        var applied = repairs.Drain();
        return applied.Count == 0
            ? Task.CompletedTask
            : GccRunLog.RecordIfAnyAsync(
                "repaired",
                new { repairs = applied.Select(a => new { label = a.Label, repairs = a.Repairs }).ToList() });
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
    /// for every other type, whose refusal is the whole type's (see <see cref="GccRunSettlement"/>).
    /// </param>
    internal sealed record TypeOutcome(
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
        Func<string, IReadOnlyList<GccGenerateService.GccPartnerToolReadiness>, Task>? onReadiness = null,
        IReadOnlyList<string>? toolPartners = null)
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
                        : verdicts => onReadiness(contentType, verdicts),
                    onlyPartners: toolPartners);

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

        // Nothing is persisted here. Generation and persistence are separate phases, so the run
        // saves what it wrote in one write after every type has been attempted
        // (GccRunSettlement). Persisting per type as it finished is what left a page on disk with
        // no record of which run wrote it.
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
    /// so a second Generate left a second pillar beside the first, under the same name. GeekRepository
    /// now finds the project's page of that type and name and replaces its content -- the old text,
    /// its evidence and its approvals are deleted (Jeff, 2026-10-06: no history) -- or creates the
    /// page where there is none.
    /// </para>
    /// <para>
    /// <b>All of the pieces given, or none of them.</b> The pieces were written one at a time, an
    /// artifact call and a version call each, so a fault on the fourth tool page left three saved
    /// under a run reported as failed. One call, one save; a refusal throws with what GeekRepository
    /// said, and nothing was kept. Which pieces are given is decided before this, by
    /// <see cref="GccRunSettlement"/>: a type that was refused is not among them.
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
            // What a run records of a piece is the page it went to and the version it became -- not
            // the page's text. The whole version was here, so every body a run wrote was also in the
            // run's result, in its stored row, in the per-piece hub event and in the terminal one:
            // six pages was some 130 KB carried four times, and read back again by a page that only
            // wants to know what the run did. The text is read from the version, where it lives.
            var version = result.Saved[i].Version;
            var item = new
            {
                artifact = result.Saved[i].Artifact,
                version = new
                {
                    version.Id,
                    version.ArtifactId,
                    version.VersionNumber,
                    version.MetadataJson,
                    version.CreatedAtUtc,
                },
            };
            produced.Add(item);
            if (onTypeOutcome is not null) await onTypeOutcome(pieces[i].ContentType, item, null);
        }

        return produced;
    }

    /// <summary>What the run was grounded on, for its record: counts, names and the warnings, not the text.</summary>
    private static object GroundingRecord(
        (GccCreateDto Create, IReadOnlyList<GccGroundedPassage> PartnerPassages, IReadOnlyList<string> Warnings, IReadOnlyList<string> PartnersWithoutPassages) resolved) => new
    {
        partnerPassages = resolved.PartnerPassages.Count,
        partnersWithoutPassages = resolved.PartnersWithoutPassages,
        warnings = resolved.Warnings,
        research = GroundingCounts(resolved.Create),
    };

    private static object? GroundingCounts(GccCreateDto create)
    {
        var research = GccResearchFetchService.Deserialize(create.ResearchJson);
        if (research is null) return null;
        return new
        {
            partnerQuoteables = research.Quoteables?.Count ?? 0,
            competitorQuoteables = research.CompetitorQuoteables?.Count ?? 0,
            siteQuoteables = research.SiteQuoteables?.Count ?? 0,
            publisherPositions = (research.PublisherPositions ?? []).Select(p => p.Heading).ToList(),
        };
    }

    private static readonly JsonSerializerOptions RecordJson = new(JsonSerializerDefaults.Web);

    private static int WordsOf(string bodyJson)
    {
        var document = GccBodyEnvelope.Read(bodyJson, RecordJson).Document;
        return document is null ? 0 : GeekAPI.Services.Workflow.Services.ContentDocumentText.CountWords(document);
    }

    /// <summary>
    /// The project's pages of every requested type, deleted before the run writes anything.
    /// </summary>
    /// <remarks>
    /// Jeff, 2026-10-06: "delete should happen first". Pieces are saved under the requested type's own
    /// string, so the types requested are the types deleted. A project that is gone fails the run here,
    /// before a model call; a repository that cannot be reached does the same.
    /// </remarks>
    internal static async Task<GccPagesDeleteResult> DeleteOldPagesAsync(
        HttpGccRepository repo, GccCreateDto create, IReadOnlyList<string> requestedTypes, CancellationToken ct)
    {
        if (create.ProjectId is not Guid projectId) throw new InvalidOperationException(NoProjectRefusal);
        if (requestedTypes.Count == 0) return new GccPagesDeleteResult(false, 0, 0);

        return await repo.DeleteProjectPagesAsync(projectId, requestedTypes, ct)
            ?? throw new InvalidOperationException(
                $"Refused: project {projectId} no longer exists, so there is nowhere to write to. Nothing was generated.");
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
            await GccRunLog.RecordIfAnyAsync("warning", new { step = "image prompt", contentType, fault = GccRunFault.Describe(ex) });
            return contentJson;
        }
    }
}
