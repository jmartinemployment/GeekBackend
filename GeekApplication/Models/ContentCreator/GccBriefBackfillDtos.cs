namespace GeekApplication.Models.ContentCreator;

/// <summary>
/// What the brief backfill (GR3) would do, project by project, read from the database and changing
/// nothing. Produced and read before the backfill runs.
/// </summary>
/// <remarks>
/// The backfill copies each project's newest create's brief, keyword, research and site section onto
/// the project, and writes every create's brief as a <c>backfill</c> revision carrying its own date.
/// Creates with no project are left alone and listed in <see cref="Unassigned"/>.
/// </remarks>
public sealed record GccBriefBackfillReport(
    DateTime GeneratedAtUtc,
    GccBriefBackfillCounts Counts,
    IReadOnlyList<GccBriefBackfillProject> Projects,
    IReadOnlyList<GccBriefBackfillUnassignedCreate> Unassigned);

/// <summary>Row counts as read. The backfill records the same counts before and after, and they must match.</summary>
public sealed record GccBriefBackfillCounts(
    int Projects,
    int ProjectsWithCreates,
    int ProjectsNeedingADecision,
    int Creates,
    int CreatesOnProjects,
    int CreatesUnassigned,
    int Artifacts,
    int Revisions);

/// <summary>One project that has at least one create, and what would happen to it.</summary>
/// <param name="Decisions">What the plan does not answer for this project, each as a sentence. Empty when
/// the copy is unambiguous. A project with any is not backfilled until Jeff has said what to do.</param>
public sealed record GccBriefBackfillProject(
    Guid ProjectId,
    string ProjectName,
    string ClientName,
    bool ProjectDeleted,
    bool ProjectHasBrief,
    int ProjectBriefVersion,
    DateTime? ProjectBriefSavedAtUtc,
    int ProjectRevisions,
    IReadOnlyList<string> Decisions,
    IReadOnlyList<GccBriefBackfillCreate> Creates);

/// <param name="Role"><c>current</c>: the newest create, whose brief becomes the project's.
/// <c>revision</c>: an older one, kept as a backfill revision only.</param>
public sealed record GccBriefBackfillCreate(
    Guid CreateId,
    string Topic,
    string StartingContentType,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    bool HasBrief,
    bool HasResearch,
    bool HasSiteSection,
    int Artifacts,
    string Role);

/// <summary>A create with no project: untouched by the backfill, for Jeff to assign or leave.</summary>
public sealed record GccBriefBackfillUnassignedCreate(
    Guid CreateId,
    string ClientName,
    string Topic,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    int Artifacts);

public static class GccBriefBackfillRoles
{
    public const string Current = "current";
    public const string Revision = "revision";
}
