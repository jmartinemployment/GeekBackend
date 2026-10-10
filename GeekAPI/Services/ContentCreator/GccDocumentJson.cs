using System.Text.Json;
using GeekAPI.Services.Workflow.Services;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// The one set of options a <c>ContentDocument</c> is written with on its way to anything that reads
/// it back as stored JSON: the artifact row, the SEO scorer's reader (<c>GcwBodyDocument</c>), the
/// batch and page checks. Paragraphs carry a <c>type</c> discriminator and every name is camel-cased.
/// </summary>
/// <remarks>
/// It lived as a private field of <c>GccGenerateService</c>. The page guard now serializes a draft to
/// count its keyword the way the scorer will, and a second options object built by hand there is how
/// a guard passes a page the report then fails.
/// </remarks>
internal static class GccDocumentJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new ParagraphJsonConverter());
        return options;
    }
}
