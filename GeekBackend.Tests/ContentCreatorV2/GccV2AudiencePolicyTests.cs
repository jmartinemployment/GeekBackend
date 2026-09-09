using System.Text.Json;
using GeekAPI.Services.ContentCreatorV2.Context;

namespace GeekBackend.Tests.ContentCreatorV2;

public sealed class GccV2AudiencePolicyTests
{
    [Fact]
    public void Accepts_typed_persona_fields_and_language_lists()
    {
        using var document = JsonDocument.Parse("""
            {
              "schemaVersion": 1,
              "summary": "Technical buyers evaluating evidence systems.",
              "locale": "en-US",
              "industries": ["software"],
              "roles": ["VP Engineering"],
              "pains": ["ungrounded AI claims"],
              "goals": ["citeable drafts"],
              "buyingTriggers": ["audit pressure"],
              "useCases": ["content ops"],
              "readingLevel": "professional",
              "preferredLanguage": ["specific", "operational"],
              "bannedTopics": ["hype"],
              "avoidPhrases": ["synergy"],
              "positioningStatement": "Evidence first.",
              "valuePropositions": [{ "title": "Proof", "description": "Every claim cites a source." }],
              "objectionResponses": [{ "objection": "Too slow", "response": "Review gates are bounded." }],
              "additionalCharacteristics": [{ "key": "region", "value": "NA" }],
              "customInstructions": "Prefer concrete systems."
            }
            """);

        Assert.Null(GccV2AudiencePolicy.Validate(document.RootElement));
    }

    [Fact]
    public void Rejects_invalid_reading_level_and_duplicate_banned_topics()
    {
        using var badLevel = JsonDocument.Parse("""
            { "readingLevel": "casual" }
            """);
        Assert.Contains("readingLevel", GccV2AudiencePolicy.Validate(badLevel.RootElement));

        using var duplicates = JsonDocument.Parse("""
            { "bannedTopics": ["Hype", "hype"] }
            """);
        Assert.Contains("unique", GccV2AudiencePolicy.Validate(duplicates.RootElement));
    }

    [Fact]
    public void Rejects_value_proposition_without_title()
    {
        using var document = JsonDocument.Parse("""
            { "valuePropositions": [{ "title": "", "description": "Missing title" }] }
            """);
        Assert.Contains("title", GccV2AudiencePolicy.Validate(document.RootElement));
    }

    [Fact]
    public void Rejects_non_object_payload()
    {
        using var document = JsonDocument.Parse("""["not", "an", "object"]""");
        Assert.Contains("JSON object", GccV2AudiencePolicy.Validate(document.RootElement));
    }
}
