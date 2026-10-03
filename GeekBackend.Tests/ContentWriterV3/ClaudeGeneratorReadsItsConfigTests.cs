using GeekAPI.Services.ContentWriterV3;
using GeekAPI.Services.Workflow.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GeekBackend.Tests.ContentWriterV3;

/// <summary>
/// <see cref="ClaudeContentGenerator"/> reads <c>LlmProviders:Anthropic</c> rather than hardcoding.
/// </summary>
/// <remarks>
/// <para>
/// It used to pin <c>claude-sonnet-4-5-20250929</c> and the messages URL in consts and read
/// <c>ANTHROPIC_API_KEY</c> directly, while <c>AnthropicProvider</c> read <c>AnthropicOptions</c>. So
/// "the Anthropic model" was two different values depending on which path ran, and setting
/// <c>LlmProviders__Anthropic__ApiKey</c> configured one of them. Jeff, 2026-10-03: "This should be fixed
/// instead left alone."
/// </para>
/// <para>
/// The generator is reachable — <c>POST api/.../{id}/generate</c> with <c>Provider: "Anthropic"</c>
/// resolves it through <c>ContentGeneratorFactory</c> — so this was a live inconsistency, not dead code.
/// </para>
/// </remarks>
public class ClaudeGeneratorReadsItsConfigTests
{
    private static ClaudeContentGenerator Generator(AnthropicOptions anthropic)
    {
        var options = Options.Create(new LlmProvidersOptions { Anthropic = anthropic });
        return new ClaudeContentGenerator(
            new SingleClientFactory(), options, NullLogger<ClaudeContentGenerator>.Instance);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task With_no_usable_key_it_refuses_by_name_rather_than_sending_an_empty_credential(string key)
    {
        // The old constructor did `?? string.Empty` and set x-api-key from it, so an unconfigured process
        // sent a request with an empty key and got an opaque 401 -- a failure naming the wrong cause.
        // Whitespace is the exact shape `??` lets through: set, non-null and useless.
        var previous = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", null);
        try
        {
            var generator = Generator(new AnthropicOptions { ApiKey = key });

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => generator.GenerateStructuredDraftAsync(
                    "problem_solution", "SMB finance leads", "evaluation", "book a call", ["evidence"]));

            Assert.Contains("LlmProviders__Anthropic__ApiKey", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", previous);
        }
    }

    [Fact]
    public void Nothing_about_the_endpoint_is_hardcoded_any_more()
    {
        // Asserted on the source, because the defect was being invisible TO configuration: a test that
        // only exercises behaviour cannot see a const that configuration never reaches.
        var source = Source();

        Assert.DoesNotContain("private const string ClaudeModel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private const string ClaudeApiUrl", source, StringComparison.Ordinal);
        Assert.Contains("_options.Model", source, StringComparison.Ordinal);
        Assert.Contains("_options.BaseUrl", source, StringComparison.Ordinal);
        Assert.Contains("_options.AnthropicVersion", source, StringComparison.Ordinal);
    }

    [Fact]
    public void It_does_not_construct_its_own_HttpClient()
    {
        // `new HttpClient()` in a scoped service is socket exhaustion waiting to happen, and it is why the
        // headers were constructor defaults -- which is what made the key un-refusable at call time.
        var source = Source();

        Assert.DoesNotContain("new HttpClient()", source, StringComparison.Ordinal);
        Assert.Contains("IHttpClientFactory", source, StringComparison.Ordinal);
    }

    [Fact]
    public void The_pinned_model_id_is_gone_and_the_configured_default_is_current()
    {
        // claude-sonnet-4-5-20250929 was two minor versions behind AnthropicOptions.Model. The point of
        // the fix is that there is now one value to keep current, so this pins which one it is.
        Assert.DoesNotContain("claude-sonnet-4-5-20250929", Source(), StringComparison.Ordinal);
        Assert.Equal("claude-sonnet-5", new AnthropicOptions().Model);
    }

    private static string Source()
    {
        const string relative = "GeekAPI/Services/ContentWriterV3/ClaudeContentGenerator.cs";
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, relative)))
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!, relative));
    }

    /// <summary>The one thing the generator needs from DI that a unit test cannot get for free.</summary>
    private sealed class SingleClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
