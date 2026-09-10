using System.Text.Json;

namespace GeekAPI.Services.ContentCreatorV2.Context;

/// <summary>
/// Product IQ claim and disclaimer policy: approved claims, prohibited claims,
/// and mandatory disclaimers attached to product versions.
/// </summary>
public static class GccV2ProductClaimPolicy
{
    public static IReadOnlyList<string> ParseStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            var parsed = JsonSerializer.Deserialize<List<string>>(json) ?? [];
            return parsed
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// Authoring validation for Product IQ claim/disclaimer lists.
    /// </summary>
    public static string? ValidateAuthoring(
        IEnumerable<string>? approvedClaims,
        IEnumerable<string>? prohibitedClaims,
        IEnumerable<string>? mandatoryDisclaimers)
    {
        var approved = Normalize(approvedClaims);
        var prohibited = Normalize(prohibitedClaims);
        var mandatory = Normalize(mandatoryDisclaimers);
        if (approved.Count == 0)
            return "Add at least one approved Product claim.";
        if (mandatory.Count == 0)
            return "Add at least one mandatory Product disclaimer.";
        if (approved.Overlaps(prohibited))
            return "A Product claim cannot be both approved and prohibited.";
        return null;
    }

    /// <summary>
    /// Resolve-time blockers when a product version lacks usable claim/disclaimer policy.
    /// </summary>
    public static IReadOnlyList<string> ResolveBlockers(
        Guid versionId, string? approvedClaimsJson, string? prohibitedClaimsJson,
        string? mandatoryDisclaimersJson)
    {
        var approved = ParseStringList(approvedClaimsJson);
        var prohibited = ParseStringList(prohibitedClaimsJson);
        var mandatory = ParseStringList(mandatoryDisclaimersJson);
        var blocks = new List<string>();
        if (approved.Count == 0)
            blocks.Add($"product:{versionId}:approved_claims_required");
        if (mandatory.Count == 0)
            blocks.Add($"product:{versionId}:mandatory_disclaimers_required");
        if (approved.Count > 0 && prohibited.Count > 0
            && approved.ToHashSet(StringComparer.OrdinalIgnoreCase)
                .Overlaps(prohibited))
        {
            blocks.Add($"product:{versionId}:claim_policy_conflict");
        }
        return blocks;
    }

    private static HashSet<string> Normalize(IEnumerable<string>? values) =>
        (values ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
