using System.Text.Json;
using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// (De)serializes a create's ResearchJson. Follow-URLs (hand-entered fetch) has been retired —
/// research is populated only by uploads (see GccController.UploadKeywordSource).
/// </summary>
public static class GccResearchFetchService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string Serialize(GccResearchDocument doc) =>
        JsonSerializer.Serialize(doc, JsonOpts);

    public static GccResearchDocument? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        return JsonSerializer.Deserialize<GccResearchDocument>(json, JsonOpts);
    }
}
