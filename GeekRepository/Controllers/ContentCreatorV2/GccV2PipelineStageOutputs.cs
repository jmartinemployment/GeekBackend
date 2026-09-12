using System.Text.Json;

namespace GeekRepository.Controllers.ContentCreatorV2;

/// <summary>Stage output builders for Geek Content Pipeline runs.</summary>
internal static class GccV2PipelineStageOutputs
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string ForTaskRun(
        string? capabilityId,
        string displayName,
        string lifecycle,
        int workItemIndex,
        Guid taskRunId,
        Guid artifactVersionId,
        string artifactType,
        string preview,
        JsonElement artifact)
    {
        return JsonSerializer.Serialize(new
        {
            artifactType,
            capabilityId,
            summary = preview,
            lifecycle,
            workItemIndex,
            mode = "task-run",
            taskRunId = taskRunId.ToString("D"),
            artifactVersionId = artifactVersionId.ToString("D"),
            displayName,
            artifact,
        }, JsonOpts);
    }

    public static string ForHandoff(string? handoff, string displayName, string lifecycle, int workItemIndex) =>
        JsonSerializer.Serialize(new
        {
            handoff,
            summary = $"Completed {displayName} handoff.",
            lifecycle,
            workItemIndex,
        }, JsonOpts);

    public static string ForCanvasHandoff(
        string displayName,
        string lifecycle,
        int workItemIndex,
        Guid projectId,
        Guid assetId,
        Guid assetVersionId,
        Guid taskRunId,
        Guid artifactVersionId,
        string artifactType,
        string title) =>
        JsonSerializer.Serialize(new
        {
            handoff = "canvas",
            mode = "canvas-attach",
            summary = $"Attached {title} to Canvas project.",
            lifecycle,
            workItemIndex,
            displayName,
            projectId = projectId.ToString("D"),
            assetId = assetId.ToString("D"),
            assetVersionId = assetVersionId.ToString("D"),
            taskRunId = taskRunId.ToString("D"),
            artifactVersionId = artifactVersionId.ToString("D"),
            artifactType,
            title,
        }, JsonOpts);

    public static string ForPublishHandoff(
        string displayName,
        string lifecycle,
        int workItemIndex,
        Guid? projectId,
        Guid? assetId,
        string? title) =>
        JsonSerializer.Serialize(new
        {
            handoff = "publish",
            mode = "publish-ready",
            summary = projectId is null
                ? $"Marked {displayName} ready without a Canvas asset."
                : $"Marked “{title}” ready to publish (no external CMS).",
            lifecycle,
            workItemIndex,
            displayName,
            projectId = projectId?.ToString("D"),
            assetId = assetId?.ToString("D"),
            title,
            externalCms = false,
        }, JsonOpts);

    public static string ForApprovalPending(string displayName, string lifecycle, int workItemIndex) =>
        JsonSerializer.Serialize(new
        {
            mode = "approval-pending",
            summary = $"Waiting for operator approval: {displayName}.",
            lifecycle,
            workItemIndex,
            displayName,
        }, JsonOpts);

    public static string ForApprovalApproved(
        string displayName, string lifecycle, int workItemIndex, string actorUserId) =>
        JsonSerializer.Serialize(new
        {
            mode = "approval-approved",
            summary = $"Approved {displayName}.",
            lifecycle,
            workItemIndex,
            displayName,
            approvedBy = actorUserId,
        }, JsonOpts);

    public static string ForApprovalRejected(
        string displayName, string lifecycle, int workItemIndex, string actorUserId) =>
        JsonSerializer.Serialize(new
        {
            mode = "approval-rejected",
            summary = $"Rejected {displayName}.",
            lifecycle,
            workItemIndex,
            displayName,
            rejectedBy = actorUserId,
        }, JsonOpts);

    /// <summary>
    /// Deterministic directional ROI stub aligned with gcc-roi-formulas.v1 defaults
    /// (workflowVolume 48, baseline 90m, assisted 25m, adoption 0.7, success 0.65, …).
    /// </summary>
    public static string ForRoiProjection(int workItemIndex, string lifecycle)
    {
        const double workflowVolume = 48;
        const double baselineMinutes = 90;
        const double assistedMinutes = 25;
        const double adoptionRate = 0.7;
        const double successfulUseRate = 0.65;
        const double loadedHourlyCost = 85;
        const double redeploymentFactor = 0.7;
        const double externalSpend = 24000;
        const double replaceableShare = 0.35;
        const double totalCostOfOwnership = 18000;

        static object Scenario(string id, string label, double timeFactor, double adoptionFactor, double externalFactor)
        {
            var savedHours = workflowVolume * ((baselineMinutes - assistedMinutes) / 60.0)
                * (adoptionRate * adoptionFactor) * successfulUseRate * timeFactor;
            var productivity = savedHours * loadedHourlyCost * redeploymentFactor;
            var external = externalSpend * replaceableShare * (adoptionRate * adoptionFactor) * externalFactor;
            var gross = productivity + external;
            var net = gross - totalCostOfOwnership;
            var roiPercent = totalCostOfOwnership > 0 ? net / totalCostOfOwnership * 100 : (double?)null;
            return new
            {
                scenario = id,
                label,
                savedHours,
                productivityValue = productivity,
                externalCostAvoided = external,
                grossBenefit = gross,
                netBenefit = net,
                roiPercent,
            };
        }

        return JsonSerializer.Serialize(new
        {
            artifactType = "roiProjection.v1",
            formulaVersion = "gcc-roi-formulas.v1",
            contractVersion = "roiBusinessCalculatorInput.v1",
            capabilityId = "roi-business-calculator",
            lifecycle,
            workItemIndex,
            summary = "Directional ROI projection from pipeline Optimize stage.",
            scenarios = new[]
            {
                Scenario("conservative", "Conservative", 0.75, 0.85, 0.9),
                Scenario("expected", "Expected", 1, 1, 1),
                Scenario("upside", "Upside", 1.25, 1.1, 1.05),
            },
            evidenceLabels = new
            {
                projections = "modeled",
                reconciliation = "experimental",
                cashClaims = "not-asserted",
            },
            warnings = new[]
            {
                "Directional model only — not a quote, guarantee, or audited finance result.",
                "Capacity created is not cash saved.",
            },
        }, JsonOpts);
    }
}
