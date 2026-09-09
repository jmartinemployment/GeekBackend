using System.Text.Json;
using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekAPI.Services.GeekCrawler;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.ContentCreatorV2;

/// <summary>Owner-scoped durable batch grids with in-process TaskRun fan-out.</summary>
[ApiController]
[Route("api/geek-content-creator-v2/grids")]
public sealed class GccV2GridsController(
    ICurrentUserContext user,
    HttpGccV2Repository repo,
    IGeekCrawlerRagClient rag,
    ILogger<GccV2GridsController> logger) : ControllerBase
{
    private const string ContractVersion = "gcc-grid.v1";
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private string Owner => user.UserId.ToString("D");

    [HttpGet]
    public async Task<ActionResult<object>> List(CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var items = await repo.ListGridsAsync(Owner, ct);
        return Ok(new
        {
            contractVersion = ContractVersion,
            grids = items.Select(Summary),
        });
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<object>> Get(Guid id, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var grid = await repo.GetGridAsync(id, Owner, ct);
        if (grid is null) return NotFound();
        return Ok(new
        {
            contractVersion = ContractVersion,
            grid = Detail(grid),
        });
    }

    [HttpPost]
    public async Task<ActionResult<object>> Create(CreatePublicGridRequest request, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var name = string.IsNullOrWhiteSpace(request.Name) ? "Untitled grid" : request.Name.Trim();
        var grid = await repo.CreateGridAsync(new(
            Owner, name, request.Description, SeedDemo: request.SeedDemo), ct);
        return Ok(new
        {
            contractVersion = ContractVersion,
            grid = Detail(grid),
        });
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<object>> Patch(
        Guid id, PatchPublicGridRequest request, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var existing = await repo.GetGridAsync(id, Owner, ct);
        if (existing is null) return NotFound();

        var configJson = request.Config.HasValue
            ? JsonSerializer.Serialize(request.Config.Value, JsonOpts)
            : null;
        var grid = await repo.PatchGridAsync(id, new(
            Owner, request.Name, request.Description, request.Status, configJson), ct);
        return Ok(new
        {
            contractVersion = ContractVersion,
            grid = Detail(grid),
        });
    }

    [HttpPost("{id:guid}/rows")]
    public async Task<ActionResult<object>> CreateRow(
        Guid id, CreatePublicGridRowRequest request, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var existing = await repo.GetGridAsync(id, Owner, ct);
        if (existing is null) return NotFound();

        var inputJson = request.Input.HasValue
            ? JsonSerializer.Serialize(request.Input.Value, JsonOpts)
            : "{}";
        await repo.CreateGridRowAsync(id, new(Owner, inputJson), ct);
        var grid = await repo.GetGridAsync(id, Owner, ct)
            ?? throw new InvalidOperationException("Grid could not be reloaded.");
        return Ok(new
        {
            contractVersion = ContractVersion,
            grid = Detail(grid),
        });
    }

    [HttpPost("{id:guid}/runs")]
    public async Task<ActionResult<object>> CreateRun(Guid id, CreatePublicGridRunRequest request, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var existing = await repo.GetGridAsync(id, Owner, ct);
        if (existing is null) return NotFound();

        var mode = string.IsNullOrWhiteSpace(request.Mode) ? "sample" : request.Mode.Trim();
        if (mode is not ("sample" or "full"))
            return BadRequest(new { error = "mode must be sample or full." });

        var sampleSize = request.SampleSize is > 0 ? request.SampleSize.Value : 10;
        var selected = mode == "full"
            ? existing.Rows.OrderBy(r => r.RowIndex).ToList()
            : existing.Rows.OrderBy(r => r.RowIndex).Take(sampleSize).ToList();

        var endpoint = ResolveAgentEndpoint(existing.ConfigJson);
        Dictionary<string, string>? rowArtifacts = null;
        if (rag.IsEnabled && !string.IsNullOrWhiteSpace(endpoint))
        {
            rowArtifacts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in selected)
            {
                try
                {
                    if (!TryBuildContentAgentInput(endpoint, row.InputJson, out var input))
                        continue;
                    var artifact = await rag.RunDiagnosticAsync(endpoint, input, ct);
                    if (artifact is null) continue;
                    rowArtifacts[row.Id.ToString("D")] = artifact.Value.GetRawText();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex,
                        "Grid {GridId} row {RowId} RAG '{Endpoint}' failed; using deterministic payload.",
                        id, row.Id, endpoint);
                }
            }

            if (rowArtifacts.Count == 0)
                rowArtifacts = null;
        }

        var grid = await repo.CreateGridRunAsync(id, new(
            Owner, mode, request.SampleSize, Owner, rowArtifacts), ct);
        return Ok(new
        {
            contractVersion = ContractVersion,
            grid = Detail(grid),
        });
    }

    private static string ResolveAgentEndpoint(string configJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(configJson) ? "{}" : configJson);
            if (!doc.RootElement.TryGetProperty("columns", out var cols)
                || cols.ValueKind != JsonValueKind.Array)
                return "faq-set";

            foreach (var col in cols.EnumerateArray())
            {
                if (col.TryGetProperty("kind", out var kind)
                    && kind.GetString() == "agent"
                    && col.TryGetProperty("capability", out var cap))
                {
                    var value = (cap.GetString() ?? "").Trim();
                    if (value.Length == 0) continue;
                    // Capability aliases used by seeded task agents / grid config.
                    return value switch
                    {
                        "faq-generator" => "faq-set",
                        "pillar-generator" => "pillar-outline",
                        _ => value,
                    };
                }
            }
        }
        catch (JsonException)
        {
            // fall through
        }

        return "faq-set";
    }

    private static bool TryBuildContentAgentInput(
        string endpoint, string? inputJson, out JsonElement input)
    {
        using var source = JsonDocument.Parse(string.IsNullOrWhiteSpace(inputJson) ? "{}" : inputJson);
        var root = source.RootElement;
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("contractVersion", out _))
        {
            input = root.Clone();
            return true;
        }

        // Topic-only rows can drive faq-set / pillar-outline. Other content agents need
        // richer caller-supplied documents and stay on the deterministic grid payload.
        var topic = ReadTopic(root);
        if (endpoint is "faq-set")
        {
            input = JsonSerializer.SerializeToElement(new
            {
                contractVersion = "faqGeneratorInput.v1",
                topic,
                hypothesisTopics = new[] { topic },
            }, JsonOpts);
            return true;
        }

        if (endpoint is "pillar-outline")
        {
            input = JsonSerializer.SerializeToElement(new
            {
                contractVersion = "pillarOutlineInput.v1",
                topic,
            }, JsonOpts);
            return true;
        }

        input = default;
        return false;
    }

    private static string ReadTopic(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("topic", out var topic)
                && topic.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(topic.GetString()))
                return topic.GetString()!;

            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(prop.Value.GetString()))
                    return prop.Value.GetString()!;
            }
        }

        return "Untitled topic";
    }

    private static object Summary(GccV2GridListItemDto g) => new
    {
        id = g.Id.ToString("D"),
        name = g.Name,
        description = g.Description,
        status = g.Status,
        updatedAt = g.UpdatedAtUtc.ToString("O"),
        owner = g.OwnerUserId,
        rowCount = g.RowCount,
        lastRunStatus = g.LastRunStatus,
        persistence = "server",
    };

    private static object Detail(GccV2GridDto g) => new
    {
        id = g.Id.ToString("D"),
        name = g.Name,
        description = g.Description,
        status = g.Status,
        updatedAt = g.UpdatedAtUtc.ToString("O"),
        owner = g.OwnerUserId,
        persistence = "server",
        config = ParseJson(g.ConfigJson, "{}"),
        rows = g.Rows
            .OrderBy(r => r.RowIndex)
            .Select(r => new
            {
                id = r.Id.ToString("D"),
                rowIndex = r.RowIndex,
                input = ParseJson(r.InputJson, "{}"),
                output = string.IsNullOrWhiteSpace(r.OutputJson)
                    ? (JsonElement?)null
                    : ParseJson(r.OutputJson, "null"),
                status = r.Status,
                error = r.Error,
                updatedAt = r.UpdatedAtUtc.ToString("O"),
            })
            .ToArray(),
        runs = g.Runs
            .OrderByDescending(r => r.StartedAtUtc)
            .Select(r => new
            {
                id = r.Id.ToString("D"),
                mode = r.Mode,
                sampleSize = r.SampleSize,
                status = r.Status,
                actor = r.ActorUserId,
                startedAt = r.StartedAtUtc.ToString("O"),
                completedAt = r.CompletedAtUtc?.ToString("O"),
                outputCount = r.OutputCount,
                budgetPreview = ParseJson(r.BudgetPreviewJson, "{}"),
                history = ParseJson(r.HistoryJson, "[]"),
            })
            .ToArray(),
    };

    private static JsonElement ParseJson(string json, string fallback)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? fallback : json);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            using var doc = JsonDocument.Parse(fallback);
            return doc.RootElement.Clone();
        }
    }

    public sealed record CreatePublicGridRequest(
        string? Name = null, string? Description = null, bool? SeedDemo = null);

    public sealed record PatchPublicGridRequest(
        string? Name = null, string? Description = null, string? Status = null,
        JsonElement? Config = null);

    public sealed record CreatePublicGridRowRequest(JsonElement? Input = null);

    public sealed record CreatePublicGridRunRequest(string? Mode = null, int? SampleSize = null);
}
