using GeekAPI.Services.ContentCreatorV2.Validate;
using GeekAPI.Services.ContentCreatorV2.Write;
using GeekAPI.Services.Rag;
using GeekAPI.Services.Workflow.Domain.Entities;

namespace GeekBackend.Tests.ContentCreatorV2;

/// <summary>
/// Jasper validation: reviewer <c>changesRequired</c> feeds VALIDATE→REPAIR;
/// <c>rejected</c> fails closed (no blind job requeue).
/// </summary>
public sealed class GccV2ReviewerRepairLoopTests
{
    [Fact]
    public void ChangesRequired_issues_merge_into_repair_targets_with_exact_instructions()
    {
        var baseValidation = new RagValidationDto
        {
            Approved = true,
            Issues = [],
            Strengths = ["Clear structure"],
            UnsupportedClaimCount = 0,
        };
        var reviewerIssues = new[]
        {
            new RagSpecialistReviewIssueDto(
                "unsupportedClaim", "error", "Evidence",
                "The claim is unsupported.",
                "Cite the Acme Research quote or remove the claim.", null),
            new RagSpecialistReviewIssueDto(
                "usefulness", "warning", "Introduction",
                "Needs a concrete example.",
                "Add one source-grounded example in the lede.", null),
        };

        var merged = GccV2ValidateService.MergeReviewerIssuesIntoValidation(
            baseValidation, reviewerIssues);
        var targets = GccV2ValidateService.SelectRagIssueTargets(Output(), merged);

        Assert.False(merged.Approved);
        Assert.Equal(2, merged.Issues.Count);
        Assert.Equal(1, merged.UnsupportedClaimCount);

        Assert.Equal(2, targets.Count);
        Assert.Equal("evidence", targets[0].SectionKey);
        Assert.Equal("Cite the Acme Research quote or remove the claim.", targets[0].RevisionNotes);
        Assert.Equal("Evidence", targets[0].ReportedSectionTitle);

        Assert.Equal("lede", targets[1].SectionKey);
        Assert.Equal("Add one source-grounded example in the lede.", targets[1].RevisionNotes);
        Assert.Equal("Introduction", targets[1].ReportedSectionTitle);
    }

    [Fact]
    public void Empty_reviewer_issues_leave_approved_validation_untouched()
    {
        var baseValidation = new RagValidationDto
        {
            Approved = true,
            Issues = [],
            Strengths = ["Clear structure"],
            UnsupportedClaimCount = 0,
            BriefAlignmentScore = 95,
        };

        var merged = GccV2ValidateService.MergeReviewerIssuesIntoValidation(baseValidation, []);

        Assert.True(merged.Approved);
        Assert.Empty(merged.Issues);
        Assert.Equal(95, merged.BriefAlignmentScore);
        Assert.Empty(GccV2ValidateService.SelectRagIssueTargets(Output(), merged));
    }

    [Theory]
    [InlineData("unsupportedClaim", RagValidationIssueCategory.UnsupportedClaim)]
    [InlineData("source_conflict", RagValidationIssueCategory.SourceConflict)]
    [InlineData("brandVoice", RagValidationIssueCategory.BrandVoice)]
    [InlineData("seo_geo", RagValidationIssueCategory.SeoGeo)]
    [InlineData("mystery-bucket", RagValidationIssueCategory.BriefAlignment)]
    public void Reviewer_category_aliases_map_onto_typed_validation_categories(
        string rawCategory, RagValidationIssueCategory expected)
    {
        var mapped = GccV2ValidateService.MapReviewerIssue(
            new RagSpecialistReviewIssueDto(
                rawCategory, "error", "Evidence", "Detail.", "Fix it.", null));

        Assert.Equal(expected, mapped.Category);
        Assert.Equal("Fix it.", mapped.RepairInstruction);
    }

    [Fact]
    public void Reviewer_rejection_and_changesRequired_never_blind_retry_the_job()
    {
        // Mirrors GccV2JobWorker: only IsTransient stops schedule AgentStageRetryScheduled.
        static bool WouldScheduleBlindRetry(RagAgentStoppedException ex, int attemptCount) =>
            ex.IsTransient && attemptCount < 3;

        var changes = new RagAgentStoppedException(
            RagAgentStopReason.ReviewerChangesRequired,
            "Revise claims.",
            [
                new RagSpecialistReviewIssueDto(
                    "unsupportedClaim", "error", "Evidence",
                    "Unsupported.", "Cite or remove.", null),
            ]);
        var rejected = new RagAgentStoppedException(
            RagAgentStopReason.ReviewerRejected, "Unsafe claims.", changes.ReviewIssues);
        var upstream = new RagAgentStoppedException(
            RagAgentStopReason.UpstreamFailure, "RAG 502");

        Assert.False(WouldScheduleBlindRetry(changes, attemptCount: 1));
        Assert.False(WouldScheduleBlindRetry(rejected, attemptCount: 1));
        Assert.True(WouldScheduleBlindRetry(upstream, attemptCount: 1));
        Assert.False(WouldScheduleBlindRetry(upstream, attemptCount: 3));

        // changesRequired carries issues for VALIDATE→REPAIR; rejection carries them for audit only.
        Assert.Single(changes.ReviewIssues);
        Assert.Equal("Cite or remove.", changes.ReviewIssues[0].Recommendation);
        Assert.Single(rejected.ReviewIssues);
    }

    [Fact]
    public void Rejected_decision_outranks_changesRequired_in_review_classification()
    {
        var issue = new RagSpecialistReviewIssueDto(
            "unsupportedClaim", "error", "Evidence", "Bad claim.", "Remove it.", null);
        var outcome = GeekAPI.Services.ContentCreatorV2.Generation.GccV2SpecialistCoordinator
            .ClassifyReviews(
            [
                new RagSpecialistReviewDto(
                    "reviewerOutput.v1", "section", "changesRequired", "Revise.", [issue], []),
                new RagSpecialistReviewDto(
                    "reviewerOutput.v1", "section", "rejected", "Fail closed.", [issue], []),
            ]);

        Assert.Equal("rejected", outcome.Decision);
        Assert.NotEmpty(outcome.Issues);
    }

    private static GccV2WriteOutput Output()
    {
        static GccV2WriteSection Write(string key, string heading) =>
            new(
                key,
                heading,
                "proof",
                new Section("h2", heading, [new TextParagraph([new Run("Text.")])], null, []),
                false);

        return new GccV2WriteOutput
        {
            Title = "Draft",
            MetaDescription = "Meta",
            Lede = Write("lede", "Introduction"),
            Sections = [Write("evidence", "Evidence")],
        };
    }
}
