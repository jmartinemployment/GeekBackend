using GeekAPI.Services.Social;
using GeekAPI.Services.Social.LanguageModel;
using GeekAPI.Services.Workflow.Providers;
using GeekApplication.Interfaces.Social;
using GeekApplication.Models.Social;
using Microsoft.Extensions.DependencyInjection;

namespace GeekBackend.Tests.Social;

/// <summary>
/// Which provider GeekSocial calls is GeekAPI's <c>LlmProviders__DefaultProvider</c>, the setting
/// Content Creator also reads. There is no default: unset, every call fails not_configured and the
/// service still starts. A value that names no provider stops the service from starting, and every
/// value Content Creator accepts is accepted.
/// </summary>
public class SocialModelSettingsTests
{
    private static SocialModelSettings With(params (string Name, string? Value)[] values) =>
        SocialModelSettings.From(name => values.FirstOrDefault(v => v.Name == name).Value);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Unset_names_no_provider(string? value)
    {
        Assert.Null(With((SocialModelSettings.ProviderVariable, value)).Provider);
    }

    [Theory]
    [InlineData("anthropic", SocialModelProviders.Anthropic)]
    [InlineData("Anthropic", SocialModelProviders.Anthropic)]
    [InlineData(" OPENAI ", SocialModelProviders.OpenAi)]
    [InlineData("OpenAi", SocialModelProviders.OpenAi)]
    [InlineData("groq", SocialModelProviders.Groq)]
    [InlineData("Gemini", SocialModelProviders.Gemini)]
    [InlineData("perplexity", SocialModelProviders.Perplexity)]
    [InlineData("1", SocialModelProviders.OpenAi)]
    [InlineData("2", SocialModelProviders.Anthropic)]
    [InlineData("3", SocialModelProviders.Groq)]
    public void Every_value_content_creator_accepts_and_gemini_and_perplexity_name_a_provider(string value, string provider)
    {
        Assert.Equal(provider, With((SocialModelSettings.ProviderVariable, value)).Provider);
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("chatgpt")]
    [InlineData("0")]
    [InlineData("7")]
    [InlineData("open ai")]
    public void A_value_that_names_no_provider_throws_and_names_the_setting(string value)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => With((SocialModelSettings.ProviderVariable, value)));

        Assert.Contains(SocialModelSettings.ProviderVariable, ex.Message, StringComparison.Ordinal);
        Assert.Contains(value.Trim(), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_key_and_model_come_from_the_providers_own_section()
    {
        var settings = With(
            (SocialModelSettings.ProviderVariable, "gemini"),
            ("LlmProviders__Gemini__ApiKey", " gemini-key "),
            ("LlmProviders__Gemini__Model", "gemini-model"),
            ("LlmProviders__OpenAi__ApiKey", "openai-key"));

        Assert.Equal("gemini-key", settings.ApiKey);
        Assert.Equal("gemini-model", settings.Model);
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models", settings.BaseUrl);
        Assert.Equal("LlmProviders__Gemini__ApiKey", settings.KeyVariable);
        Assert.Equal("LlmProviders__Gemini__Model", settings.ModelVariable);
    }

    [Theory]
    [InlineData(SocialModelProviders.Anthropic, "https://api.anthropic.com/v1/messages")]
    [InlineData(SocialModelProviders.OpenAi, "https://api.openai.com/v1/chat/completions")]
    [InlineData(SocialModelProviders.Groq, "https://api.groq.com/openai/v1/chat/completions")]
    [InlineData(SocialModelProviders.Perplexity, "https://api.perplexity.ai/v1/agent")]
    public void Each_provider_has_its_own_address(string provider, string address)
    {
        var settings = With((SocialModelSettings.ProviderVariable, provider));

        Assert.Equal(address, settings.BaseUrl);
        Assert.Equal(120, settings.TimeoutSeconds);
        Assert.Equal(string.Empty, settings.ApiKey);
        Assert.Equal(string.Empty, settings.Model);
    }

    [Fact]
    public void Only_anthropic_falls_back_to_ANTHROPIC_API_KEY()
    {
        Assert.Equal("env-key", With((SocialModelSettings.ProviderVariable, "anthropic"), (SocialModelSettings.AnthropicKeyVariable, "env-key")).ApiKey);
        Assert.Equal(string.Empty, With((SocialModelSettings.ProviderVariable, "openai"), (SocialModelSettings.AnthropicKeyVariable, "env-key")).ApiKey);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("soon")]
    public void A_timeout_that_is_not_a_positive_number_throws(string value)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            With((SocialModelSettings.ProviderVariable, "openai"), ("LlmProviders__OpenAi__TimeoutSeconds", value)));

        Assert.Contains("LlmProviders__OpenAi__TimeoutSeconds", ex.Message, StringComparison.Ordinal);
    }

    // ----- Registration: the one client the setting names, and nothing else -----

    private static ServiceProvider Register(params (string Name, string? Value)[] values)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddSingleton(new LlmConcurrencyGate(1));
        services.AddSocialAgents(name => values.FirstOrDefault(v => v.Name == name).Value);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    [Theory]
    [InlineData("anthropic", typeof(AnthropicSocialModelClient))]
    [InlineData("OpenAi", typeof(OpenAiSocialModelClient))]
    [InlineData("groq", typeof(GroqSocialModelClient))]
    [InlineData("gemini", typeof(GeminiSocialModelClient))]
    [InlineData("perplexity", typeof(PerplexitySocialModelClient))]
    public void The_setting_chooses_the_one_client_registered(string value, Type client)
    {
        using var provider = Register((SocialModelSettings.ProviderVariable, value));
        using var scope = provider.CreateScope();

        var model = scope.ServiceProvider.GetRequiredService<ISocialModelClient>();
        Assert.IsType(client, model);
        Assert.Single(provider.GetServices<ISocialModelClient>());
        Assert.Equal(SocialModelProviders.Parse(value), model.Provider);
    }

    [Fact]
    public void Unset_registers_the_client_that_refuses_every_call_and_the_service_still_starts()
    {
        using var provider = Register();
        using var scope = provider.CreateScope();

        Assert.IsType<UnconfiguredSocialModelClient>(scope.ServiceProvider.GetRequiredService<ISocialModelClient>());
        Assert.Null(provider.GetRequiredService<SocialModelSettings>().Provider);
    }

    [Fact]
    public void An_unknown_provider_stops_registration()
    {
        Assert.Throws<InvalidOperationException>(() => Register((SocialModelSettings.ProviderVariable, "claude")));
    }

    [Fact]
    public void The_retired_clearance_stops_registration()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Register(
            (SocialModelSettings.ProviderVariable, "anthropic"),
            (SocialAiClearance.RetiredVariable, SocialPlatforms.Facebook)));

        Assert.Contains(SocialAiClearance.Variable, ex.Message, StringComparison.Ordinal);
    }
}
