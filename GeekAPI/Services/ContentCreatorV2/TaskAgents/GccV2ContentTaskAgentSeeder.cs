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
                    x.SemanticVersion == "1.1.0")
                    ?? definition.Versions.SingleOrDefault(x =>
                        x.SemanticVersion == "1.0.0");
                if (definition.Versions.All(x => x.SemanticVersion != "1.1.0"))
                {
                    version = await repo.CreateTaskAgentVersionAsync(definition.Id, new(
                        "1.1.0",
                        contract.WorkflowGroup,
                        JsonSerializer.Serialize(FacetsFor(contract.CapabilityId, contract.JasperWorkflow)),
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
                            downstream = contract.Downstream,
                        }),
                        """["content-engine"]""",
                        "[]",
                        "[]",
                        """{"minimumEvidenceCoverage":0,"requiresTypedArtifact":true}""",
                        Actor), cancellationToken);
                }
                if (version is null) continue;
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

    private static object FacetsFor(string capabilityId, string jasperWorkflow) => capabilityId switch
    {
        "citable-claims" => new
        {
            workflow = jasperWorkflow,
            marketingFunction = new[] { "content", "aeo" },
            contentType = new[] { "claims", "article" },
            funnelStage = new[] { "awareness", "consideration" },
            process = new[] { "originate", "optimize" },
            capability = capabilityId,
            methodology = "citeable-generation",
        },
        "faq-generator" => new
        {
            workflow = jasperWorkflow,
            marketingFunction = new[] { "content", "aeo", "seo" },
            contentType = new[] { "faq" },
            funnelStage = new[] { "awareness", "consideration" },
            process = new[] { "originate" },
            capability = capabilityId,
            methodology = "citeable-generation",
        },
        "comparison-brief" => new
        {
            workflow = jasperWorkflow,
            marketingFunction = new[] { "competitive", "content" },
            contentType = new[] { "brief", "comparison" },
            funnelStage = new[] { "consideration", "decision" },
            process = new[] { "originate", "brief" },
            capability = capabilityId,
            methodology = "citeable-generation",
        },
        "pillar-outline" => new
        {
            workflow = jasperWorkflow,
            marketingFunction = new[] { "content", "seo", "aeo" },
            contentType = new[] { "pillar", "outline" },
            funnelStage = new[] { "awareness" },
            process = new[] { "originate", "plan" },
            capability = capabilityId,
            methodology = "citeable-generation",
        },
        "pillar-article" => new
        {
            workflow = jasperWorkflow,
            marketingFunction = new[] { "content", "seo", "aeo" },
            contentType = new[] { "pillar", "article" },
            funnelStage = new[] { "awareness" },
            process = new[] { "originate", "draft" },
            capability = capabilityId,
            methodology = "citeable-generation",
        },
        "competitive-response" => new
        {
            workflow = jasperWorkflow,
            marketingFunction = new[] { "competitive", "content" },
            contentType = new[] { "response", "article" },
            funnelStage = new[] { "consideration", "decision" },
            process = new[] { "respond", "originate" },
            capability = capabilityId,
            methodology = "citeable-generation",
        },
        _ => new
        {
            workflow = jasperWorkflow,
            marketingFunction = new[] { "content" },
            contentType = new[] { "article" },
            funnelStage = new[] { "awareness" },
            process = new[] { "originate" },
            capability = capabilityId,
            methodology = "citeable-generation",
        },
    };

    private static object? UiSchemaFor(string capabilityId) => capabilityId switch
    {
        "faq-generator" => new
        {
            fields = new object[]
            {
                new { id = "topic", label = "Topic", type = "shortText", required = true },
                new { id = "faqQuestions", label = "FAQ questions", type = "longText", required = false },
                new { id = "sourceContent", label = "Source content", type = "longText", required = false },
                new { id = "sourceUrl", label = "Source URL", type = "shortText", required = false },
            },
        },
        "citable-claims" => new
        {
            fields = new object[]
            {
                new { id = "sourceContent", label = "Source content", type = "longText", required = true },
                new { id = "vagueStatements", label = "Vague statements to rewrite", type = "longText", required = false },
                new
                {
                    id = "contentCompleteness",
                    label = "Source completeness",
                    type = "select",
                    required = false,
                    options = new[] { "full", "partial" },
                    placeholder = "full",
                },
            },
        },
        "comparison-brief" => new
        {
            fields = new object[]
            {
                new { id = "subjectName", label = "Subject name", type = "shortText", required = true },
                new { id = "competitorName", label = "Competitor name", type = "shortText", required = true },
                new { id = "subjectContent", label = "Subject page content", type = "longText", required = true },
                new { id = "competitorContent", label = "Competitor page content", type = "longText", required = true },
                new { id = "sourceUrl", label = "Subject URL", type = "shortText", required = false },
                new
                {
                    id = "subjectCompleteness",
                    label = "Subject source completeness",
                    type = "select",
                    required = false,
                    options = new[] { "full", "partial" },
                    placeholder = "full",
                },
                new
                {
                    id = "competitorCompleteness",
                    label = "Competitor source completeness",
                    type = "select",
                    required = false,
                    options = new[] { "full", "partial" },
                    placeholder = "full",
                },
            },
        },
        "pillar-outline" => new
        {
            fields = new object[]
            {
                new { id = "topic", label = "Topic", type = "shortText", required = true },
                new { id = "relatedQueries", label = "Related queries", type = "longText", required = false },
                new { id = "supportingContentHints", label = "Supporting content hints", type = "longText", required = false },
                new { id = "sourceContent", label = "Source content", type = "longText", required = false },
                new { id = "sourceUrl", label = "Source URL", type = "shortText", required = false },
            },
        },
        "pillar-article" => new
        {
            fields = new object[]
            {
                new { id = "topic", label = "Topic", type = "shortText", required = true },
                new { id = "relatedQueries", label = "Related queries", type = "longText", required = false },
                new { id = "supportingContentHints", label = "Supporting content hints", type = "longText", required = false },
                new { id = "sourceContent", label = "Source content", type = "longText", required = true },
                new { id = "sourceUrl", label = "Source URL", type = "shortText", required = false },
            },
        },
        "competitive-response" => new
        {
            fields = new object[]
            {
                new { id = "brandName", label = "Brand name", type = "shortText", required = true },
                new { id = "competitorName", label = "Competitor name", type = "shortText", required = true },
                new { id = "brandContent", label = "Brand page content", type = "longText", required = true },
                new { id = "competitorContent", label = "Competitor page content", type = "longText", required = true },
                new { id = "focusQuery", label = "Focus query", type = "shortText", required = false },
                new { id = "sourceUrl", label = "Brand URL", type = "shortText", required = false },
                new
                {
                    id = "subjectCompleteness",
                    label = "Subject source completeness",
                    type = "select",
                    required = false,
                    options = new[] { "full", "partial" },
                    placeholder = "full",
                },
                new
                {
                    id = "competitorCompleteness",
                    label = "Competitor source completeness",
                    type = "select",
                    required = false,
                    options = new[] { "full", "partial" },
                    placeholder = "full",
                },
            },
        },
        _ => null,
    };

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

    private const string PillarArticleInputSchema =
        """
        {
          "type": "object",
          "required": ["topic", "sourceDocument"],
          "additionalProperties": false,
          "properties": {
            "contractVersion": { "const": "pillarArticleInput.v1" },
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
            "originate", "originate", "citable-claims", "claimLedger.v1", "claim-ledger",
            CitableClaimsInputSchema,
            ["diagnosticDocument.v1"],
            ["faqSet.v1", "pillarOutline.v1", "pillarArticle.v1"]),
        new("faq-generator", "FAQ Generator",
            "Generate answer-first FAQ pairs grounded in supplied queries and visible source content.",
            "originate", "originate", "faq-set", "faqSet.v1", "faq-list",
            FaqGeneratorInputSchema,
            ["queryPlan.v1", "diagnosticDocument.v1", "queryProvenance.v1"],
            ["schemaMarkup.v1"]),
        new("comparison-brief", "Comparison Brief",
            "Build a structured X vs Y brief from supplied brand and competitor pages.",
            "originate", "originate", "comparison-brief", "comparisonBrief.v1", "comparison-brief",
            ComparisonBriefInputSchema,
            ["pageSnapshot.v1", "competitorPageSnapshot.v1"],
            ["pillarOutline.v1", "pillarArticle.v1", "competitiveResponse.v1"]),
        new("pillar-outline", "Pillar Article Outline",
            "Produce a topic-cluster pillar outline and supporting-content plan from supplied inputs.",
            "originate", "originate", "pillar-outline", "pillarOutline.v1", "outline",
            PillarOutlineInputSchema,
            ["diagnosticDocument.v1", "queryProvenance.v1"],
            ["pillarArticle.v1", "faqSet.v1", "comparisonBrief.v1"]),
        new("pillar-article", "Pillar Article",
            "Produce a grounded topic-cluster pillar Markdown draft with supporting-content plan.",
            "originate", "originate", "pillar-article", "pillarArticle.v1", "pillar-article",
            PillarArticleInputSchema,
            ["diagnosticDocument.v1", "queryProvenance.v1", "pillarOutline.v1"],
            ["faqSet.v1", "comparisonBrief.v1", "schemaMarkup.v1"]),
        new("competitive-response", "Competitive Response",
            "Turn a competitor win into a brand-aligned response strategy without copying competitor prose.",
            "outrank", "outrank", "competitive-response", "competitiveResponse.v1", "response-plan",
            CompetitiveResponseInputSchema,
            ["pageSnapshot.v1", "competitorPageSnapshot.v1"],
            ["pillarOutline.v1", "pillarArticle.v1", "comparisonBrief.v1"]),
    ];

    private sealed record Contract(
        string CapabilityId, string DisplayName, string Description, string WorkflowGroup,
        string JasperWorkflow, string Endpoint, string ArtifactType, string Renderer, string InputSchema,
        IReadOnlyList<string> AcceptedInputs, IReadOnlyList<string> Downstream);
}
