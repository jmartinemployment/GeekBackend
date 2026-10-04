using System.Text.Json;
using GeekApplication.Interfaces.ContentWriterV3;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// What a generated version records about how it was made.
/// </summary>
/// <remarks>
/// <para>
/// Shared because there are two paths that create a version — the generate path through
/// <c>GccGenerationCoordinator.PersistOneAsync</c>, and revise through <c>GccController</c> — and the
/// second one shipped without the stamp. The label it feeds exists to tell two drafts apart, so a
/// revise that drops it removes the feature for the rest of that create's life: the workspace shows
/// the highest version number, and that is the one with no provenance.
/// </para>
/// <para>
/// In the version's <c>MetadataJson</c> rather than a column: <c>gcc_artifacts</c> lives in
/// GeekRepository, so a field there is a cross-service schema change, while <c>MetadataJson</c>
/// already travels on <c>CreateGccArtifactVersionCommand</c>.
/// </para>
/// <para>
/// The <b>provider</b>, not the model. <c>ChatCompletionResult.ModelUsed</c> carries the real id but
/// is only threaded through the dormant v2 writer, so naming a model here would mean inventing one.
/// Provider is enough to tell drafts apart while one model is configured per provider; when that stops
/// being true, this is the one place the model goes.
/// </para>
/// </remarks>
public static class GccVersionProvenance
{
    /// <summary>The metadata a new version carries, as JSON.</summary>
    /// <param name="briefRevision">The project brief revision the version was written from (J7), so the
    /// workspace can say "generated from the brief saved at ...". Null on the create-keyed path, whose
    /// brief has no revisions.</param>
    public static string For(ContentGeneratorProvider provider, GccBriefRevisionStamp? briefRevision = null) =>
        briefRevision is null
            ? JsonSerializer.Serialize(new { generatedByProvider = provider.ToString() })
            : JsonSerializer.Serialize(new
            {
                generatedByProvider = provider.ToString(),
                briefRevisionId = briefRevision.Id,
                briefRevisionSavedAtUtc = briefRevision.SavedAtUtc,
            });
}

/// <summary>The brief revision a run read: its id and when it was saved.</summary>
public sealed record GccBriefRevisionStamp(Guid Id, DateTime SavedAtUtc);
