using GeekAPI.HttpClients;

namespace GeekAPI.Services.ContentCreatorV2.Jobs;

/// <summary>Shared outline-approval rules for multi-draft creates (§5.1).</summary>
public static class GccV2OutlineApproval
{
    /// <summary>Sibling jobs on the same create that should advance when one outline is approved.</summary>
    public static IReadOnlyList<GccV2JobDto> SiblingJobsToAdvance(
        IReadOnlyList<GccV2JobDto> jobsOnCreate,
        Guid approvedJobId) =>
        jobsOnCreate
            .Where(j => j.Id != approvedJobId)
            .Where(j => string.Equals(j.Status, "awaiting_outline_approval", StringComparison.OrdinalIgnoreCase))
            .ToList();
}
