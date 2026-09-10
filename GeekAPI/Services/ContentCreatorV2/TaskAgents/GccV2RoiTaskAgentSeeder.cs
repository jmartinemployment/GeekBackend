using GeekAPI.HttpClients;
using System.Text.Json;

namespace GeekAPI.Services.ContentCreatorV2.TaskAgents;

public sealed class GccV2RoiTaskAgentSeeder(
    IServiceScopeFactory scopes,
    IHostEnvironment environment,
    ILogger<GccV2RoiTaskAgentSeeder> logger) : IHostedService
{
    private const string Actor = "system:roi-task-agent-seed";
    private const string CapabilityId = "roi-business-calculator";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (environment.IsEnvironment("Testing")) return;
        using var scope = scopes.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<HttpGccV2Repository>();
        try
        {
            var definition = await repo.GetTaskAgentAsync(CapabilityId, cancellationToken)
                ?? await repo.CreateTaskAgentAsync(new(
                    CapabilityId,
                    "AI-Based ROI Business Calculator",
                    "Project directional ROI from editable assumptions and reconcile against observed workflow telemetry.",
                    Actor), cancellationToken);

            var version = definition.Versions.SingleOrDefault(x => x.SemanticVersion == "1.0.0");
            if (version is null)
            {
                version = await repo.CreateTaskAgentVersionAsync(definition.Id, new(
                    "1.0.0",
                    "business",
                    JsonSerializer.Serialize(new
                    {
                        workflow = "optimize",
                        marketingFunction = new[] { "ops", "content" },
                        contentType = new[] { "business-case" },
                        funnelStage = new[] { "decision" },
                        process = new[] { "measure", "forecast" },
                        capability = CapabilityId,
                        methodology = "transparent-formulas",
                    }),
                    """
                    {
                      "type": "object",
                      "required": ["contractVersion", "assumptions"],
                      "additionalProperties": false,
                      "properties": {
                        "contractVersion": { "const": "roiBusinessCalculatorInput.v1" },
                        "lookbackDays": { "type": "integer", "enum": [30, 90, 180, 365] },
                        "assumptions": { "type": "object" },
                        "observedTelemetry": { "type": ["object", "null"] },
                        "platformUsers": { "type": "number" },
                        "annualAgencySpend": { "type": "number" },
                        "bottleneckLevel": { "type": "string" },
                        "annualRevenue": { "type": "number" },
                        "marketingFunctions": { "type": "array", "items": { "type": "string" } }
                      }
                    }
                    """,
                    JsonSerializer.Serialize(new
                    {
                        type = "object",
                        required = new[] { "artifactType" },
                        properties = new
                        {
                            artifactType = new Dictionary<string, string>
                            {
                                ["const"] = GccV2RoiProjectionEngine.ArtifactType,
                            },
                        },
                    }),
                    JsonSerializer.Serialize(new
                    {
                        engine = GccV2RoiProjectionEngine.Engine,
                        artifactType = GccV2RoiProjectionEngine.ArtifactType,
                        formulaVersion = GccV2RoiProjectionEngine.FormulaVersion,
                        uiSchema = new
                        {
                            fields = new object[]
                            {
                                new
                                {
                                    id = "lookbackDays",
                                    label = "Telemetry lookback (days)",
                                    type = "select",
                                    required = false,
                                    options = new[] { "30", "90", "180", "365" },
                                    placeholder = "90",
                                },
                                new
                                {
                                    id = "workflowVolume",
                                    label = "Annual workflow volume",
                                    type = "shortText",
                                    required = true,
                                    placeholder = "120",
                                },
                                new
                                {
                                    id = "baselineMinutes",
                                    label = "Baseline minutes per item",
                                    type = "shortText",
                                    required = true,
                                    placeholder = "90",
                                },
                                new
                                {
                                    id = "assistedMinutes",
                                    label = "Assisted minutes per item",
                                    type = "shortText",
                                    required = true,
                                    placeholder = "25",
                                },
                                new
                                {
                                    id = "adoptionRate",
                                    label = "Adoption rate (0-1)",
                                    type = "shortText",
                                    required = true,
                                    placeholder = "0.7",
                                },
                                new
                                {
                                    id = "successfulUseRate",
                                    label = "Successful use rate (0-1)",
                                    type = "shortText",
                                    required = true,
                                    placeholder = "0.85",
                                },
                                new
                                {
                                    id = "loadedHourlyCost",
                                    label = "Loaded hourly cost (USD)",
                                    type = "shortText",
                                    required = true,
                                    placeholder = "85",
                                },
                                new
                                {
                                    id = "redeploymentFactor",
                                    label = "Redeployment factor (0-1)",
                                    type = "shortText",
                                    required = true,
                                    placeholder = "0.6",
                                },
                                new
                                {
                                    id = "externalSpend",
                                    label = "Annual external / agency spend (USD)",
                                    type = "shortText",
                                    required = true,
                                    placeholder = "48000",
                                },
                                new
                                {
                                    id = "replaceableShare",
                                    label = "Replaceable share of external spend (0-1)",
                                    type = "shortText",
                                    required = true,
                                    placeholder = "0.35",
                                },
                                new
                                {
                                    id = "totalCostOfOwnership",
                                    label = "Total cost of ownership (USD)",
                                    type = "shortText",
                                    required = true,
                                    placeholder = "36000",
                                },
                                new
                                {
                                    id = "attributableGrossMargin",
                                    label = "Attributable gross margin (0-1)",
                                    type = "shortText",
                                    required = false,
                                    placeholder = "0.55",
                                },
                            },
                        },
                    }),
                    """{"requiresApprovedContext":false,"acceptsDirectDocument":false}""",
                    JsonSerializer.Serialize(new
                    {
                        kind = "roi-projection",
                        artifactType = GccV2RoiProjectionEngine.ArtifactType,
                    }),
                    JsonSerializer.Serialize(new
                    {
                        accepts = new[] { "roiAssumptions.v1", "observedTelemetry.v1" },
                        produces = new[] { GccV2RoiProjectionEngine.ArtifactType },
                    }),
                    "[]",
                    "[]",
                    "[]",
                    """{"requiresTypedArtifact":true,"requiresTransparentFormulas":true}""",
                    Actor), cancellationToken);
            }

            if (version.State == "draft")
            {
                await repo.TransitionTaskAgentVersionAsync(version.Id, "publish",
                    new(Actor, "Published transparent ROI business calculator."), cancellationToken);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "ROI task-agent catalog seed failed.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
