using System.Reflection;
using GeekAPI.Services.Workflow.Providers;
using Xunit;

namespace GeekBackend.Tests.Workflow.Providers;

/// <summary>
/// Anthropic requests must not carry <c>temperature</c>.
/// </summary>
/// <remarks>
/// <para>
/// Anthropic's current models reject it outright: <c>400 invalid_request_error</c>,
/// <i>"`temperature` is deprecated for this model"</i>. Jeff, 2026-10-03, on the first real Anthropic
/// generate.
/// </para>
/// <para>
/// This is tested rather than left to the comment because of how it failed. Not one call degraded --
/// every call failed, and the failure arrived wearing someone else's clothes: all four long-form types
/// errored, and all 108 partner extraction pages across five partners threw, which the pre-flight
/// reported as <b>"0 of 5 partners can be grounded ... no capability signal"</b>. A provider fault
/// presented as a data shortage. The extraction path says "this is a fault, not a data shortage"
/// precisely so that distinction survives, and it still read as five unusable partners.
/// </para>
/// </remarks>
public class AnthropicSendsNoTemperatureTests
{
    private static PropertyInfo TemperatureProperty()
    {
        var dto = typeof(AnthropicProvider)
            .GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public)
            .Single(t => t.Name.Contains("Request"));
        return dto.GetProperty("Temperature", BindingFlags.Public | BindingFlags.Instance)!;
    }

    [Fact]
    public void The_request_shape_leaves_temperature_out_when_it_is_null()
    {
        // Nullable plus WhenWritingNull is what keeps the key out of the JSON entirely. A non-nullable
        // double would serialise as `"temperature": 0`, which is still sending it -- and 0 is a worse
        // value to send by accident than the one that was configured.
        var prop = TemperatureProperty();

        Assert.Equal(typeof(double?), prop.PropertyType);
        var ignore = prop.GetCustomAttribute<System.Text.Json.Serialization.JsonIgnoreAttribute>();
        Assert.NotNull(ignore);
        Assert.Equal(System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull, ignore!.Condition);
    }

    [Fact]
    public void The_provider_never_assigns_a_temperature()
    {
        // Asserted on the source: the assignment is the thing that regresses, and a reader adding
        // `Temperature = request.Temperature` back would satisfy every behavioural test here.
        var src = Source("GeekAPI/Services/Workflow/Providers/AnthropicProvider.cs");

        Assert.Contains("Temperature = null", src, StringComparison.Ordinal);
        Assert.DoesNotContain("Temperature = request.Temperature", src, StringComparison.Ordinal);
    }

    [Fact]
    public void OpenAi_and_Groq_still_send_it()
    {
        // The drop is Anthropic-specific. ChatCompletionRequest.Temperature is set deliberately per
        // prompt -- 0.3 for the tool FAQ, 0.7 for a lede -- and those providers still honour it.
        //
        // OpenAI already drops it for reasoning models ("reasoning ? null : request.Temperature"),
        // which is the same problem solved per-model. Anthropic is unconditional instead because every
        // current Anthropic model rejects it; the ones that took it are retired, so a name check would
        // be a second list to keep current for no live case.
        Assert.Contains("request.Temperature",
            Source("GeekAPI/Services/Workflow/Providers/OpenAiProvider.cs"), StringComparison.Ordinal);
        Assert.Contains("request.Temperature",
            Source("GeekAPI/Services/Workflow/Providers/GroqProvider.cs"), StringComparison.Ordinal);
    }

    private static string Source(string relative)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, relative)))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!, relative));
    }
}
