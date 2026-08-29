namespace GeekAPI.Services.ContentCreatorV2.Plan;

/// <summary>Validates operator-edited outline payloads before PLAN output is persisted.</summary>
public static class GccV2OutlinePutValidator
{
    /// <summary>
    /// Returns an error message when roles are invalid; null when acceptable.
    /// When no section declares a job, legacy outlines pass through unchanged.
    /// </summary>
    public static string? ValidatePutOutlineSections(IReadOnlyList<string?> jobs)
    {
        if (jobs.Count == 0) return null;

        var hasDeclaredJob = jobs.Any(j => !string.IsNullOrWhiteSpace(j));
        if (!hasDeclaredJob) return null;

        var problemCount = jobs.Count(j =>
            string.Equals(j, "problem", StringComparison.OrdinalIgnoreCase));
        if (problemCount != 1)
            return "Outline must include exactly one problem section.";

        if (!string.Equals(jobs[0], "problem", StringComparison.OrdinalIgnoreCase))
            return "First outline section must be the problem role.";

        return null;
    }
}
