using GeekAPI.HttpClients;
using System.Text.Json;

namespace GeekAPI.Services.ContentCreatorV2.TaskAgents;

public sealed class GccV2DiagnosticTaskAgentSeeder(
    IServiceScopeFactory scopes,
    IHostEnvironment environment,
    ILogger<GccV2DiagnosticTaskAgentSeeder> logger) : IHostedService
{
    private const string Actor = "system:diagnostic-task-agent-seed";

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
                            category = "analysis",
                            methodology = "heuristic",
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
                            uiSchema = UiSchemaFor(contract.CapabilityId),
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
                        }),
                        """["analysis-engine"]""",
                        "[]",
                        "[]",
                        """{"minimumEvidenceCoverage":0,"requiresTypedArtifact":true}""",
                        Actor), cancellationToken);
                }
                if (version.State == "draft")
                    await repo.TransitionTaskAgentVersionAsync(version.Id, "publish",
                        new(Actor, "Published deterministic first-party analysis agent."), cancellationToken);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "First-party task-agent catalog seed failed.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static object? UiSchemaFor(string capabilityId) => capabilityId switch
    {
        "ai-readiness" => new
        {
            fields = new object[]
            {
                new { id = "sourceUrl", label = "Source URL", type = "shortText", required = false },
                new { id = "visibleContent", label = "Visible page content", type = "longText", required = true },
            },
        },
        "query-planner" => new
        {
            fields = new object[]
            {
                new { id = "hypothesisTopics", label = "Hypothesis topics", type = "longText", required = false },
                new { id = "importedQueries", label = "Imported queries", type = "longText", required = false },
            },
        },
        _ => null,
    };

    private const string DocumentInputSchema =
        """
        {
          "type": "object",
          "required": ["document"],
          "additionalProperties": false,
          "properties": {
            "contractVersion": { "type": "string" },
            "document": {
              "type": "object",
              "required": ["source", "visibleContent"],
              "properties": {
                "source": { "type": "object" },
                "visibleContent": { "type": "string" },
                "mediaType": { "type": "string" },
                "contentCompleteness": { "type": "string" },
                "queries": { "type": "array" },
                "evidence": { "type": "array" },
                "existingJsonLd": { "type": "array" },
                "technical": { "type": "object" }
              }
            },
            "seeds": { "type": "array" },
            "requestedTypes": { "type": "array" }
          }
        }
        """;

    private const string QueryPlanInputSchema =
        """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "contractVersion": { "const": "queryPlannerInput.v1" },
            "queries": { "type": "array" },
            "hypothesisTopics": { "type": "array", "items": { "type": "string" } },
            "sources": { "type": "array" },
            "maxGeneratedQueries": { "type": "integer", "minimum": 0, "maximum": 200 }
          }
        }
        """;

    private const string CompetitorPageInputSchema =
        """
        {
          "type": "object",
          "required": ["page"],
          "additionalProperties": false,
          "properties": {
            "contractVersion": { "const": "competitorPageAnalysisInput.v1" },
            "page": {
              "type": "object",
              "required": ["source", "visibleContent", "competitorId", "competitorName"],
              "properties": {
                "source": { "type": "object" },
                "visibleContent": { "type": "string" },
                "mediaType": { "type": "string" },
                "contentCompleteness": { "type": "string" },
                "evidence": { "type": "array" },
                "competitorId": { "type": "string" },
                "competitorName": { "type": "string" }
              }
            }
          }
        }
        """;

    private const string ContentGapInputSchema =
        """
        {
          "type": "object",
          "required": ["subjectPages", "competitorPages"],
          "additionalProperties": false,
          "properties": {
            "contractVersion": { "const": "contentGapInput.v1" },
            "subjectPages": { "type": "array", "minItems": 1 },
            "competitorPages": { "type": "array", "minItems": 1 }
          }
        }
        """;

    private const string ReadinessComparisonInputSchema =
        """
        {
          "type": "object",
          "required": ["subjectPage", "competitorPages"],
          "additionalProperties": false,
          "properties": {
            "contractVersion": { "const": "readinessComparisonInput.v1" },
            "subjectPage": { "type": "object" },
            "competitorPages": { "type": "array", "minItems": 1, "maxItems": 4 }
          }
        }
        """;

    private const string CompetitorAuditInputSchema =
        """
        {
          "type": "object",
          "required": ["subjectPages", "competitorPages"],
          "additionalProperties": false,
          "properties": {
            "contractVersion": { "const": "competitorAuditInput.v1" },
            "subjectPages": { "type": "array", "minItems": 1 },
            "competitorPages": { "type": "array", "minItems": 1 }
          }
        }
        """;

    private const string CompetitorPositioningInputSchema =
        """
        {
          "type": "object",
          "required": ["brandPages", "competitorPages"],
          "additionalProperties": false,
          "properties": {
            "contractVersion": { "const": "competitorPositioningInput.v1" },
            "brandPages": { "type": "array", "minItems": 1 },
            "competitorPages": { "type": "array", "minItems": 1 },
            "aiAnswerObservations": { "type": "array" }
          }
        }
        """;

    private static readonly IReadOnlyList<Contract> Contracts =
    [
        new("ai-readiness", "AI Readiness Score",
            "Score a page across seven explicit AI-answer readiness dimensions.",
            "diagnostic", "readiness-score", "readinessScore.v1", "scorecard",
            DocumentInputSchema, ["diagnosticDocument.v1"]),
        new("fact-density", "Fact Density Audit",
            "Find specific claims, measure support density, and identify unsupported statements.",
            "diagnostic", "fact-density", "factDensityReport.v1", "claim-audit",
            DocumentInputSchema, ["diagnosticDocument.v1"]),
        new("entity-mapper", "Entity Mapper",
            "Build an evidence-linked map of canonical entities and co-occurrence relationships.",
            "diagnostic", "entity-map", "entityMap.v1", "entity-graph",
            DocumentInputSchema, ["diagnosticDocument.v1"]),
        new("schema-markup", "Schema Markup Generator",
            "Generate and validate JSON-LD from visible page content only.",
            "diagnostic", "schema-markup", "schemaMarkup.v1", "json-ld",
            DocumentInputSchema, ["diagnosticDocument.v1"]),
        new("query-planner", "Query Planner",
            "Prioritize observed, imported, and generated queries without treating hypotheses as demand.",
            "intelligence", "query-plan", "queryPlan.v1", "query-plan",
            QueryPlanInputSchema, ["queryProvenance.v1", "sourceProvenance.v1"]),
        new("ai-readiness-comparison", "AI Readiness Comparison",
            "Compare one owned page with up to four competitor pages under one AEO/GEO rubric.",
            "intelligence", "readiness-comparison", "readinessComparison.v1", "score-matrix",
            ReadinessComparisonInputSchema, ["pageSnapshot.v1", "competitorPageSnapshot.v1"]),
        new("content-gap", "Content Gap Finder",
            "Find evidence-linked content gaps between subject and competitor pages.",
            "intelligence", "content-gap", "contentGapAnalysis.v1", "gap-report",
            ContentGapInputSchema, ["pageSnapshot.v1", "competitorPageSnapshot.v1"]),
        new("competitor-audit", "Competitor Audit",
            "Explain competitor strengths from supplied pages with evidence-linked prioritized actions.",
            "intelligence", "competitor-audit", "competitorAudit.v1", "audit-report",
            CompetitorAuditInputSchema, ["pageSnapshot.v1", "competitorPageSnapshot.v1"]),
        new("competitor-positioning", "Competitor Positioning",
            "Map brand-versus-competitor narrative attributes without treating generated opinions as market perception.",
            "intelligence", "competitor-positioning", "competitorPositioning.v1", "positioning-map",
            CompetitorPositioningInputSchema, ["pageSnapshot.v1", "competitorPageSnapshot.v1", "aiAnswerObservation.v1"]),
        new("competitor-page", "Competitor Page Analysis",
            "Analyze a competitor page from supplied visible content and evidence only.",
            "intelligence", "competitor-page", "competitorPageAnalysis.v1", "competitor-report",
            CompetitorPageInputSchema, ["competitorPageSnapshot.v1"]),
    ];

    private sealed record Contract(
        string CapabilityId, string DisplayName, string Description, string WorkflowGroup,
        string Endpoint, string ArtifactType, string Renderer, string InputSchema,
        IReadOnlyList<string> AcceptedInputs);
}
