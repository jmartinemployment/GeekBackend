using System.Net;
using System.Text;
using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.Workflow.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// Each use of OpenAI has a key of its own (Jeff, 2026-11-23), named in Railway exactly as it is named in OpenAI's console. A request is
/// sent with the key for its own task class and with no other, and a use whose key is missing fails by name: there is no shared key and
/// no environment variable to fall back to, because a fallback is how one use ends up spending on another's key unnoticed.
/// </summary>
public sealed class OpenAiProviderKeyPerUseTests
{
    private const string Model = "gpt-4.1";

    private static OpenAiProvider Provider(Capture handler, OpenAiOptions openAi)
    {
        openAi.Model = Model;
        openAi.BaseUrl = "http://localhost/v1/chat/completions";
        return new OpenAiProvider(new HttpClient(handler), Options.Create(new LlmProvidersOptions { OpenAi = openAi }), NullLogger<OpenAiProvider>.Instance);
    }

    private static ChatCompletionRequest Request(LlmTaskClass taskClass) =>
        new([new ChatMessage(ChatRole.User, "write")], MaxOutputTokens: 1024, TaskClass: taskClass);

    private static OpenAiOptions AllThree() => new()
    {
        WritingApiKey = "key-writing",
        ExtractionApiKey = "key-extraction",
        UtilityApiKey = "key-utility",
    };

    [Theory]
    [InlineData(LlmTaskClass.Writing, "key-writing")]
    [InlineData(LlmTaskClass.Extraction, "key-extraction")]
    [InlineData(LlmTaskClass.Utility, "key-utility")]
    public async Task A_request_is_sent_with_the_key_for_its_own_use(LlmTaskClass taskClass, string expectedKey)
    {
        var handler = new Capture();
        var provider = Provider(handler, AllThree());

        await provider.CompleteAsync(Request(taskClass));

        Assert.Equal("Bearer " + expectedKey, handler.Authorization);
    }

    [Theory]
    [InlineData(LlmTaskClass.Writing, "content-creator-v2")]
    [InlineData(LlmTaskClass.Extraction, "extraction")]
    [InlineData(LlmTaskClass.Utility, "utility")]
    public async Task A_use_with_no_key_fails_by_name_and_sends_nothing_even_when_the_other_uses_have_theirs(LlmTaskClass taskClass, string variable)
    {
        var handler = new Capture();
        var options = AllThree();
        switch (taskClass)
        {
            case LlmTaskClass.Writing: options.WritingApiKey = string.Empty; break;
            case LlmTaskClass.Extraction: options.ExtractionApiKey = string.Empty; break;
            default: options.UtilityApiKey = string.Empty; break;
        }
        var provider = Provider(handler, options);

        var ex = await Assert.ThrowsAsync<ContentGenerationException>(() => provider.CompleteAsync(Request(taskClass)));

        Assert.Contains(variable, ex.Message, StringComparison.Ordinal);
        Assert.Contains("no shared key", ex.Message, StringComparison.Ordinal);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public void The_setting_names_are_exactly_the_names_the_keys_have_in_openai()
    {
        Assert.Equal("content-creator-v2", OpenAiOptions.KeyVariableFor(LlmTaskClass.Writing));
        Assert.Equal("extraction", OpenAiOptions.KeyVariableFor(LlmTaskClass.Extraction));
        Assert.Equal("utility", OpenAiOptions.KeyVariableFor(LlmTaskClass.Utility));
    }

    private sealed class Capture : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Authorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"model\":\"gpt-4.1\",\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"ok\"},\"finish_reason\":\"stop\"}],"
                    + "\"usage\":{\"prompt_tokens\":1,\"completion_tokens\":1}}",
                    Encoding.UTF8, "application/json"),
            });
        }
    }
}
