using GeekAPI.Services.Workflow.Providers;
using Xunit;

namespace GeekBackend.Tests.Workflow.Providers;

/// <summary>
/// No provider carries a hardcoded model name; an unset model is refused, not substituted.
/// </summary>
/// <remarks>
/// <para>
/// Jeff, 2026-10-03: <i>"I prefer using Railway variables versus hard coding."</i> The three providers
/// shipped with model ids in source — and Groq's was a model its API had already retired, which is the
/// whole argument: an id in source outlives the release it was current for, and the failure is silent
/// spend on a model nobody picked rather than an error anyone can see.
/// </para>
/// <para>
/// This is the same rule <c>ContentProviderFactory</c> applies to providers — "silently substituting one
/// is how every create ends up billed against a model nobody chose" — one level down.
/// </para>
/// </remarks>
public class ProviderModelGuardTests
{
    [Fact]
    public void No_provider_option_carries_a_default_model()
    {
        // The defaults, not the behaviour: a default is invisible to any test that supplies a value.
        Assert.Equal(string.Empty, new OpenAiOptions().Model);
        Assert.Equal(string.Empty, new AnthropicOptions().Model);
        Assert.Equal(string.Empty, new GroqOptions().Model);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_unset_model_is_refused_and_the_message_names_the_variable(string? model)
    {
        // Whitespace counts as unset, the rule the API keys already follow: a variable set to "" is the
        // shape `??` lets through, and it has caused two auth outages here.
        var ex = Assert.Throws<ContentGenerationException>(
            () => ProviderModelGuard.Require(model, "LlmProviders__Anthropic__Model"));

        Assert.Contains("LlmProviders__Anthropic__Model", ex.Message, StringComparison.Ordinal);
        // Says nothing was spent, because the operator's first question on a refusal is whether it billed.
        Assert.Contains("no request was billed", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_configured_model_passes_through_untouched()
    {
        Assert.Equal("claude-opus-5", ProviderModelGuard.Require("claude-opus-5", "irrelevant"));
    }

    [Fact]
    public void The_endpoint_and_api_version_keep_their_defaults()
    {
        // The line being drawn: a model is a choice with a price, so it must be stated. A base URL and an
        // API version are protocol facts, not choices -- requiring them would be configuration noise that
        // every environment sets identically.
        Assert.False(string.IsNullOrWhiteSpace(new AnthropicOptions().BaseUrl));
        Assert.False(string.IsNullOrWhiteSpace(new AnthropicOptions().AnthropicVersion));
        Assert.False(string.IsNullOrWhiteSpace(new OpenAiOptions().BaseUrl));
        Assert.False(string.IsNullOrWhiteSpace(new GroqOptions().BaseUrl));
    }

    [Fact]
    public void No_model_id_is_written_anywhere_in_the_provider_options()
    {
        // The file that used to hold three. Asserted on source because a removed default leaves no
        // behaviour behind to test, and the next person to add a provider will copy this file.
        var source = Source("GeekAPI/Services/Workflow/Providers/ProviderOptions.cs");

        Assert.DoesNotContain("gpt-4o\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("claude-", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("gpt-oss", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("llama", source, StringComparison.OrdinalIgnoreCase);
    }

    private static string Source(string relative)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, relative)))
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!, relative));
    }
}
