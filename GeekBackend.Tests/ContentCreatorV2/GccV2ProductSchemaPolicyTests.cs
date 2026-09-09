using System.Text.Json;
using GeekAPI.Services.ContentCreatorV2.Context;

namespace GeekBackend.Tests.ContentCreatorV2;

public sealed class GccV2ProductSchemaPolicyTests
{
    [Fact]
    public void Accepts_typed_fields_object()
    {
        using var document = JsonDocument.Parse("""
            {
              "schemaVersion": 1,
              "fields": [
                {
                  "id": "11111111-1111-4111-8111-111111111111",
                  "key": "pricing",
                  "label": "Pricing",
                  "required": true,
                  "valueType": "string"
                }
              ]
            }
            """);

        Assert.Null(GccV2ProductSchemaPolicy.Validate(document.RootElement));
    }

    [Fact]
    public void Rejects_empty_fields_and_duplicate_keys()
    {
        using var empty = JsonDocument.Parse("""{ "fields": [] }""");
        Assert.Contains("at least one field", GccV2ProductSchemaPolicy.Validate(empty.RootElement));

        using var duplicate = JsonDocument.Parse("""
            {
              "fields": [
                {
                  "id": "11111111-1111-4111-8111-111111111111",
                  "key": "pricing",
                  "label": "Pricing"
                },
                {
                  "id": "22222222-2222-4222-8222-222222222222",
                  "key": "Pricing",
                  "label": "Other"
                }
              ]
            }
            """);
        Assert.Contains("keys must be unique", GccV2ProductSchemaPolicy.Validate(duplicate.RootElement));
    }
}
