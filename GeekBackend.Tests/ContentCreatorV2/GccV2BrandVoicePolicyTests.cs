using System.Text.Json;
using GeekAPI.Services.ContentCreatorV2.Context;

namespace GeekBackend.Tests.ContentCreatorV2;

public sealed class GccV2BrandVoicePolicyTests
{
    [Fact]
    public void Accepts_valid_voice_policy()
    {
        using var document = JsonDocument.Parse("""
            {
              "schemaVersion": 1,
              "toneAttributes": ["clear", "confident"],
              "preferredPhrases": ["evidence-first"],
              "avoidPhrases": ["synergy"],
              "bannedClaims": ["guaranteed ROI"],
              "customInstructions": "Sound like a precise operator."
            }
            """);
        Assert.Null(GccV2BrandVoicePolicy.Validate(document.RootElement));
    }

    [Fact]
    public void Rejects_duplicate_avoid_phrases()
    {
        using var duplicates = JsonDocument.Parse("""
            { "avoidPhrases": ["synergy", "Synergy"] }
            """);
        Assert.Contains("unique", GccV2BrandVoicePolicy.Validate(duplicates.RootElement));
    }

    [Fact]
    public void ValidateKitJson_checks_nested_voice_policy()
    {
        Assert.Null(GccV2BrandVoicePolicy.ValidateKitJson("""{"companyName":"Acme"}"""));
        Assert.Contains("unique", GccV2BrandVoicePolicy.ValidateKitJson(
            """{"voicePolicy":{"avoidPhrases":["a","A"]}}"""));
    }

    [Fact]
    public void Rejects_non_object_payload()
    {
        using var document = JsonDocument.Parse("[]");
        Assert.Contains("JSON object", GccV2BrandVoicePolicy.Validate(document.RootElement));
    }
}
