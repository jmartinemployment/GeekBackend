using System.Collections.Generic;
using ContentWriter.Domain.Entities;
using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.ContentCreator.Guardrail;
using GeekApplication.Models.ContentCreator;

namespace GeekBackend.Tests;

public class GccBriefAndGuardrailTests
{
    private static GccCreateDto Create(string? briefJson) => new(
        Id: Guid.NewGuid(),
        ClientId: Guid.NewGuid(),
        OwnerUserId: Guid.NewGuid(),
        StartingContentType: "blog",
        Topic: "ai marketing",
        Notes: null,
        SiteAnalysisId: null,
        SiteSectionJson: null,
        BriefJson: briefJson,
        ResearchJson: null,
        Status: "draft",
        CreatedAtUtc: DateTime.UtcNow,
        UpdatedAtUtc: DateTime.UtcNow);

    [Fact]
    public void ValidateBriefRequired_accepts_canonical_google_aligned_brief()
    {
        const string brief = """
        {
          "primaryIntent": "commercial_investigation",
          "buyingStage": "awareness",
          "audienceSegment": "affinity",
          "audienceNotes": "SMB owners",
          "angle": "comparative",
          "ctaType": "book_now",
          "toneOfVoice": "commercial_balanced",
          "eeatSignals": ["expertise", "trustworthiness"],
          "lengthBand": "blog"
        }
        """;

        // Should not throw.
        GccGenerateService.ValidateBriefRequired(Create(brief));
    }

    [Fact]
    public void ValidateBriefRequired_accepts_legacy_names_during_compat_window()
    {
        // Legacy brief: old field names, numeric toneOfVoice object, no eeatSignals.
        const string legacy = """
        {
          "intent": "informational",
          "buyingStage": "awareness",
          "audiencePrimary": "cold_prospect",
          "audienceDetail": "SMB owners",
          "angle": "case_study",
          "ctaType": "book_demo",
          "toneOfVoice": { "formalCasual": 2 },
          "lengthBand": "blog"
        }
        """;

        // Should not throw: legacy names satisfy requirements and tone/eeat are not
        // enforced for briefs that have not been migrated.
        GccGenerateService.ValidateBriefRequired(Create(legacy));
    }

    [Fact]
    public void ValidateBriefRequired_surfaces_missing_fields_by_name()
    {
        const string brief = """
        { "primaryIntent": "informational", "toneOfVoice": "commercial_balanced" }
        """;

        var ex = Assert.Throws<InvalidOperationException>(
            () => GccGenerateService.ValidateBriefRequired(Create(brief)));
        Assert.Contains("buyingStage", ex.Message);
        Assert.Contains("eeatSignals", ex.Message);
    }

    [Fact]
    public void ConsultantAppendix_applies_for_consultant_tone_and_ultimate_guide()
    {
        Assert.NotEqual(string.Empty, GccGenerateService.BuildConsultantAppendix(
            Create("""{ "toneOfVoice": "consultant_professional", "angle": "comparative" }""")));
        Assert.NotEqual(string.Empty, GccGenerateService.BuildConsultantAppendix(
            Create("""{ "toneOfVoice": "commercial_balanced", "angle": "ultimate_guide" }""")));
        Assert.Equal(string.Empty, GccGenerateService.BuildConsultantAppendix(
            Create("""{ "toneOfVoice": "commercial_balanced", "angle": "comparative" }""")));
    }

    [Fact]
    public void Guardrail_strips_and_replaces_banned_phrases()
    {
        var (clean, flagged) = ContentGuardrail.Clean(
            "We utilize a synergistic approach in today's fast-paced digital world.");
        Assert.Equal(3, flagged);
        Assert.Contains("use", clean);
        Assert.Contains("collaborative strategy", clean);
        Assert.DoesNotContain("utilize", clean);
        Assert.DoesNotContain("in today's fast-paced digital world", clean);
    }

    [Fact]
    public void Guardrail_walks_document_runs_and_counts_flags()
    {
        var lede = new Section("h2", "Overview", new List<Paragraph>
        {
            new TextParagraph(new List<Run> { new("Let us delve deeper here.") }),
        }, null, new List<Section>());
        var doc = new ContentDocument(lede, new List<Section>());

        var result = ContentGuardrail.Apply(doc);

        Assert.Equal(1, result.FlaggedCount);
        var text = ((TextParagraph)result.Document.Lede.Paragraphs[0]).Runs[0].Text;
        Assert.Contains("examine", text);
        Assert.DoesNotContain("delve deeper", text);
    }
}
