using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GeekBackend.Tests;

/// <summary>
/// The LLM cost kill switch. Disabled must REFUSE, never substitute — canned text returned here would
/// be indistinguishable from a real draft downstream.
/// </summary>
public class ContentProviderFactoryKillSwitchTests
{
    private static ContentProviderFactory Build(bool enabled) =>
        new(new ServiceCollection().BuildServiceProvider(),
            Options.Create(new LlmProvidersOptions { Enabled = enabled, DefaultProvider = "OpenAi" }));

    [Fact]
    public void Disabled_refuses_the_default_provider()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Build(enabled: false).GetDefault());
        Assert.Contains("LlmProviders:Enabled=false", ex.Message);
        Assert.Contains("no request was billed", ex.Message);
    }

    [Fact]
    public void Disabled_refuses_an_explicitly_named_provider()
    {
        // Naming a provider must not route around the switch -- that is how a kill switch ends up
        // covering one path and not the one actually in use.
        var ex = Assert.Throws<InvalidOperationException>(
            () => Build(enabled: false).Get(LlmProviderType.OpenAi));
        Assert.Contains("LlmProviders:Enabled=false", ex.Message);
    }

    [Fact]
    public void Default_is_enabled_so_absent_config_cannot_silently_stop_production()
    {
        Assert.True(new LlmProvidersOptions().Enabled);
    }

    [Fact]
    public void Enabled_still_refuses_an_unknown_default_provider()
    {
        // The pre-existing no-fallback behaviour must survive the new gate.
        var factory = new ContentProviderFactory(
            new ServiceCollection().BuildServiceProvider(),
            Options.Create(new LlmProvidersOptions { Enabled = true, DefaultProvider = "NotAProvider" }));

        var ex = Assert.Throws<InvalidOperationException>(() => factory.GetDefault());
        Assert.Contains("not a known", ex.Message);
    }
}
