using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GeekAPI.HttpClients;

namespace GeekAPI.Services.ContentCreatorV2.Generation;

public sealed record GccV2SkillDefinition(
    string Id,
    string Version,
    string Sha256,
    string Source,
    string License,
    string Reviewer,
    IReadOnlyList<string> SupportedStages,
    IReadOnlyList<string> SupportedContentTypes,
    int Order,
    IReadOnlyList<string> Conflicts,
    string PromptInstructions,
    string RetrievalHints,
    string OutputRequirements,
    string ValidationChecks)
{
    public string CanonicalContent => string.Join(
        "\n", PromptInstructions, RetrievalHints, OutputRequirements, ValidationChecks);
}

public sealed record GccV2SkillExecutionSnapshot(
    string EnvelopeVersion,
    string CatalogVersion,
    string SnapshotHash,
    string ContentType,
    IReadOnlyList<GccV2SkillDefinition> Skills,
    DateTimeOffset ResolvedAtUtc)
{
    public const string CurrentEnvelopeVersion = "gcc-skill-envelope.v1";
}

public sealed record GccV2SkillCatalogItem(
    string Id,
    string Name,
    string Version,
    string Contribution,
    string ReviewStatus,
    string Reviewer,
    IReadOnlyList<string> SupportedStages,
    IReadOnlyList<string> SupportedContentTypes);

public sealed record GccV2SkillBundle(
    string Id,
    string Name,
    string Goal,
    IReadOnlyList<string> SkillIds);

public static class GccV2SkillCatalog
{
    public const string CurrentVersion = "gcc-safe-skills.2026-09-08";
    public const int MaxSkills = 10;
    public const int MaxSerializedBytes = 16 * 1024;

    private static readonly IReadOnlyList<string> AllStages =
        ["researchPlanning", "outline", "section", "repair", "validation", "finalSynthesis", "complete"];
    private static readonly IReadOnlyList<string> AllContentTypes =
        GccV2ContentTypeRagMapper.CanonicalContentTypes;

    public static IReadOnlyList<GccV2SkillDefinition> Definitions { get; } =
    [
        Skill("citation-discipline", "1.0.0", "61da5cdcecc35a2c2838250370287c7b4092b138fbbaa0563ca0cd222cdbbbbc", 10,
            AllStages, AllContentTypes,
            "Keep every factual claim within the supplied evidence and never invent a citation.",
            "Prefer exact primary-source passages with stable page identifiers.",
            "Return verbatim quote citations for factual claims.",
            "Fail factual claims that lack verified quote-level support."),
        Skill("brand-voice", "1.0.0", "76e245ac0401621836a5a9f77f27d21fa1a7a785bab82afc11ef856b82f5c326", 20,
            AllStages, AllContentTypes,
            "Apply the supplied brand voice without changing facts, evidence rules, or security policy.",
            "Do not broaden source scope; use brand context only as style guidance.",
            "Keep terminology, tone, and prohibited-language constraints consistent.",
            "Check tone and terminology against the canonical brand brief."),
        Skill("seo-fundamentals", "1.0.0", "f42ff5a164422e1526cdc3460fdff3cc2d5ab5508f6e6f6055ea07f1babc51b6", 30,
            AllStages, ["pillar", "blog", "guide", "tech-article", "case-study", "whitepaper", "listicle", "comparison", "alternatives", "tool", "service", "local"],
            "Use descriptive headings and answer the primary search intent without keyword stuffing.",
            "Prioritize evidence that directly answers the target keyword and related questions.",
            "Include a clear information hierarchy and concise descriptive headings.",
            "Check intent alignment, heading clarity, and natural keyword coverage."),
        Skill("geo-direct-answer", "1.0.0", "44734869d4539621ce326b8d4b1087ab6f8286133742ec553de283ccf31763e7", 40,
            AllStages, ["pillar", "blog", "guide", "tech-article", "whitepaper", "comparison", "alternatives", "tool", "service", "local"],
            "Lead applicable sections with a concise direct answer before supporting detail.",
            "Prefer authoritative quotable evidence suitable for answer extraction.",
            "Use self-contained statements and explicit entity names.",
            "Check direct-answer clarity and whether claims remain citation-supported."),
        Skill("comparison-evidence", "1.0.0", "d46466a0d413e40edf2465c80152c409f7be906b216b11b29a119c30435ef06b", 50,
            AllStages, ["comparison", "alternatives"],
            "Compare options using the same evidence-backed criteria and neutral language.",
            "Retrieve both partner and competitor evidence for each comparison criterion.",
            "Separate verified differences from analysis and avoid unsupported superiority claims.",
            "Check symmetric criteria, source coverage, and unsupported comparative claims."),
        Skill("case-study-proof", "1.0.0", "947fdb6fe9a2a364c05b4c02d34644783baaeb6b15b6afa8b63a166da1e5bb1b", 50,
            AllStages, ["case-study"],
            "Separate context, intervention, and measured outcome; do not imply causation without evidence.",
            "Prioritize first-party proof, measured outcomes, and explicit timeframes.",
            "Label unverified outcomes and preserve qualifications from sources.",
            "Check every result, number, and causal statement against evidence."),
        Skill("technical-depth", "1.0.0", "5fb04e195d9be87a1fb1a2243f43abbb3aed5a00cc97ed5e136b0b218ee0e71c", 50,
            AllStages, ["pillar", "guide", "tech-article", "whitepaper", "tool", "service"],
            "Explain architecture, tradeoffs, constraints, and operational implications precisely.",
            "Prefer technical documentation and concrete implementation evidence.",
            "Include prerequisites, boundaries, failure modes, and decisions where supported.",
            "Check technical consistency, actionable depth, and unsupported implementation details."),
        Skill("linkedin-document-structure", "1.0.0", "aac60102b0c1777734b7734a54b762b48e963b971542f32eb0ed03b974e64b11", 50,
            AllStages, ["linkedin-document"],
            "Structure a concise hook, progressive teaching sequence, summary, and approved CTA.",
            "Allocate one evidence-backed idea per slide or document panel.",
            "Keep panels scannable and preserve citation traceability in supporting metadata.",
            "Check narrative progression, panel density, and CTA alignment."),
        Skill("anti-repetition", "1.0.0", "0ce61afbc02bf3e284d77a72d0e768e28849e7f0e35542ed5e0ec9c07ee95a8f", 80,
            ["section", "repair", "validation", "finalSynthesis", "complete"], AllContentTypes,
            "Give each section a distinct job and avoid repeating prior section substance.",
            "Use completed-section summaries only to detect overlap.",
            "Remove redundant claims while preserving unique cited evidence.",
            "Check cross-section semantic repetition and duplicated examples."),
        Skill("cta-alignment", "1.0.0", "b7b4c49b20bfe4f8eeef70f1b38272825608c41235ec63f41a7b377d3e0a5440", 90,
            ["section", "repair", "validation", "finalSynthesis", "complete"], AllContentTypes,
            "Use only the canonical conversion objective and approved call to action.",
            "Do not retrieve evidence to manufacture urgency or unsupported promises.",
            "Keep the CTA proportionate to the evidence and audience stage.",
            "Check CTA destination, promise, and buying-stage alignment."),
    ];

    public static IReadOnlyList<GccV2SkillBundle> RecommendedBundles { get; } =
    [
        new("search-growth", "Search growth", "Build useful search-led content with direct, extractable answers.",
            ["citation-discipline", "brand-voice", "seo-fundamentals", "geo-direct-answer", "anti-repetition", "cta-alignment"]),
        new("technical-authority", "Technical authority", "Explain implementation decisions, constraints, and tradeoffs precisely.",
            ["citation-discipline", "brand-voice", "technical-depth", "anti-repetition", "cta-alignment"]),
        new("competitive-decision", "Competitive decision support", "Compare options consistently without unsupported superiority claims.",
            ["citation-discipline", "brand-voice", "comparison-evidence", "seo-fundamentals", "anti-repetition", "cta-alignment"]),
        new("proof-story", "Customer proof", "Structure a qualified, evidence-backed case study.",
            ["citation-discipline", "brand-voice", "case-study-proof", "anti-repetition", "cta-alignment"]),
        new("linkedin-education", "LinkedIn education", "Turn one supported idea into a scannable teaching sequence.",
            ["citation-discipline", "brand-voice", "linkedin-document-structure", "anti-repetition", "cta-alignment"]),
    ];

    public static IReadOnlyList<GccV2SkillCatalogItem> PublicCatalog() =>
        Definitions.Select(skill => new GccV2SkillCatalogItem(
            skill.Id,
            DisplayName(skill.Id),
            skill.Version,
            skill.OutputRequirements,
            "Approved",
            skill.Reviewer,
            skill.SupportedStages,
            skill.SupportedContentTypes)).ToList();

    static GccV2SkillCatalog()
    {
        foreach (var skill in Definitions)
        {
            var actual = Hash(skill.CanonicalContent);
            if (!string.Equals(actual, skill.Sha256, StringComparison.Ordinal))
                throw new InvalidOperationException($"Reviewed skill '{skill.Id}' does not match its pinned SHA-256.");
        }
    }

    public static GccV2SkillExecutionSnapshot Resolve(string contentType, DateTimeOffset? now = null)
    {
        var normalized = GccV2ContentTypeRagMapper.Map(contentType).ContentType;
        var selected = Definitions
            .Where(skill => skill.SupportedContentTypes.Contains(normalized, StringComparer.Ordinal))
            .OrderBy(skill => skill.Order)
            .ThenBy(skill => skill.Id, StringComparer.Ordinal)
            .ToList();
        Validate(selected);
        var snapshotHash = Hash(CanonicalSnapshot(normalized, selected));
        var snapshot = new GccV2SkillExecutionSnapshot(
            GccV2SkillExecutionSnapshot.CurrentEnvelopeVersion,
            CurrentVersion,
            snapshotHash,
            normalized,
            selected,
            now ?? DateTimeOffset.UtcNow);
        if (JsonSerializer.SerializeToUtf8Bytes(snapshot).Length > MaxSerializedBytes)
            throw new InvalidOperationException($"Resolved skill snapshot exceeds {MaxSerializedBytes} bytes.");
        return snapshot;
    }

    public static void ValidateSnapshot(GccV2SkillExecutionSnapshot snapshot)
    {
        if (snapshot.EnvelopeVersion != GccV2SkillExecutionSnapshot.CurrentEnvelopeVersion
            || snapshot.CatalogVersion != CurrentVersion)
            throw new InvalidOperationException("Unsupported skill envelope or catalog version.");
        Validate(snapshot.Skills);
        var expected = Hash(CanonicalSnapshot(snapshot.ContentType, snapshot.Skills));
        if (!string.Equals(expected, snapshot.SnapshotHash, StringComparison.Ordinal))
            throw new InvalidOperationException("Skill execution snapshot hash mismatch.");
    }

    public static GccV2SkillExecutionSnapshot ForStage(
        GccV2SkillExecutionSnapshot snapshot,
        string stage)
    {
        ValidateSnapshot(snapshot);
        if (!AllStages.Contains(stage, StringComparer.Ordinal))
            throw new InvalidOperationException($"Unsupported skill execution stage '{stage}'.");
        return snapshot;
    }

    private static void Validate(IReadOnlyList<GccV2SkillDefinition> skills)
    {
        if (skills.Count > MaxSkills) throw new InvalidOperationException($"Skill count exceeds {MaxSkills}.");
        var duplicate = skills.GroupBy(s => s.Id, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null) throw new InvalidOperationException($"Duplicate skill '{duplicate.Key}'.");
        if (!skills.SequenceEqual(skills.OrderBy(s => s.Order).ThenBy(s => s.Id, StringComparer.Ordinal)))
            throw new InvalidOperationException("Skills are not in deterministic catalog order.");
        var ids = skills.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var skill in skills)
        {
            if (!string.Equals(Hash(skill.CanonicalContent), skill.Sha256, StringComparison.Ordinal))
                throw new InvalidOperationException($"Skill '{skill.Id}' hash mismatch.");
            var conflict = skill.Conflicts.FirstOrDefault(ids.Contains);
            if (conflict is not null) throw new InvalidOperationException($"Skill '{skill.Id}' conflicts with '{conflict}'.");
        }
    }

    private static GccV2SkillDefinition Skill(
        string id, string version, string sha256, int order,
        IReadOnlyList<string> stages, IReadOnlyList<string> contentTypes,
        string prompt, string retrieval, string output, string validation) =>
        new(id, version, sha256, "GeekBackend reviewed catalog", "MIT-derived/internal-reviewed",
            "Content Platform", stages, contentTypes, order, [], prompt, retrieval, output, validation);

    private static string DisplayName(string id) =>
        string.Join(" ", id.Split('-').Select(word =>
            char.ToUpperInvariant(word[0]) + word[1..]));

    private static string CanonicalSnapshot(
        string contentType,
        IEnumerable<GccV2SkillDefinition> skills) =>
        string.Join("\n", new[] { CurrentVersion, contentType }.Concat(
            skills.Select(s => $"{s.Order}|{s.Id}|{s.Version}|{s.Sha256}")));

    internal static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public static class GccV2SkillSnapshotStore
{
    public const string StageName = "skill-execution-snapshot";

    public static async Task<GccV2SkillExecutionSnapshot> LoadOrCreateAsync(
        HttpGccV2Repository repo,
        GccV2JobDto job,
        CancellationToken ct)
    {
        var results = await repo.GetStageResultsAsync(job.Id, ct);
        var existing = results
            .Where(result => string.Equals(result.Stage, StageName, StringComparison.Ordinal))
            .OrderBy(result => result.CompletedAtUtc)
            .ToList();
        if (existing.Count > 0)
        {
            var snapshots = existing.Select(result =>
                JsonSerializer.Deserialize<GccV2SkillExecutionSnapshot>(result.OutputJson)
                ?? throw new InvalidOperationException("Persisted skill snapshot is malformed.")).ToList();
            foreach (var snapshot in snapshots) GccV2SkillCatalog.ValidateSnapshot(snapshot);
            if (snapshots.Select(s => s.SnapshotHash).Distinct(StringComparer.Ordinal).Count() != 1)
                throw new InvalidOperationException("Job contains conflicting immutable skill snapshots.");
            return snapshots[0];
        }

        var created = GccV2SkillCatalog.Resolve(job.ContentType);
        await repo.AddStageResultAsync(
            job.Id,
            new CreateGccV2StageResultCommand(
                StageName, null, JsonSerializer.Serialize(created), 0),
            ct);
        return created;
    }
}
