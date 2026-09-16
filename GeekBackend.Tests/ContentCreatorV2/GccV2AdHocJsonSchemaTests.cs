using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using GeekAPI.Services.ContentCreatorV2.Generation;

namespace GeekBackend.Tests.ContentCreatorV2;

/// <summary>
/// JsonSchemaExporter marks the supplied options read-only, and reflection-based type resolution is
/// not picked up implicitly. Options without an explicit TypeInfoResolver therefore throw
/// "must specify a TypeInfoResolver setting before being marked as read-only" on the first real
/// export - which in production surfaced as a 500 on POST /creates/{id}/generate, not at startup.
/// </summary>
public sealed class GccV2AdHocJsonSchemaTests
{
    private sealed record Payload(string Name, int Count, List<string>? Tags, string? Note);

    [Fact]
    public void Options_without_a_resolver_fail_the_way_production_did()
    {
        var unresolved = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
        };

        var error = Assert.Throws<InvalidOperationException>(
            () => GccV2AdHocJsonSchema.For<Payload>(unresolved));
        Assert.Contains("TypeInfoResolver", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Options_with_an_explicit_resolver_export_a_usable_schema()
    {
        var resolved = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };

        var schema = GccV2AdHocJsonSchema.For<Payload>(resolved);

        using var document = JsonDocument.Parse(schema);
        var properties = document.RootElement.GetProperty("properties");
        Assert.True(properties.TryGetProperty("name", out _));
        Assert.True(properties.TryGetProperty("count", out _));
    }

    /// <summary>
    /// Every schema-constrained partner and competitor extraction 400'd in production:
    /// "Invalid 'response_format.json_schema.name': string does not match pattern.
    ///  Expected a string that matches the pattern '^[a-zA-Z0-9_-]+$'."
    /// The names carried the dotted ".v4" extractor-version style, and a dot is not in that set,
    /// so no page ever produced partner evidence and drafts fell back to generic prose.
    /// Checked with a plain character test rather than a pattern match.
    /// </summary>
    [Theory]
    [InlineData(GeekAPI.Services.ContentCreatorV2.Partner.GccV2PartnerExtractionService.ProviderSchemaName)]
    [InlineData(GeekAPI.Services.ContentCreatorV2.Competitor.GccV2CompetitorExtractionService.ProviderSchemaName)]
    public void Provider_schema_names_use_only_characters_openai_accepts(string schemaName)
    {
        Assert.False(string.IsNullOrWhiteSpace(schemaName));

        var illegal = schemaName
            .Where(c => !char.IsAsciiLetterOrDigit(c) && c != '_' && c != '-')
            .Distinct()
            .ToList();

        Assert.True(
            illegal.Count == 0,
            $"Schema name '{schemaName}' contains characters OpenAI rejects: "
            + $"{string.Join(", ", illegal.Select(c => $"'{c}'"))}. "
            + "Only letters, digits, underscore and hyphen are accepted.");
    }
}
