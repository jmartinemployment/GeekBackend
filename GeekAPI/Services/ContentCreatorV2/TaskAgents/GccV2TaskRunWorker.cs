using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Context;
using GeekAPI.Services.GeekCrawler;
using GeekAPI.Services.Workflow.Providers;

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
        var notifier = services.GetRequiredService<GccV2TaskAgentRunProgressNotifier>();
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

            run = await repo.TransitionTaskRunAsync(run.Id, new(
                "running", "analyzing", 25, "analysis-started",
                GccV2CanonicalJson.Serialize(new
                {
                    engine = isStudioTemplate ? GccV2StudioTemplateRenderer.Engine : "geek-crawler-rag",
                }),
                _instanceId, _instanceId,
                DateTimeOffset.UtcNow.AddMinutes(10), null), ct);
            await PushRunEventAsync(notifier, run, "Executing task agent.", ct);

            using var input = JsonDocument.Parse(run.InputJson);
            JsonElement output;
            string evidence;
            string citations;
            string? contextDigest = null;
            var parentArtifactVersionIds = new List<Guid>();
            string? lineageRelationship = null;
            if (!string.IsNullOrWhiteSpace(run.SourceSnapshotJson))
            {
                using var source = JsonDocument.Parse(run.SourceSnapshotJson);
                if (source.RootElement.TryGetProperty("parentArtifactVersionIds", out var parentsEl)
                    && parentsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var parent in parentsEl.EnumerateArray())
                    {
                        if (parent.ValueKind == JsonValueKind.String
                            && Guid.TryParse(parent.GetString(), out var parentId))
                            parentArtifactVersionIds.Add(parentId);
                        else if (parent.TryGetGuid(out parentId))
                            parentArtifactVersionIds.Add(parentId);
                    }
                }
                if (source.RootElement.TryGetProperty("lineageRelationship", out var relationshipEl)
                    && relationshipEl.ValueKind == JsonValueKind.String)
                    lineageRelationship = relationshipEl.GetString();
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
                output = await ExecuteStudioTemplateAsync(
                    services, definition, version, workflow.RootElement, input.RootElement, ct);
                evidence = "[]";
                citations = "[]";
            }
            else if (IsRoiProjection(workflow.RootElement))
            {
                JsonElement? observed = null;
                if (input.RootElement.TryGetProperty("observedTelemetry", out var observedEl)
                    && observedEl.ValueKind == JsonValueKind.Object)
                {
                    observed = observedEl.Clone();
                }
                output = GccV2RoiProjectionEngine.Execute(input.RootElement, observed);
                evidence = ExtractArray(output, "provenance", "evidence");
                citations = ExtractCitations(output);
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
                parentArtifactVersionIds.Distinct().ToList(),
                _instanceId,
                Relationship: string.IsNullOrWhiteSpace(lineageRelationship)
                    ? (parentArtifactVersionIds.Count == 0 ? null : "derived-from")
                    : lineageRelationship,
                ExpectedClaimedBy: _instanceId), ct);
            run = await repo.TransitionTaskRunAsync(run.Id, new(
                "succeeded", "complete", 100, "completed",
                GccV2CanonicalJson.Serialize(new
                {
                    artifactId = artifact.Id,
                    artifactVersionId = artifactVersion.Id,
                    artifactVersion.Digest,
                }),
                _instanceId, _instanceId, null, null), ct);
            await PushRunEventAsync(notifier, run, "Task agent complete.", ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Task-agent run {RunId} failed.", run.Id);
            try
            {
                run = await repo.TransitionTaskRunAsync(run.Id, new(
                    "failed", "failed", 100, "failed",
                    GccV2CanonicalJson.Serialize(new { error = exception.Message }),
                    _instanceId, _instanceId, null, exception.Message), ct);
                await PushRunEventAsync(notifier, run, exception.Message, ct);
            }
            catch (Exception transitionException)
            {
                logger.LogError(transitionException,
                    "Could not persist failure for task-agent run {RunId}.", run.Id);
            }
        }
    }

    private static async Task PushRunEventAsync(
        GccV2TaskAgentRunProgressNotifier notifier,
        GccV2TaskRunDto run,
        string? message,
        CancellationToken ct) =>
        await notifier.PushAsync(
            run,
            "update",
            GccV2TaskAgentRunProgressNotifier.LatestSeq(run),
            message,
            ct);

    private static bool IsRoiProjection(JsonElement workflow) =>
        workflow.TryGetProperty("engine", out var engine)
        && engine.ValueKind == JsonValueKind.String
        && string.Equals(engine.GetString(), GccV2RoiProjectionEngine.Engine, StringComparison.Ordinal);

    private static async Task<JsonElement> ExecuteStudioTemplateAsync(
        IServiceProvider services,
        GccV2TaskAgentDefinitionDto definition,
        GccV2TaskAgentVersionDto version,
        JsonElement workflow,
        JsonElement input,
        CancellationToken ct)
    {
        var instructionsTemplate = GccV2StudioLlmExecutor.ReadString(workflow, "instructionsTemplate");
        var exampleOutput = GccV2StudioLlmExecutor.ReadString(workflow, "exampleOutput");
        var evaluationPrompt = GccV2StudioLlmExecutor.ReadString(workflow, "evaluationPrompt");
        var temperature = GccV2StudioLlmExecutor.ReadTemperature(workflow);
        var model = GccV2StudioLlmExecutor.FirstAllowedModel(version.AllowedModelsJson);

        var render = GccV2StudioTemplateRenderer.Render(
            instructionsTemplate, definition.DisplayName, definition.Description, input);
        if (render.MissingTokens.Count > 0)
            throw new InvalidOperationException(
                $"Studio instruction template has unresolved tokens: {string.Join(", ", render.MissingTokens)}.");

        var providers = services.GetRequiredService<IContentProviderFactory>();
        var provider = providers.GetDefault();
        var request = GccV2StudioLlmExecutor.BuildRequest(
            render.RenderedInstructions,
            exampleOutput,
            evaluationPrompt,
            temperature,
            model);
        var completion = await provider.CompleteAsync(request, ct);
        var generated = (completion.Content ?? "").Trim();
        if (string.IsNullOrWhiteSpace(generated))
            throw new InvalidOperationException("Studio LLM returned empty content.");

        var payload = GccV2StudioLlmExecutor.BuildArtifactJson(
            render.RenderedInstructions,
            exampleOutput,
            evaluationPrompt,
            input,
            generated,
            string.IsNullOrWhiteSpace(completion.ModelUsed) ? (model ?? "default") : completion.ModelUsed);
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
