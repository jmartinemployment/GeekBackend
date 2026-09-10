using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GeekRepository.Auth;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreatorV2;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Controllers.ContentCreatorV2;

/// <summary>Internal persistence API for the user-invokable task-agent application kernel.</summary>
[ApiController]
[Route("repo/content-creator-v2/task-agents")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public sealed class GccV2TaskAgentsController(ContentCreatorV2DbContext db) : ControllerBase
{
    private static readonly HashSet<string> VersionStates = ["draft", "published", "deprecated", "revoked"];

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GccV2TaskAgentDefinition>>> List(
        [FromQuery] string? state, CancellationToken ct)
    {
        var query = db.GccV2TaskAgentDefinitions.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(state))
            query = query.Where(x => x.LifecycleState == state);
        return Ok(await IncludeVersions(query).OrderBy(x => x.CapabilityId).ToListAsync(ct));
    }

    [HttpGet("{idOrCapability}")]
    public async Task<ActionResult<GccV2TaskAgentDefinition>> Get(string idOrCapability, CancellationToken ct)
    {
        var query = IncludeVersions(db.GccV2TaskAgentDefinitions.AsNoTracking());
        var definition = Guid.TryParse(idOrCapability, out var id)
            ? await query.SingleOrDefaultAsync(x => x.Id == id, ct)
            : await query.SingleOrDefaultAsync(x => x.CapabilityId == idOrCapability, ct);
        return definition is null ? NotFound() : Ok(definition);
    }

    [HttpPost]
    public async Task<ActionResult<GccV2TaskAgentDefinition>> Create(
        CreateDefinitionCommand command, CancellationToken ct)
    {
        var capabilityId = command.CapabilityId.Trim().ToLowerInvariant();
        if (!ValidId(capabilityId) || string.IsNullOrWhiteSpace(command.DisplayName)
            || string.IsNullOrWhiteSpace(command.Description))
            return BadRequest("Valid capabilityId, displayName, and description are required.");
        if (await db.GccV2TaskAgentDefinitions.AnyAsync(x => x.CapabilityId == capabilityId, ct))
            return Conflict("Capability ID already exists.");
        var definition = new GccV2TaskAgentDefinition
        {
            CapabilityId = capabilityId,
            DisplayName = command.DisplayName.Trim(),
            Description = command.Description.Trim(),
        };
        db.Add(definition);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { idOrCapability = definition.Id }, definition);
    }

    [HttpPatch("{definitionId:guid}")]
    public async Task<ActionResult<GccV2TaskAgentDefinition>> Patch(
        Guid definitionId, PatchDefinitionCommand command, CancellationToken ct)
    {
        var definition = await db.GccV2TaskAgentDefinitions.SingleOrDefaultAsync(x => x.Id == definitionId, ct);
        if (definition is null) return NotFound();
        if (definition.LifecycleState is "published" or "deprecated" or "revoked")
            return Conflict("Published task-agent metadata is immutable.");
        if (!string.IsNullOrWhiteSpace(command.DisplayName)) definition.DisplayName = command.DisplayName.Trim();
        if (!string.IsNullOrWhiteSpace(command.Description)) definition.Description = command.Description.Trim();
        definition.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(definition);
    }

    [HttpPost("{definitionId:guid}/versions")]
    public async Task<ActionResult<GccV2TaskAgentVersion>> CreateVersion(
        Guid definitionId, CreateVersionCommand command, CancellationToken ct)
    {
        var definition = await db.GccV2TaskAgentDefinitions.SingleOrDefaultAsync(x => x.Id == definitionId, ct);
        if (definition is null) return NotFound();
        if (!ValidSemver(command.SemanticVersion) || string.IsNullOrWhiteSpace(command.WorkflowGroup))
            return BadRequest("semanticVersion and workflowGroup are required.");

        string facets, inputSchema, outputSchema, workflow, contextPolicy, renderer, compatible,
            tools, models, skills, thresholds;
        try
        {
            facets = Canonical(command.FacetsJson, JsonValueKind.Object);
            inputSchema = Canonical(command.InputSchemaJson, JsonValueKind.Object);
            outputSchema = Canonical(command.OutputSchemaJson, JsonValueKind.Object);
            workflow = Canonical(command.WorkflowJson, JsonValueKind.Object);
            contextPolicy = Canonical(command.ContextPolicyJson, JsonValueKind.Object);
            renderer = Canonical(command.ResultRendererJson, JsonValueKind.Object);
            compatible = Canonical(command.CompatibleArtifactTypesJson, JsonValueKind.Object);
            tools = Canonical(command.AllowedToolsJson, JsonValueKind.Array);
            models = Canonical(command.AllowedModelsJson, JsonValueKind.Array);
            skills = Canonical(command.SkillVersionIdsJson, JsonValueKind.Array);
            thresholds = Canonical(command.EvaluationThresholdsJson, JsonValueKind.Object);
        }
        catch (JsonException ex) { return BadRequest(ex.Message); }

        var skillIds = ParseGuidArray(skills);
        if (skillIds is null) return BadRequest("skillVersionIdsJson must contain only GUID strings.");
        if (skillIds.Count > 0)
        {
            var publishedCount = await db.GccV2SkillVersions.CountAsync(
                x => skillIds.Contains(x.Id) && x.State == "published", ct);
            if (publishedCount != skillIds.Count)
                return Conflict("Every pinned skill version must exist and be published.");
        }

        var inputDigest = Hash(inputSchema);
        var outputDigest = Hash(outputSchema);
        var workflowDigest = Hash(workflow);
        var contextDigest = Hash(contextPolicy);
        var rendererDigest = Hash(renderer);
        var thresholdsDigest = Hash(thresholds);
        var versionDigest = Hash(Canonical(JsonSerializer.Serialize(new
        {
            definitionId,
            command.SemanticVersion,
            workflowGroup = command.WorkflowGroup.Trim(),
            facets,
            inputSchemaDigest = inputDigest,
            outputSchemaDigest = outputDigest,
            workflowDigest,
            contextPolicyDigest = contextDigest,
            resultRendererDigest = rendererDigest,
            compatibleArtifactTypes = compatible,
            allowedTools = tools,
            allowedModels = models,
            skillVersionIds = skills,
            evaluationThresholdsDigest = thresholdsDigest,
        }), JsonValueKind.Object));
        var version = new GccV2TaskAgentVersion
        {
            DefinitionId = definitionId,
            SemanticVersion = command.SemanticVersion,
            WorkflowGroup = command.WorkflowGroup.Trim(),
            FacetsJson = facets,
            InputSchemaJson = inputSchema,
            InputSchemaDigest = inputDigest,
            OutputSchemaJson = outputSchema,
            OutputSchemaDigest = outputDigest,
            WorkflowJson = workflow,
            WorkflowDigest = workflowDigest,
            ContextPolicyJson = contextPolicy,
            ContextPolicyDigest = contextDigest,
            ResultRendererJson = renderer,
            ResultRendererDigest = rendererDigest,
            CompatibleArtifactTypesJson = compatible,
            AllowedToolsJson = tools,
            AllowedModelsJson = models,
            SkillVersionIdsJson = skills,
            EvaluationThresholdsJson = thresholds,
            EvaluationThresholdsDigest = thresholdsDigest,
            VersionDigest = versionDigest,
            CreatedBy = command.Actor,
        };
        db.Add(version);
        definition.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { idOrCapability = definitionId }, version);
    }

    [HttpPost("versions/{versionId:guid}/{transition:regex(^publish|deprecate|revoke$)}")]
    public async Task<ActionResult<GccV2TaskAgentVersion>> TransitionVersion(
        Guid versionId, string transition, TransitionVersionCommand command, CancellationToken ct)
    {
        var version = await db.GccV2TaskAgentVersions.Include(x => x.Definition)
            .SingleOrDefaultAsync(x => x.Id == versionId, ct);
        if (version is null) return NotFound();
        var next = transition switch
        {
            "publish" when version.State == "draft" => "published",
            "deprecate" when version.State == "published" => "deprecated",
            "revoke" when version.State is "published" or "deprecated" => "revoked",
            _ => null,
        };
        if (next is null || !VersionStates.Contains(next)) return Conflict("Invalid lifecycle transition.");
        version.State = next;
        version.ReviewedBy = command.Actor;
        var now = DateTimeOffset.UtcNow;
        if (next == "published") version.PublishedAtUtc = now;
        if (next == "deprecated") version.DeprecatedAtUtc = now;
        if (next == "revoked") version.RevokedAtUtc = now;
        version.Definition.LifecycleState = next == "published"
            || await db.GccV2TaskAgentVersions.AnyAsync(
                x => x.DefinitionId == version.DefinitionId && x.Id != version.Id && x.State == "published", ct)
            ? "published" : next;
        version.Definition.UpdatedAtUtc = now;
        await db.SaveChangesAsync(ct);
        return Ok(version);
    }

    private static IQueryable<GccV2TaskAgentDefinition> IncludeVersions(
        IQueryable<GccV2TaskAgentDefinition> query) =>
        query.Include(x => x.Versions.OrderByDescending(v => v.CreatedAtUtc));

    internal static string Canonical(string json, JsonValueKind expected)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json)
            ? expected == JsonValueKind.Array ? "[]" : "{}"
            : json);
        if (document.RootElement.ValueKind != expected)
            throw new JsonException($"JSON must have a {expected.ToString().ToLowerInvariant()} root.");
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteCanonical(writer, document.RootElement);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    internal static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in element.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                WriteCanonical(writer, property.Value);
            }
            writer.WriteEndObject();
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in element.EnumerateArray()) WriteCanonical(writer, item);
            writer.WriteEndArray();
        }
        else element.WriteTo(writer);
    }

    private static List<Guid>? ParseGuidArray(string json)
    {
        using var document = JsonDocument.Parse(json);
        var ids = new List<Guid>();
        foreach (var item in document.RootElement.EnumerateArray())
            if (item.ValueKind != JsonValueKind.String || !Guid.TryParse(item.GetString(), out var id))
                return null;
            else if (!ids.Contains(id)) ids.Add(id);
        return ids;
    }

    [HttpGet("library-preferences")]
    public async Task<ActionResult<GccV2TaskAgentLibraryPreference>> GetLibraryPreferences(
        [FromQuery] string ownerUserId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId)) return BadRequest("ownerUserId is required.");
        var row = await db.GccV2TaskAgentLibraryPreferences.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OwnerUserId == ownerUserId, ct);
        if (row is not null) return Ok(row);
        return Ok(new GccV2TaskAgentLibraryPreference
        {
            OwnerUserId = ownerUserId,
            FavoritesJson = "[]",
            SavedConfigsJson = "[]",
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
    }

    [HttpPut("library-preferences")]
    public async Task<ActionResult<GccV2TaskAgentLibraryPreference>> PutLibraryPreferences(
        PutLibraryPreferencesCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.OwnerUserId)) return BadRequest("ownerUserId is required.");
        string favorites;
        string configs;
        try
        {
            favorites = Canonical(command.FavoritesJson, JsonValueKind.Array);
            configs = Canonical(command.SavedConfigsJson, JsonValueKind.Array);
        }
        catch (JsonException ex)
        {
            return BadRequest(ex.Message);
        }

        var row = await db.GccV2TaskAgentLibraryPreferences
            .SingleOrDefaultAsync(x => x.OwnerUserId == command.OwnerUserId, ct);
        if (row is null)
        {
            row = new GccV2TaskAgentLibraryPreference { OwnerUserId = command.OwnerUserId };
            db.Add(row);
        }
        row.FavoritesJson = favorites;
        row.SavedConfigsJson = configs;
        row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(row);
    }

    private static bool ValidId(string value) =>
        value.Length is > 0 and <= 128 && value.All(x => char.IsAsciiLetterOrDigit(x) || x is '-' or '.')
        && char.IsAsciiLetterOrDigit(value[0]) && char.IsAsciiLetterOrDigit(value[^1]);
    private static bool ValidSemver(string value) =>
        value.Split('.').Length == 3 && value.Split('.').All(x => int.TryParse(x, out _));

    public sealed record CreateDefinitionCommand(
        string CapabilityId, string DisplayName, string Description, string Actor);
    public sealed record PatchDefinitionCommand(string? DisplayName, string? Description, string Actor);
    public sealed record CreateVersionCommand(
        string SemanticVersion, string WorkflowGroup, string FacetsJson, string InputSchemaJson,
        string OutputSchemaJson, string WorkflowJson, string ContextPolicyJson,
        string ResultRendererJson, string CompatibleArtifactTypesJson, string AllowedToolsJson,
        string AllowedModelsJson, string SkillVersionIdsJson, string EvaluationThresholdsJson,
        string Actor);
    public sealed record TransitionVersionCommand(string Actor, string? Reason);
    public sealed record PutLibraryPreferencesCommand(
        string OwnerUserId, string FavoritesJson, string SavedConfigsJson);
}
