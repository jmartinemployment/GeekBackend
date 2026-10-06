namespace GeekApplication.Models.ContentCreator;

/// <summary>
/// One piece a Generate wrote, to be saved to its page on the project.
/// </summary>
/// <param name="Type">The content type: pillar, blog, tool.</param>
/// <param name="Name">The page's name: the keyword for a pillar or a blog, the product for a tool page.
/// With <paramref name="Type"/> it says which page of the project this is.</param>
public sealed record GccGeneratedPiece(string Type, string Name, string BodyDocumentJson, string? MetadataJson = null);

/// <summary>Every piece of one Generate, saved together or not at all.</summary>
/// <param name="CreateId">The create a page that does not exist yet is stored under. Drafts are keyed by
/// create until they are keyed to the project (fix-project-persistence GR4); it must be one of the
/// project's own.</param>
public sealed record SaveGccGeneratedPiecesCommand(Guid CreateId, IReadOnlyList<GccGeneratedPiece> Pieces);

/// <summary>A piece as saved: the page it went to and the version that is now its content.</summary>
/// <param name="NewPage">True when the project had no page of this type and name before this run; false when
/// the run replaced the page's content.</param>
public sealed record GccSavedPieceDto(GccArtifactDto Artifact, GccArtifactVersionDto Version, bool NewPage);

/// <summary>A run's pieces saved, or why none of them was. Nothing is written on a refusal.</summary>
public sealed record GccGeneratedPiecesSaveResult(
    IReadOnlyList<GccSavedPieceDto>? Saved, bool ProjectNotFound, string? Refusal)
{
    public static GccGeneratedPiecesSaveResult Written(IReadOnlyList<GccSavedPieceDto> saved) => new(saved, false, null);
    public static GccGeneratedPiecesSaveResult Missing() => new(null, true, null);
    public static GccGeneratedPiecesSaveResult Refused(string why) => new(null, false, why);
}
