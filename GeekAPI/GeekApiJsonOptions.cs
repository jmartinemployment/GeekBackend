using System.Text.Json;
using System.Text.Json.Serialization;
using GeekAPI.Services.Workflow.Domain.Entities;

namespace GeekAPI;

/// <summary>
/// The one definition of how this API binds and emits JSON, applied by Program.cs and reused by
/// tests. Internal-for-test in the same spirit as HttpGeekCrawlerRagClient.JsonOpts: a contract
/// test that builds its own options proves only that its copy agrees with itself.
///
/// RagIndexStatusWebhookContractTests held two hand-made copies and neither matched this. One set
/// PropertyNameCaseInsensitive without a naming policy, so it matched runId, RunId and RUNID
/// alike — the single mismatch class a name-bound wire contract exists to catch was the one it
/// could not see.
/// </summary>
public static class GeekApiJsonOptions
{
    /// <summary>Applies this API's policy and converters to an options instance.</summary>
    public static void Configure(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.Converters.Add(new TolerantNullableLedeTypeConverter());
        options.Converters.Add(new StrictLedeTypeConverter());
        options.Converters.Add(new JsonStringEnumConverter());
    }

    /// <summary>
    /// A standalone instance equivalent to what MVC model binding uses: the framework's Web
    /// defaults (camelCase, case-insensitive) plus <see cref="Configure"/>. Tests deserialize
    /// through this so they exercise the real binding rules.
    /// </summary>
    public static JsonSerializerOptions ForBinding()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Configure(options);
        return options;
    }
}
