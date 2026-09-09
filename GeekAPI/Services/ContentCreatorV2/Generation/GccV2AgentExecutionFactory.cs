using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using GeekAPI.HttpClients;
using GeekAPI.Services.Rag;

namespace GeekAPI.Services.ContentCreatorV2.Generation;

public sealed class GccV2AgentExecutionFactory(
    HttpGccV2Repository repo,
    GccV2AgentTeamResolver teams,
    GccV2AgentTeamSigner signer)
{
    private static readonly JsonSerializerOptions Json = CreateJson();

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        options.Converters.Add(new GccV2PythonDateTimeOffsetConverter());
        options.Converters.Add(new GccV2PythonDoubleConverter());
        return options;
    }

    public async Task<RagAgentExecutionRequestDto> CreateAsync(
        GccV2JobDto job,
        string attemptId,
        string stage,
        string role,
        string coordinatorExecutionId,
        GccV2SignedSkillExecutionEnvelopeV2 envelope,
        RagGenerateRequest request,
        CancellationToken ct)
    {
        var team = teams.ValidatePersisted(job);
        var matches = team.Agents.SelectMany(member => member.Participation
                .Where(p => p.Stage == stage && p.Role == role).Select(p => member))
            .OrderBy(x => x.Slug, StringComparer.Ordinal).ToList();
        if (matches.Count != 1)
            throw new InvalidOperationException($"Expected exactly one {role} specialist for {stage}.");
        return await CreateForMemberAsync(
            job, attemptId, stage, role, coordinatorExecutionId, matches[0], envelope, request, ct);
    }

    public async Task<RagAgentExecutionRequestDto> CreateForMemberAsync(
        GccV2JobDto job,
        string attemptId,
        string stage,
        string role,
        string coordinatorExecutionId,
        GccV2AgentTeamMember member,
        GccV2SignedSkillExecutionEnvelopeV2 envelope,
        RagGenerateRequest request,
        CancellationToken ct)
    {
        if (!Guid.TryParse(attemptId, out _) || !Guid.TryParse(coordinatorExecutionId, out _))
            throw new InvalidOperationException("Specialist attempt and coordinator IDs must be UUIDs.");
        var toolIds = StageTools(stage, role);
        if (toolIds.Except(member.AllowedTools, StringComparer.Ordinal).Any())
            throw new InvalidOperationException(
                $"Specialist '{member.Slug}' policy does not authorize the exact {stage}/{role} tools.");
        var modelIds = member.AllowedModels.Where(x => ContentModelPolicy.IsApproved(stage, x))
            .Order(StringComparer.Ordinal).ToList();
        if (modelIds.Count == 0)
            throw new InvalidOperationException($"Specialist '{member.Slug}' has no model intersection for {stage}.");
        var roleStages = member.Participation.Where(x => x.Role == role).Select(x => x.Stage)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var executionInstructions =
            $"Objective:\n{member.Objective}\n\nInstructions:\n{member.Instructions}";
        var unsignedAgent = new RagSelectedAgentDto(
            member.Slug, member.Version, new string('0', 64), member.Name, role,
            executionInstructions, Hash(executionInstructions), member.Policy, member.PolicyDigest,
            roleStages, toolIds, modelIds);
        var selectedAgent = unsignedAgent with { Digest = CanonicalDigest(unsignedAgent, "digest") };
        var skills = member.Skills.Select(pin =>
        {
            var skill = envelope.Skills.SingleOrDefault(x =>
                x.Id == pin.Slug && x.Version == pin.Version && x.PackageDigest == pin.Digest)
                ?? throw new InvalidOperationException(
                    $"Assigned skill '{pin.Slug}@{pin.Version}' is absent from the signed execution envelope.");
            return new RagAgentSkillReferenceDto(
                skill.Id, skill.Version, skill.ActivationId, skill.PackageDigest, "required");
        }).OrderBy(x => x.SkillId, StringComparer.Ordinal).ToList();
        var artifactInputs = ArtifactInputs(request);
        var previous = (await repo.GetStageResultsAsync(job.Id, ct))
            .Where(x => x.Stage == "specialist-execution-request")
            .Select(x => TryExecution(x.OutputJson))
            .Where(x => x is not null && x.Stage == stage
                && x.SelectedAgent.Id == member.Slug && x.SelectedAgent.Role == role)
            .Cast<RagAgentExecutionRequestDto>()
            .OrderBy(x => x.AttemptNumber).ToList();
        var stageExecutionId = Guid.NewGuid().ToString("D");
        var now = DateTimeOffset.UtcNow;
        var issued = new DateTimeOffset(now.Ticks - now.Ticks % 10, TimeSpan.Zero);
        var attemptNumber = previous.Count + 1;
        var unsigned = new RagAgentExecutionRequestDto(
            "specialist-team-execution.v1", new string('0', 64), new string('0', 64), signer.KeyId,
            job.Id.ToString("D"), attemptId, coordinatorExecutionId, stageExecutionId,
            Hash($"{job.Id:D}|{coordinatorExecutionId}|{stageExecutionId}|{member.Slug}|{stage}|{role}"),
            issued, issued.AddMinutes(10), attemptNumber,
            previous.LastOrDefault()?.StageExecutionId, stage, selectedAgent,
            role switch
            {
                "contributor" => "contributorOutput.v1",
                "reviewer" => "reviewerOutput.v1",
                _ => "producerOutput.v1",
            },
            skills, artifactInputs, new RagAgentBudgetDto(), false,
            stage == "repair" ? Math.Max(0, job.AttemptCount - 1) : 0);
        var digest = CanonicalDigest(unsigned, "snapshotDigest", "signature", "cancelled");
        var execution = unsigned with { SnapshotDigest = digest, Signature = signer.Sign(digest) };
        await repo.AddStageResultAsync(job.Id, new CreateGccV2StageResultCommand(
            "specialist-execution-request", $"{stage}:{role}:{member.Slug}",
            JsonSerializer.Serialize(execution, Json), 0), ct);
        return execution;
    }

    public static IReadOnlyList<RagArtifactInputReferenceDto> ArtifactInputs(RagGenerateRequest request)
    {
        var result = new List<RagArtifactInputReferenceDto>();
        if (request.CanonicalBrief is { } brief)
            Add(result, "canonical-brief", "canonicalBrief", brief, omitNullProperties: true);
        if (request.Outline is not null && request.GenerationStage is "section" or "repair" or "finalSynthesis" or "validation")
            Add(result, "outline", "outline", request.Outline);
        if (request.DraftContent is not null && request.GenerationStage is "finalSynthesis" or "validation")
            AddString(result, "draft-content", "draftContent", request.DraftContent);
        if (request.Sources is not null && request.GenerationStage is "finalSynthesis" or "validation")
            Add(result, "sources", "sources", request.Sources, omitNullProperties: true);
        if (request.CompletedSectionSummaries is not null && request.GenerationStage is "section" or "repair")
            Add(result, "completed-section-summaries", "completedSectionSummaries", request.CompletedSectionSummaries);
        foreach (var contribution in request.SpecialistContributions ?? [])
            Add(result, $"contribution-{CanonicalDigest(contribution)}", "specialistContribution", contribution);
        foreach (var review in request.SpecialistReviews ?? [])
            Add(result, $"review-{CanonicalDigest(review)}", "specialistReview", review);
        return result;
    }

    public static string CanonicalDigest(object value, params string[] excludedProperties)
    {
        var element = JsonSerializer.SerializeToElement(value, Json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
            WriteCanonical(element, writer, excludedProperties.ToHashSet(StringComparer.Ordinal), false);
        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    public static string SerializeWireExecution(RagAgentExecutionRequestDto execution) =>
        JsonSerializer.Serialize(execution, Json);

    private static void Add(
        List<RagArtifactInputReferenceDto> result, string id, string type, object value,
        bool omitNullProperties = false)
    {
        var element = JsonSerializer.SerializeToElement(value, Json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
            WriteCanonical(element, writer, new HashSet<string>(StringComparer.Ordinal), omitNullProperties);
        result.Add(new(id, type,
            Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant()));
    }

    private static void AddString(
        List<RagArtifactInputReferenceDto> result, string id, string type, string value) =>
        result.Add(new(id, type, Hash(value)));

    private static void WriteCanonical(
        JsonElement element, Utf8JsonWriter writer, IReadOnlySet<string> excluded, bool omitNullProperties)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject()
                    .Where(x => !excluded.Contains(x.Name)
                        && !(omitNullProperties && x.Value.ValueKind == JsonValueKind.Null))
                    .OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(property.Value, writer, excluded, omitNullProperties);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteCanonical(item, writer, excluded, omitNullProperties);
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static IReadOnlyList<string> StageTools(string stage, string role)
    {
        var read = stage switch
        {
            "researchPlanning" or "outline" =>
                new[] { "search_corpus", "load_evidence_page", "get_brief_context",
                    "get_specialist_artifacts", "activate_skill", "read_skill_resource" },
            "section" or "repair" =>
                ["load_evidence_page", "get_brief_context", "get_outline_context",
                    "get_completed_section_summaries", "activate_skill", "read_skill_resource",
                    "get_specialist_artifacts"],
            _ =>
                ["load_evidence_page", "get_brief_context", "get_outline_context",
                    "get_specialist_artifacts", "activate_skill", "read_skill_resource"],
        };
        var terminal = role switch
        {
            "contributor" => "submit_contribution",
            "reviewer" => "submit_review",
            _ => stage switch
            {
                "researchPlanning" => "submit_research_plan",
                "outline" => "submit_outline",
                "section" => "submit_section",
                "repair" => "submit_repair",
                "validation" => "submit_validation",
                "finalSynthesis" => "submit_final_synthesis",
                _ => throw new InvalidOperationException($"Unsupported v3 specialist stage '{stage}'."),
            },
        };
        return read.Append(terminal).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
    }

    private static RagAgentExecutionRequestDto? TryExecution(string json)
    {
        try { return JsonSerializer.Deserialize<RagAgentExecutionRequestDto>(json, Json); }
        catch (JsonException) { return null; }
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

}

public sealed class GccV2PythonDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        DateTimeOffset.Parse(reader.GetString()!, System.Globalization.CultureInfo.InvariantCulture);

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        var utc = value.ToUniversalTime();
        var fractional = utc.ToString("fffffff").TrimEnd('0');
        writer.WriteStringValue(fractional.Length == 0
            ? utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")
            : utc.ToString("yyyy-MM-dd'T'HH:mm:ss") + "." + fractional + "Z");
    }
}

public sealed class GccV2PythonDoubleConverter : JsonConverter<double>
{
    public override double Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDouble();

    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options) =>
        writer.WriteRawValue(value.ToString("0.0################",
            System.Globalization.CultureInfo.InvariantCulture));
}
