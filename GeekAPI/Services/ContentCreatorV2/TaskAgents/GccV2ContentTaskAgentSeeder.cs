using GeekAPI.HttpClients;
using System.Text.Json;

namespace GeekAPI.Services.ContentCreatorV2.TaskAgents;

public sealed class GccV2ContentTaskAgentSeeder(
    IServiceScopeFactory scopes,
    IHostEnvironment environment,
    ILogger<GccV2ContentTaskAgentSeeder> logger) : IHostedService
{
    private const string Actor = "system:content-task-agent-seed";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (environment.IsEnvironment("Testing")) return;
        using var scope = scopes.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<HttpGccV2Repository>();
        try
        {
            foreach (var contract in Contracts)
            {
                var definition = await repo.GetTaskAgentAsync(contract.CapabilityId, cancellationToken)
                    ?? await repo.CreateTaskAgentAsync(new(
                        contract.CapabilityId, contract.DisplayName, contract.Description, Actor),
                        cancellationToken);
                var version = definition.Versions.SingleOrDefault(x =>
                    x.SemanticVersion == "1.0.0");
                if (version is null)
                {
                    version = await repo.CreateTaskAgentVersionAsync(definition.Id, new(
                        "1.0.0",
                        contract.WorkflowGroup,
                        JsonSerializer.Serialize(new
                        {
                            category = "content",
                            methodology = "citeable-generation",
                            capability = contract.CapabilityId,
                        }),
                        contract.InputSchema,
                        JsonSerializer.Serialize(new
                        {
                            type = "object",
                            required = new[] { "artifactType" },
                            properties = new
                            {
                                artifactType = new Dictionary<string, string>
                                {
                                    ["const"] = contract.ArtifactType,
                                },
                            },
                        }),
                        JsonSerializer.Serialize(new
                        {
                            engine = "geek-crawler-rag",
                            endpoint = contract.Endpoint,
                            artifactType = contract.ArtifactType,
                        }),
                        """{"requiresApprovedContext":false,"acceptsDirectDocument":true}""",
                        JsonSerializer.Serialize(new
                        {
                            kind = contract.Renderer,
                            artifactType = contract.ArtifactType,
                        }),
                        JsonSerializer.Serialize(new
                        {
                            accepts = contract.AcceptedInputs,
                            produces = new[] { contract.ArtifactType },
                            downstream = contract.Downstream,
                        }),
                        """["content-engine"]""",
                        "[]",
                        "[]",
                        """{"minimumEvidenceCoverage":0,"requiresTypedArtifact":true}""",
                        Actor), cancellationToken);
                }
                if (version.State == "draft")
                    await repo.TransitionTaskAgentVersionAsync(version.Id, "publish",
                        new(Actor, "Published deterministic first-party content agent."), cancellationToken);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Content task-agent catalog seed failed.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private const string FaqGeneratorInputSchema =
        """
        {
          "type": "object",
          "required": ["topic"],
          "additionalProperties": false,
          "properties": {
            "contractVersion": { "const": "faqGeneratorInput.v1" },
            "topic": { "type": "string" },
            "queries": { "type": "array" },
            "hypothesisTopics": { "type": "array", "items": { "type": "string" } },
            "sourceDocument": { "type": "object" },
            "maxPairs": { "type": "integer", "minimum": 1, "maximum": 20 }
          }
        }
        """;

    private const string CitableClaimsInputSchema =
        """
        {
          "type": "object",
          "required": ["sourceDocument"],
          "additionalProperties": false,
          "properties": {
            "contractVersion": { "const": "citableClaimsInput.v1" },
            "sourceDocument": { "type": "object" },
            "targetStatements": { "type": "array", "items": { "type": "string" } },
            "maxClaims": { "type": "integer", "minimum": 1, "maximum": 50 },
            "insertionTarget": { "type": "string" }
          }
        }
        """;

    private const string ComparisonBriefInputSchema =
        """
        {
          "type": "object",
          "required": ["subjectName", "competitorName", "subjectPages", "competitorPages"],
          "additionalProperties": false,
          "properties": {
            "contractVersion": { "const": "comparisonBriefInput.v1" },
            "subjectName": { "type": "string" },
            "competitorName": { "type": "string" },
            "subjectPages": { "type": "array", "minItems": 1 },
            "competitorPages": { "type": "array", "minItems": 1 },
            "decisionCriteria": { "type": "array", "items": { "type": "string" } }
          }
        }
        """;

    private const string CompetitiveResponseInputSchema =
        """
        {
          "type": "object",
          "required": ["brandPages", "competitorPages"],
          "additionalProperties": false,
          "properties": {
            "contractVersion": { "const": "competitiveResponseInput.v1" },
            "brandPages": { "type": "array", "minItems": 1 },
            "competitorPages": { "type": "array", "minItems": 1 },
            "responseMode": { "type": "string" },
            "focusQuery": { "type": "string" }
          }
        }
        """;

    private const string PillarOutlineInputSchema =
        """
        {
          "type": "object",
          "required": ["topic"],
          "additionalProperties": false,
          "properties": {
            "contractVersion": { "const": "pillarOutlineInput.v1" },
            "topic": { "type": "string" },
            "sourceDocument": { "type": "object" },
            "queries": { "type": "array" },
            "supportingContentHints": { "type": "array", "items": { "type": "string" } }
          }
        }
        """;

    private static readonly IReadOnlyList<Contract> Contracts =
    [
        new("citable-claims", "Citable Claims",
            "Convert source material into precise attributable claims without inventing statistics.",
            "originate", "citable-claims", "claimLedger.v1", "claim-ledger",
            CitableClaimsInputSchema,
            ["diagnosticDocument.v1"],
            ["faqSet.v1", "pillarOutline.v1"]),
        new("faq-generator", "FAQ Generator",
            "Generate answer-first FAQ pairs grounded in supplied queries and visible source content.",
            "originate", "faq-set", "faqSet.v1", "faq-list",
            FaqGeneratorInputSchema,
            ["queryPlan.v1", "diagnosticDocument.v1", "queryProvenance.v1"],
            ["schemaMarkup.v1"]),
        new("comparison-brief", "Comparison Brief",
            "Build a structured X vs Y brief from supplied brand and competitor pages.",
            "originate", "comparison-brief", "comparisonBrief.v1", "comparison-brief",
            ComparisonBriefInputSchema,
            ["pageSnapshot.v1", "competitorPageSnapshot.v1"],
            ["pillarOutline.v1", "competitiveResponse.v1"]),
        new("pillar-outline", "Pillar Article Outline",
            "Produce a topic-cluster pillar outline and supporting-content plan from supplied inputs.",
            "originate", "pillar-outline", "pillarOutline.v1", "outline",
            PillarOutlineInputSchema,
            ["diagnosticDocument.v1", "queryProvenance.v1"],
            ["faqSet.v1", "comparisonBrief.v1"]),
        new("competitive-response", "Competitive Response",
            "Turn a competitor win into a brand-aligned response strategy without copying competitor prose.",
            "outrank", "competitive-response", "competitiveResponse.v1", "response-plan",
            CompetitiveResponseInputSchema,
            ["pageSnapshot.v1", "competitorPageSnapshot.v1"],
            ["pillarOutline.v1", "comparisonBrief.v1"]),
    ];

    private sealed record Contract(
        string CapabilityId, string DisplayName, string Description, string WorkflowGroup,
        string Endpoint, string ArtifactType, string Renderer, string InputSchema,
        IReadOnlyList<string> AcceptedInputs, IReadOnlyList<string> Downstream);
}
