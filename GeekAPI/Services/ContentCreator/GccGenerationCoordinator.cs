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
    private static GccCreateDto MergeRetrievedEvidence(GccCreateDto create, GccGroundingOutcome grounding)
    {
        if (grounding.Pages.Count == 0)
        {
            return create;
        }

        var existing = GccResearchFetchService.Deserialize(create.ResearchJson);
        var quoteables = existing?.Quoteables.ToList() ?? [];
        var seen = new HashSet<string>(quoteables.Select(q => q.Url), StringComparer.OrdinalIgnoreCase);

        foreach (var page in grounding.Pages)
        {
            if (seen.Add(page.Url))
            {
                quoteables.Add(page);
            }
        }

        var merged = existing is null
            ? new GccResearchDocument(null, quoteables)
            : existing with { Quoteables = quoteables };

        return create with { ResearchJson = GccResearchFetchService.Serialize(merged) };
    }

    /// <summary>
    /// Resolves grounding evidence for one content type and merges it into the create, refusing
    /// (never proceeding ungrounded) if the resolver says so. Called once per type by
    /// GenerateAndPersistOneAsync, whether that's the only type requested or one of several.
    /// </summary>
    private async Task<GccCreateDto> ResolveAndMergeGroundingAsync(
        GccCreateDto create, string contentType, CancellationToken ct)
    {
        var grounding = await _grounding.ResolveAsync(create, contentType, ct);
        if (grounding.Refused)
            throw new InvalidOperationException($"Refused: {grounding.Refusal}");
        return MergeRetrievedEvidence(create, grounding);
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
        Func<string, object, Task>? onTypeCompleted = null)
    {
        var requested = (outputTypes ?? [])
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // No default, no fallback -- an empty/omitted outputTypes used to silently fall back to
        // create.StartingContentType (the type the create happened to be minted with), which is
        // exactly the default-content-type pattern removed everywhere else this session. Refuse
        // instead: the frontend already disables Generate at zero selections, so this is only ever
        // reachable from a caller that skipped the UI, and it must fail closed the same as any
        // other missing-required-input case.
        if (requested.Count == 0)
            throw new InvalidOperationException(
                "Refused: at least one content type must be requested -- Generate has no default "
                + "or fallback type.");

        // Enforced, not just hidden in the picker -- checked before any generation starts, for
        // every item in the request (single or multi-select alike).
        var disabledRequested = requested
            .Where(GccGenerateService.IsContentTypeDisabledPendingImplementation)
            .ToList();
        if (disabledRequested.Count > 0)
            throw new InvalidOperationException(
                $"Refused: '{string.Join("', '", disabledRequested)}' "
                + "is disabled pending a written, approved resolve plan for its content-type quality.");

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
            var results = await Task.WhenAll(
                requested.Select(type =>
                    GenerateAndPersistOneAsync(
                        repo, gen, create, section, provider, type, mustMentionBlock, ct, onTypeCompleted)));
            return new { created = results };
        }

        // requested.Count is guaranteed 1 here: 0 was refused above, >1 returned above.
        return await GenerateAndPersistOneAsync(
            repo, gen, create, section, provider, requested[0], mustMentionBlock, ct, onTypeCompleted);
    }

    /// <summary>
    /// Generates and persists exactly one content type, fully independently -- the single unit both
    /// a single-select and a multi-select generate call use, once per requested type. Resolves its
    /// own grounding (never reused across types, since different types can require different
    /// evidence) and dispatches to that type's real generator; nothing here ever reads another
    /// type's output as input.
    /// </summary>
    private async Task<object> GenerateAndPersistOneAsync(
        HttpGccRepository repo,
        GccGenerateService gen,
        GccCreateDto create,
        SiteSectionContextDto? section,
        ContentGeneratorProvider provider,
        string requestedType,
        string? mustMentionBlock,
        CancellationToken ct,
        Func<string, object, Task>? onTypeCompleted = null)
    {
        var contentType = requestedType.Trim().ToLowerInvariant();
        // Strips hyphens/spaces so "tech-article"/"techArticle" and "email-cold-outreach"/"email"
        // (CONTENT_TYPES' real values once the picker unified onto it, Jeff: "they should be
        // identical") each reach one case, the same normalization the disabled-type check uses.
        var normalizedType = new string(contentType.Where(char.IsLetter).ToArray());

        // Grounding gate. Every grounding block downstream is conditional, so absent evidence used
        // to drop out silently and generation continued — a draft that reads identically whether
        // it was grounded or not. Required evidence is resolved here and its absence refuses.
        create = await ResolveAndMergeGroundingAsync(create, contentType, ct);

        string bodyJson;
        switch (normalizedType)
        {
            case "pillar":
                bodyJson = await gen.GeneratePillarBodyAsync(create, section, provider, mustMentionBlock, ct);
                // Per-H2 image prompts, merged into the document itself (Section.ImagePrompt) --
                // previously computed and discarded; bodyJson now carries the real result.
                bodyJson = await gen.GenerateSectionImagePromptsAsync(
                    "pillar", create.Topic, bodyJson, section, provider, ct);
                break;

            case "blog":
                // Same StartingContentType override tool/image-prompt already use below, extended
                // to every branch that reaches BuildAudience -- without it, a create originally
                // started as (say) "tool" would tell the model "Starting content type: tool" while
                // it was actually generating this blog post. The starting type is a mint-time fact
                // about the create; what BuildAudience should describe is what's being generated
                // right now, which is exactly what normalizedType/platform already say below.
                bodyJson = await gen.GenerateBlogBodyAsync(
                    create with { StartingContentType = "blog" }, section, provider, mustMentionBlock, ct);
                bodyJson = await gen.GenerateSectionImagePromptsAsync(
                    "blog", create.Topic, bodyJson, section, provider, ct);
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
                bodyJson = await gen.GenerateStartingContentAsync(
                    create with { StartingContentType = "tool" }, section, provider, ct, mustMentionBlock);
                break;

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

        var artifact = await repo.CreateArtifactAsync(
            new CreateGccArtifactCommand(create.Id, contentType, create.Topic), ct);
        var version = await repo.CreateVersionAsync(
            new CreateGccArtifactVersionCommand(artifact.Id, bodyJson), ct);
        var produced = new { artifact, version };
        // Announced the moment this type lands, not when every type does -- with several types
        // selected the caller can surface each artifact as it arrives instead of waiting on the
        // slowest one.
        if (onTypeCompleted is not null) await onTypeCompleted(contentType, produced);
        return produced;
    }

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
