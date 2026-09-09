using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GeekAPI.HttpClients;

namespace GeekAPI.Services.ContentCreatorV2.Generation;

public static class GccV2SkillApiMapper
{
    public static object Summary(GccV2SkillPackageDto package, GccV2SkillVersionDto version)
    {
        var applicability = version.Applicability.OrderBy(x => x.Order).ToList();
        return new
        {
            id = package.Slug,
            versionId = version.Id,
            name = package.DisplayName,
            version = version.SemanticVersion,
            contribution = package.Description,
            source = new
            {
                repositoryUrl = package.SourceRepository,
                commit = version.ImmutableGitRef,
                path = package.SourcePath,
                discoveryUrl = (string?)null,
            },
            packageDigest = version.PackageSha256,
            manifestDigest = version.ManifestDigest,
            license = version.License,
            compatibility = version.Compatibility,
            reviewStatus = version.State,
            reviewer = version.Reviewer,
            reviewedAtUtc = version.ReviewedAtUtc,
            publishedAtUtc = version.PublishedAtUtc,
            supportedStages = applicability.Select(x => x.Stage).Distinct(StringComparer.Ordinal).Order().ToList(),
            supportedContentTypes = applicability.Select(x => x.ContentType).Distinct(StringComparer.Ordinal).Order().ToList(),
            requestedTools = applicability.SelectMany(ParseTools).Distinct(StringComparer.Ordinal).Order().ToList(),
            activationMode = applicability.Select(x => x.ActivationMode).FirstOrDefault() ?? "automatic",
            deprecatedAtUtc = package.DeprecatedAtUtc,
            supersededByVersionId = (Guid?)null,
            origin = package.IsFirstParty ? "first-party" : "community",
        };
    }

    public static object Detail(
        GccV2SkillPackageDto package,
        GccV2SkillVersionDto version,
        IReadOnlyList<GccV2SkillAuditEventDto> audit) => new
        {
            summary = Summary(package, version),
            findings = version.Findings.Select(x => new
            {
                id = x.Id,
                severity = x.Severity,
                scanner = x.Scanner,
                rule = x.Rule,
                filePath = x.FilePath,
                line = x.Line,
                message = x.Message,
                disposition = x.Disposition == "unreviewed" ? "open" : x.Disposition,
                reviewerRationale = x.ReviewerRationale,
            }),
            files = version.Files.Select(x => new
            {
                path = x.RelativePath,
                mediaType = x.MediaType,
                byteCount = x.ByteCount,
                digest = x.Sha256,
                content = x.Content,
                executable = x.RelativePath.StartsWith("scripts/", StringComparison.Ordinal),
            }),
            audit = audit.Where(x => x.VersionId == version.Id || x.VersionId is null).Select(x => new
            {
                id = x.Id,
                actor = x.Actor,
                action = x.Action,
                atUtc = x.CreatedAtUtc,
                requestId = x.RequestId,
                beforeStatus = x.BeforeState,
                afterStatus = x.AfterState,
                detail = (string?)null,
            }),
        };

    public static object FlattenDetail(
        GccV2SkillPackageDto package,
        GccV2SkillVersionDto version,
        IReadOnlyList<GccV2SkillAuditEventDto> audit)
    {
        var summary = JsonSerializer.SerializeToElement(Summary(package, version),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var detail = JsonSerializer.SerializeToElement(Detail(package, version, audit),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var result = new Dictionary<string, object?>();
        foreach (var property in summary.EnumerateObject()) result[property.Name] = property.Value.Clone();
        result["findings"] = detail.GetProperty("findings").Clone();
        result["files"] = detail.GetProperty("files").Clone();
        result["audit"] = detail.GetProperty("audit").Clone();
        return result;
    }

    public static string ResolutionDigest(IEnumerable<GccV2SkillPackageDto> packages)
    {
        var canonical = string.Join("\n", packages.OrderBy(x => x.Slug, StringComparer.Ordinal)
            .SelectMany(x => x.Versions.Where(v => v.State == "published")
                .OrderBy(v => v.SemanticVersion, StringComparer.Ordinal)
                .Select(v => $"{x.Slug}|{v.Id:D}|{v.SemanticVersion}|{v.PackageSha256}")));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static IEnumerable<string> ParseTools(GccV2SkillApplicabilityDto applicability)
    {
        try { return JsonSerializer.Deserialize<List<string>>(applicability.RequiredToolsJson) ?? []; }
        catch (JsonException) { return []; }
    }
}
