using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Context;
using GeekAPI.Services.GeekCrawler;

namespace GeekAPI.Services.ContentCreatorV2.TaskAgents;

public sealed class GccV2TaskRunWorker(
    IServiceScopeFactory scopeFactory,
    IHostEnvironment environment,
    ILogger<GccV2TaskRunWorker> logger) : BackgroundService
{
    private readonly string _instanceId = $"task-agent:{Environment.MachineName}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (environment.IsEnvironment("Testing")) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<HttpGccV2Repository>();
                var run = await repo.ClaimNextTaskRunAsync(
                    _instanceId, leaseSeconds: 600, stoppingToken);
                if (run is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                    continue;
                }
                await ExecuteRunAsync(scope.ServiceProvider, repo, run, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Task-agent worker loop failed.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task ExecuteRunAsync(
        IServiceProvider services,
        HttpGccV2Repository repo,
        GccV2TaskRunDto run,
        CancellationToken ct)
    {
        try
        {
            var definition = await repo.GetTaskAgentAsync(
                run.TaskAgentDefinitionId.ToString("D"), ct)
                ?? throw new InvalidOperationException("Pinned task-agent definition is unavailable.");
            var version = definition.Versions.SingleOrDefault(x => x.Id == run.TaskAgentVersionId)
                ?? throw new InvalidOperationException("Pinned task-agent version is unavailable.");
            if (!string.Equals(version.VersionDigest, run.TaskAgentVersionDigest, StringComparison.Ordinal))
                throw new InvalidOperationException("Pinned task-agent version digest no longer matches.");

            using var workflow = JsonDocument.Parse(version.WorkflowJson);
            var artifactType = workflow.RootElement.TryGetProperty("artifactType", out var artifactTypeEl)
                ? artifactTypeEl.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(artifactType))
                throw new InvalidOperationException("Task-agent artifact type is missing.");

            var isStudioTemplate = workflow.RootElement.TryGetProperty("engine", out var engine)
                && engine.ValueKind == JsonValueKind.String
                && string.Equals(engine.GetString(), GccV2StudioTemplateRenderer.Engine, StringComparison.Ordinal)
                && workflow.RootElement.TryGetProperty("mode", out var mode)
                && mode.ValueKind == JsonValueKind.String
                && string.Equals(mode.GetString(), GccV2StudioTemplateRenderer.Mode, StringComparison.Ordinal);

            await repo.TransitionTaskRunAsync(run.Id, new(
                "running", "analyzing", 25, "analysis-started",
                GccV2CanonicalJson.Serialize(new
                {
                    engine = isStudioTemplate ? GccV2StudioTemplateRenderer.Engine : "geek-crawler-rag",
                }),
                _instanceId, _instanceId,
                DateTimeOffset.UtcNow.AddMinutes(10), null), ct);

            using var input = JsonDocument.Parse(run.InputJson);
            JsonElement output;
            string evidence;
            string citations;
            string? contextDigest = null;
            if (!string.IsNullOrWhiteSpace(run.SourceSnapshotJson))
            {
                using var source = JsonDocument.Parse(run.SourceSnapshotJson);
                if (source.RootElement.TryGetProperty("contextEnvelope", out var envelope)
                    && envelope.ValueKind == JsonValueKind.Object)
                {
                    var canonical = envelope.TryGetProperty("canonicalJson", out var canonicalEl)
                        ? canonicalEl.GetString()
                        : null;
                    var digest = envelope.TryGetProperty("digest", out var digestEl)
                        ? digestEl.GetString()
                        : null;
                    var signature = envelope.TryGetProperty("signature", out var signatureEl)
                        ? signatureEl.GetString()
                        : null;
                    var signingKeyId = envelope.TryGetProperty("signingKeyId", out var keyEl)
                        ? keyEl.GetString()
                        : null;
                    if (!string.IsNullOrWhiteSpace(canonical)
                        && !string.IsNullOrWhiteSpace(digest)
                        && !string.IsNullOrWhiteSpace(signature)
                        && !string.IsNullOrWhiteSpace(signingKeyId))
                    {
                        var resolver = services.GetRequiredService<GccV2ContextResolver>();
                        resolver.VerifyTaskAgentEnvelope(canonical, digest, signature, signingKeyId);
                        if (!string.IsNullOrWhiteSpace(run.ContextManifestDigest)
                            && !string.Equals(run.ContextManifestDigest, digest, StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException(
                                "Pinned task-agent context digest no longer matches its signed envelope.");
                        }
                        contextDigest = digest;
                    }
                }
            }
            if (isStudioTemplate)
            {
                output = ExecuteStudioTemplate(definition, workflow.RootElement, input.RootElement);
                evidence = "[]";
                citations = "[]";
            }
            else
            {
                var endpoint = workflow.RootElement.TryGetProperty("endpoint", out var endpointEl)
                    ? endpointEl.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(endpoint))
                    throw new InvalidOperationException("Task-agent workflow endpoint is missing.");
                var rag = services.GetRequiredService<IGeekCrawlerRagClient>();
                var ragOutput = await rag.RunDiagnosticAsync(endpoint, input.RootElement.Clone(), ct)
                    ?? throw new InvalidOperationException("The analysis engine is unavailable.");
                var actualArtifactType = ragOutput.TryGetProperty("artifactType", out var returnedType)
                    ? returnedType.GetString()
                    : null;
                if (!string.Equals(actualArtifactType, artifactType, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"Diagnostic returned '{actualArtifactType ?? "(missing)"}' instead of '{artifactType}'.");
                output = ragOutput;
                evidence = ExtractArray(output, "provenance", "evidence");
                citations = ExtractCitations(output);
            }
            if (!string.IsNullOrWhiteSpace(contextDigest))
            {
                evidence = MergeContextEvidence(evidence, contextDigest, run.ContextManifestId);
            }

            var artifact = await repo.CreateTaskArtifactAsync(run.Id, new(
                run.OwnerUserId, artifactType, _instanceId, _instanceId), ct);
            var artifactVersion = await repo.CreateTaskArtifactVersionAsync(artifact.Id, new(
                run.OwnerUserId,
                GccV2CanonicalJson.Serialize(output),
                evidence,
                citations,
                "valid",
                GccV2CanonicalJson.Serialize(new
                {
                    validator = "task-agent-output-contract.v1",
                    artifactType,
                    valid = true,
                }),
                [],
                _instanceId,
                ExpectedClaimedBy: _instanceId), ct);
            await repo.TransitionTaskRunAsync(run.Id, new(
                "succeeded", "complete", 100, "completed",
                GccV2CanonicalJson.Serialize(new
                {
                    artifactId = artifact.Id,
                    artifactVersionId = artifactVersion.Id,
                    artifactVersion.Digest,
                }),
                _instanceId, _instanceId, null, null), ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Task-agent run {RunId} failed.", run.Id);
            try
            {
                await repo.TransitionTaskRunAsync(run.Id, new(
                    "failed", "failed", 100, "failed",
                    GccV2CanonicalJson.Serialize(new { error = exception.Message }),
                    _instanceId, _instanceId, null, exception.Message), ct);
            }
            catch (Exception transitionException)
            {
                logger.LogError(transitionException,
                    "Could not persist failure for task-agent run {RunId}.", run.Id);
            }
        }
    }

    private static JsonElement ExecuteStudioTemplate(
        GccV2TaskAgentDefinitionDto definition,
        JsonElement workflow,
        JsonElement input)
    {
        var instructionsTemplate = workflow.TryGetProperty("instructionsTemplate", out var template)
            && template.ValueKind == JsonValueKind.String
            ? template.GetString() ?? ""
            : "";
        var exampleOutput = workflow.TryGetProperty("exampleOutput", out var example)
            && example.ValueKind == JsonValueKind.String
            ? example.GetString() ?? ""
            : "";
        var render = GccV2StudioTemplateRenderer.Render(
            instructionsTemplate, definition.DisplayName, definition.Description, input);
        if (render.MissingTokens.Count > 0)
            throw new InvalidOperationException(
                $"Studio instruction template has unresolved tokens: {string.Join(", ", render.MissingTokens)}.");

        var payload = GccV2CanonicalJson.Serialize(new
        {
            artifactType = GccV2StudioTemplateRenderer.ArtifactType,
            methodology = "instruction-template-dry-execution.v1",
            renderedInstructions = render.RenderedInstructions,
            exampleOutput,
            inputs = input,
            warnings = new[]
            {
                "LLM generation is not enabled for Studio v1; this artifact validates the template and inputs only.",
            },
        });
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.Clone();
    }

    private static string ExtractArray(JsonElement artifact, string parent, string property)
    {
        if (artifact.TryGetProperty(parent, out var container)
            && container.ValueKind == JsonValueKind.Object
            && container.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.Array)
            return GccV2CanonicalJson.Serialize(value);
        return "[]";
    }

    private static string ExtractCitations(JsonElement artifact)
    {
        if (!artifact.TryGetProperty("provenance", out var provenance)
            || provenance.ValueKind != JsonValueKind.Object
            || !provenance.TryGetProperty("source", out var source))
            return "[]";
        return GccV2CanonicalJson.Serialize(new[] { source.Clone() });
    }

    private static string MergeContextEvidence(string evidenceJson, string contextDigest, Guid? manifestId)
    {
        var items = new List<object>();
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(evidenceJson) ? "[]" : evidenceJson);
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in document.RootElement.EnumerateArray())
                    items.Add(item.Clone());
            }
        }
        catch (JsonException)
        {
            // Preserve a failed parse by starting from an empty evidence list.
        }
        items.Add(new
        {
            kind = "governed-context-manifest",
            contextManifestId = manifestId,
            digest = contextDigest,
        });
        return GccV2CanonicalJson.Serialize(items);
    }
}
