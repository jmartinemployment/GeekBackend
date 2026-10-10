using GeekAPI.Services.ContentCreator;
using GeekApplication.Models.ContentCreator;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// Stage 3, the v1 side: create.BriefJson used to be dumped verbatim into the prompt. Render as
/// labeled prose instead, and prove it -- assert on the rendered block, not on behavior downstream
/// of it, per the plan's own "assert on the prompt, not the article" standard.
/// </summary>
public class GccGenerateServiceBriefBlockTests
{
    private static readonly GccGenerateService.BriefFields FullBrief = new()
    {
        Segment = "SMB operators",
        Details = ["budget-conscious", "time-poor"],
        Notes = "SMBs looking to implement AI",
        Angle = "practical-first",
        PrimaryIntent = "commercial_investigation",
        SecondaryIntent = "informational",
        BuyingStage = "consideration",
        ToneOfVoice = "consultant_professional",
        EeatSignals = ["experience", "expertise"],
        LengthBand = "long",
    };

    [Fact]
    public void Every_populated_field_renders_as_a_labeled_line()
    {
        var block = GccGenerateService.BuildBriefFieldsBlock(FullBrief);

        Assert.Contains("Audience segment: SMB operators (budget-conscious, time-poor)", block);
        Assert.Contains("Audience notes: SMBs looking to implement AI", block);
        Assert.Contains("Angle: practical-first", block);
        Assert.Contains("Primary intent: commercial investigation (the reader is weighing options before buying) + informational", block);
        Assert.Contains("Buying stage: consideration", block);
        Assert.Contains("Tone of voice: consultant, professional (objective authority)", block);
        Assert.Contains("E-E-A-T signals to demonstrate: experience, expertise.", block);
        Assert.Contains("Length band: long", block);
    }

    [Fact]
    public void Conflict_resolution_instruction_travels_with_the_notes_line()
    {
        // The old raw dump could not carry "if these disagree, follow this one" at all -- it was
        // sibling JSON keys with no relationship stated between them.
        var block = GccGenerateService.BuildBriefFieldsBlock(FullBrief);

        Assert.Contains("if this conflicts with the segment above, follow the notes", block);
    }

    [Fact]
    public void An_unpopulated_field_produces_no_line_rather_than_an_empty_one()
    {
        var minimal = new GccGenerateService.BriefFields { Segment = "SMB operators" };
        var block = GccGenerateService.BuildBriefFieldsBlock(minimal);

        Assert.Contains("Audience segment: SMB operators", block);
        Assert.DoesNotContain("Angle:", block);
        Assert.DoesNotContain("Tone of voice:", block);
    }

    [Fact]
    public void BuildBriefAndResearchBlock_never_contains_the_raw_brief_json_verbatim()
    {
        var rawBriefJson = """{"audienceSegment":"SMBs","primaryIntent":"commercial_investigation","buyingStage":"consideration","angle":"practical-first","toneOfVoice":"consultant_professional","eeatSignals":["experience"],"lengthBand":"long"}""";
        var create = new GccCreateDto(
            Id: Guid.NewGuid(), ClientId: Guid.NewGuid(), OwnerUserId: Guid.NewGuid(),
            StartingContentType: "techArticle", Topic: "AI implementation", Notes: null,
            ProjectSiteRunId: null, SiteSectionJson: null, BriefJson: rawBriefJson, ResearchJson: null,
            Status: "draft", CreatedAtUtc: DateTime.UtcNow, UpdatedAtUtc: DateTime.UtcNow);

        var block = GccGenerateService.BuildBriefAndResearchBlock(create);

        // The literal raw-JSON substring must not survive into the prompt -- that was the mistake.
        Assert.DoesNotContain(rawBriefJson, block);
        Assert.DoesNotContain("{\"audienceSegment\"", block);
        // But the information itself must still reach the model, as labeled prose.
        Assert.Contains("Primary intent: commercial investigation (the reader is weighing options before buying)", block);
        Assert.Contains("Buying stage: consideration", block);
    }
}
