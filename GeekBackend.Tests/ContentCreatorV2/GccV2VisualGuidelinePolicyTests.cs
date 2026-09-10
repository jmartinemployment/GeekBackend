using System.Text.Json;
using GeekAPI.Services.ContentCreatorV2.Context;

namespace GeekBackend.Tests.ContentCreatorV2;

public sealed class GccV2VisualGuidelinePolicyTests
{
    [Fact]
    public void Accepts_typed_palette_typography_logo_and_layout()
    {
        using var document = JsonDocument.Parse("""
            {
              "schemaVersion": 1,
              "palette": { "primary": "#0F172A", "accent": null },
              "typography": { "displayFont": "Source Serif 4", "minBodySizePx": 16 },
              "logoUsage": {
                "clearSpaceRatio": 0.5,
                "allowedBackgrounds": ["light"],
                "prohibitedTreatments": ["stretch"]
              },
              "layout": { "preferFullBleedHero": true, "maxContentWidthPx": 1200 },
              "imagery": { "styleNotes": "Documentary", "prohibitedMotifs": ["handshake"] },
              "customInstructions": "Prefer full-bleed photography."
            }
            """);

        Assert.Null(GccV2VisualGuidelinePolicy.Validate(document.RootElement));
    }

    [Fact]
    public void Rejects_unknown_palette_key_and_bad_body_size()
    {
        using var badKey = JsonDocument.Parse("""
            { "palette": { "neon": "#ff00ff" } }
            """);
        Assert.Contains("Unknown Visual Guidelines palette key",
            GccV2VisualGuidelinePolicy.Validate(badKey.RootElement));

        using var badSize = JsonDocument.Parse("""
            { "typography": { "minBodySizePx": 2 } }
            """);
        Assert.Contains("minBodySizePx",
            GccV2VisualGuidelinePolicy.Validate(badSize.RootElement));
    }
}
