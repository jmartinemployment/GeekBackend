namespace GeekApplication.Models.ContentCreator;

/// <summary>
/// One piece a Generate wrote, to be saved to its page on the project.
/// </summary>
/// <param name="Type">The content type: pillar, blog, tool.</param>
/// <param name="Name">The page's name: the keyword for a pillar or a blog, the product for a tool page.
/// With <paramref name="Type"/> it says which page of the project this is.</param>
/// <param name="ParentPageId">Null for a page of the project. For a piece a Repurpose wrote from a page --
/// an email, a social post, an ad -- the page it was written from: the piece is saved under that page,
/// one of each type and name, and goes when the page does.</param>
public sealed record GccGeneratedPiece(
    string Type, string Name, string BodyDocumentJson, string? MetadataJson = null, Guid? ParentPageId = null);

/// <summary>Every piece of one Generate, saved together or not at all.</summary>
/// <param name="CreateId">The create a page that does not exist yet is stored under. Drafts are keyed by
/// create until they are keyed to the project (fix-project-persistence GR4); it must be one of the
/// project's own.</param>
public sealed record SaveGccGeneratedPiecesCommand(Guid CreateId, IReadOnlyList<GccGeneratedPiece> Pieces);

/// <summary>A piece as saved: the page it went to and the version that is now its content.</summary>
/// <param name="NewPage">True when the project had no page of this type and name before this run; false when
/// the run replaced the page's content.</param>
public sealed record GccSavedPieceDto(GccArtifactDto Artifact, GccArtifactVersionDto Version, bool NewPage);

/// <summary>What deleting a project's pages of some types removed.</summary>
/// <param name="Pages">Pages of the named types that were deleted, with their content.</param>
/// <param name="DerivedPages">Pages derived from one of those -- a social post cut from the pillar -- deleted with it.</param>
public sealed record GccPagesDeleteResult(bool ProjectNotFound, int Pages, int DerivedPages)
{
    public static GccPagesDeleteResult Missing() => new(true, 0, 0);
}

/// <summary>What deleting the pieces written from one page removed.</summary>
/// <param name="PageNotFound">The project has no such page: nothing was deleted.</param>
/// <param name="Pieces">The pieces of the named types under that page that were deleted, with their content.</param>
public sealed record GccPiecesDeleteResult(bool PageNotFound, int Pieces)
{
    public static GccPiecesDeleteResult Missing() => new(true, 0);
}

/// <summary>A run's pieces saved, or why none of them was. Nothing is written on a refusal.</summary>
public sealed record GccGeneratedPiecesSaveResult(
    IReadOnlyList<GccSavedPieceDto>? Saved, bool ProjectNotFound, string? Refusal)
{
    public static GccGeneratedPiecesSaveResult Written(IReadOnlyList<GccSavedPieceDto> saved) => new(saved, false, null);
    public static GccGeneratedPiecesSaveResult Missing() => new(null, true, null);
    public static GccGeneratedPiecesSaveResult Refused(string why) => new(null, false, why);
}
