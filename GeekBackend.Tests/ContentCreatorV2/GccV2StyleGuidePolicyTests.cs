using System.Text.Json;
using GeekAPI.Services.ContentCreatorV2.Context;

namespace GeekBackend.Tests.ContentCreatorV2;

public sealed class GccV2StyleGuidePolicyTests
{
    [Fact]
    public void Accepts_typed_grammar_term_rules_and_legacy_phrase_lists()
    {
        using var document = JsonDocument.Parse("""
            {
              "schemaVersion": 1,
              "grammar": {
                "oxfordComma": true,
                "preferActiveVoice": true,
                "allowEmDash": false,
                "sentenceCaseHeadings": null
              },
              "termRules": [
                { "id": "no-synergy", "kind": "prohibit", "match": "synergy", "caseSensitive": false },
                { "id": "use-customers", "kind": "replace", "match": "users", "replacement": "customers" },
                { "id": "brand-case", "kind": "capitalize", "match": "Acme Cloud", "caseSensitive": true },
                { "id": "sso", "kind": "abbreviation", "match": "SSO", "replacement": "single sign-on" },
                { "id": "first", "kind": "firstMention", "match": "CRM", "replacement": "customer relationship management (CRM)" }
              ],
              "prohibitedPhrases": ["best-in-class"],
              "requiredPhrases": ["security review"],
              "customInstructions": "Prefer concrete operational outcomes."
            }
            """);

        Assert.Null(GccV2StyleGuidePolicy.Validate(document.RootElement));
    }

    [Fact]
    public void Rejects_unknown_term_kind_and_missing_replacement()
    {
        using var badKind = JsonDocument.Parse("""
            { "termRules": [{ "kind": "suggest", "match": "foo" }] }
            """);
        Assert.Contains("termRules.kind", GccV2StyleGuidePolicy.Validate(badKind.RootElement));

        using var missingReplacement = JsonDocument.Parse("""
            { "termRules": [{ "kind": "replace", "match": "users" }] }
            """);
        Assert.Contains("replacement", GccV2StyleGuidePolicy.Validate(missingReplacement.RootElement)!);
    }

    [Fact]
    public void Rejects_overlap_between_prohibited_and_required_phrases()
    {
        using var document = JsonDocument.Parse("""
            {
              "prohibitedPhrases": ["security review"],
              "requiredPhrases": ["Security Review"]
            }
            """);

        Assert.Contains("both prohibited and required",
            GccV2StyleGuidePolicy.Validate(document.RootElement));
    }

    [Fact]
    public void Rejects_non_object_payload()
    {
        using var document = JsonDocument.Parse("""["not", "an", "object"]""");
        Assert.Contains("JSON object", GccV2StyleGuidePolicy.Validate(document.RootElement));
    }
}
