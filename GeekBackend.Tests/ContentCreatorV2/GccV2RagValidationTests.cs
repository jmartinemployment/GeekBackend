using System.Text.Json;
using GeekAPI.Services.ContentCreatorV2.Validate;
using GeekAPI.Services.ContentCreatorV2.Write;
using GeekAPI.Services.Rag;
using GeekAPI.Services.Workflow.Domain.Entities;

namespace GeekBackend.Tests.ContentCreatorV2;

public sealed class GccV2RagValidationTests
{
    [Fact]
    public void Unsupported_claims_fail_ship_ready_even_when_approved()
    {
        var report = new GccV2ValidationReport(
            "approved",
            null,
            100,
            100,
            true,
            [],
            RagValidation: Validation(
                approved: true,
                unsupportedClaimCount: 1,
                issues:
                [
                    new RagValidationIssueDto
                    {
                        SectionTitle = "Evidence",
                        Category = RagValidationIssueCategory.UnsupportedClaim,
                        Detail = "Unsupported.",
                        RepairInstruction = "Remove the unsupported claim.",
                    },
                ]));

        Assert.False(report.ShipReady);
    }

    [Fact]
    public void Approved_rag_validation_remains_additive_to_deterministic_gates()
    {
        var report = new GccV2ValidationReport(
            "approved",
            null,
            100,
            100,
            true,
            [new OverlapHit("A", "B", "same", "a", "b", "Differentiate B.")],
            RagValidation: Validation(true, 0, []));

        Assert.False(report.ShipReady);
    }

    [Fact]
    public void Rag_rejection_fails_ship_ready()
    {
        var report = new GccV2ValidationReport(
            "rejected",
            "Needs repair.",
            100,
            100,
            true,
            [],
            RagValidation: Validation(
                false,
                0,
                [
                    new RagValidationIssueDto
                    {
                        Category = RagValidationIssueCategory.BriefAlignment,
                        Detail = "Misses the brief.",
                        RepairInstruction = "Address the required topic.",
                    },
                ]));

        Assert.False(report.ShipReady);
    }

    [Fact]
    public void Issue_mapping_preserves_exact_instruction_and_targets_unmatched_issue_deterministically()
    {
        var output = Output();
        var validation = Validation(
            false,
            0,
            [
                new RagValidationIssueDto
                {
                    SectionTitle = "Evidence",
                    Category = RagValidationIssueCategory.Usefulness,
                    Detail = "Needs an example.",
                    RepairInstruction = "Add one concrete source-grounded example.",
                },
                new RagValidationIssueDto
                {
                    SectionTitle = "Missing heading",
                    Category = RagValidationIssueCategory.BriefAlignment,
                    Detail = "Document-level mismatch.",
                    RepairInstruction = "Align the draft with the canonical brief.",
                },
            ]);

        var targets = GccV2ValidateService.SelectRagIssueTargets(output, validation);

        Assert.Equal("evidence", targets[0].SectionKey);
        Assert.Equal("Add one concrete source-grounded example.", targets[0].RevisionNotes);
        Assert.Equal("Missing heading", targets[1].ReportedSectionTitle);
        Assert.Equal("lede", targets[1].SectionKey);
        Assert.Equal("Align the draft with the canonical brief.", targets[1].RevisionNotes);
    }

    [Fact]
    public void Validate_stage_payload_persists_typed_result_citations_and_provenance()
    {
        var report = new GccV2ValidationReport(
            "rejected",
            "Unsupported.",
            80,
            90,
            true,
            [],
            RagValidation: Validation(
                false,
                1,
                [
                    new RagValidationIssueDto
                    {
                        SectionTitle = "Evidence",
                        Category = RagValidationIssueCategory.UnsupportedClaim,
                        Detail = "Unsupported.",
                        RepairInstruction = "Remove it.",
                    },
                ]),
            ValidationCitations:
            [
                new RagCitationDto
                {
                    PageId = "page-1",
                    Url = "https://example.test/evidence",
                    Quote = "Verified quote.",
                },
            ],
            ValidationProvenance: new RagGenerateProvenanceDto
            {
                GenerationStage = "validation",
                ModelUsed = "o3",
                ModelPolicyPreset = "best-quality",
                ModelPolicyVersion = "content-model-policy.v1",
                PromptVersion = "rag-validation.v1",
                Retrieval = "hybrid",
                EvidenceIds = ["page-1"],
            },
            ValidationModelUsed: "o3");

        var json = JsonSerializer.Serialize(
            GccV2ValidateService.BuildPersistedReportPayload(report, 1),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("unsupportedClaim", root.GetProperty("validation").GetProperty("issues")[0].GetProperty("category").GetString());
        Assert.Equal(90, root.GetProperty("validation").GetProperty("evidenceCoverageScore").GetDouble());
        Assert.Equal("page-1", root.GetProperty("validationCitations")[0].GetProperty("pageId").GetString());
        Assert.Equal("validation", root.GetProperty("validationProvenance").GetProperty("generationStage").GetString());
        Assert.Equal("o3", root.GetProperty("validationModelUsed").GetString());
    }

    [Theory]
    [InlineData("validation", "validation")]
    [InlineData(" VALIDATION ", "validation")]
    public void Validation_generation_stage_is_normalized(string raw, string expected) =>
        Assert.Equal(expected, RagGenerateService.NormalizeGenerationStage(raw));

    private static RagValidationDto Validation(
        bool approved,
        int unsupportedClaimCount,
        IReadOnlyList<RagValidationIssueDto> issues) =>
        new()
        {
            Approved = approved,
            Issues = issues,
            Strengths = ["Clear structure."],
            UnsupportedClaimCount = unsupportedClaimCount,
            BriefAlignmentScore = 90,
            EvidenceCoverageScore = 90,
            UsefulnessScore = 90,
            OriginalityScore = 90,
            BrandAlignmentScore = 90,
        };

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
