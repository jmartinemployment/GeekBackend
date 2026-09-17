using GeekAPI.Services.Workflow.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GeekAPI.Services.Workflow.Providers;

public interface IContentProviderFactory
{
    IContentGenerationProvider Get(LlmProviderType providerType);
    IContentGenerationProvider GetDefault();
}

/// <summary>
/// Resolves the correct <see cref="IContentGenerationProvider"/> implementation using keyed DI
/// registrations. This is the seam that lets the orchestrator swap OpenAI / Anthropic / Groq
/// per-project (Project.PreferredProvider) without an if/else chain anywhere else in the app.
/// </summary>
public class ContentProviderFactory : IContentProviderFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly LlmProvidersOptions _options;

    public ContentProviderFactory(IServiceProvider serviceProvider, IOptions<LlmProvidersOptions> options)
    {
        _serviceProvider = serviceProvider;
        _options = options.Value;
    }

    public IContentGenerationProvider Get(LlmProviderType providerType)
    {
        RefuseIfDisabled();
        return _serviceProvider.GetRequiredKeyedService<IContentGenerationProvider>(providerType);
    }

    /// <summary>
    /// Stops every LLM call on this path when LlmProviders:Enabled is false.
    ///
    /// Placed on the factory because it is the one choke point both Get and GetDefault pass through —
    /// gating individual call sites leaves whichever one is added next un-gated, which is how
    /// GccV2JobWorker's kill switch nearly stopped covering PLAN.
    /// </summary>
    private void RefuseIfDisabled()
    {
        if (_options.Enabled) return;

        throw new InvalidOperationException(
            "LLM calls are disabled (LlmProviders:Enabled=false). Set it to true to generate. "
            + "Nothing was generated and no request was billed.");
    }

    public IContentGenerationProvider GetDefault()
    {
        // No fallback: a misconfigured DefaultProvider must not be quietly swapped for some other
        // provider. Silently substituting one is how every create ends up billed against a model
        // nobody chose. Bad configuration stops here instead.
        RefuseIfDisabled();

        if (!Enum.TryParse<LlmProviderType>(_options.DefaultProvider, ignoreCase: true, out var parsed))
        {
            throw new InvalidOperationException(
                $"LlmProviders:DefaultProvider is '{_options.DefaultProvider}', which is not a known "
                + $"provider. Valid values: {string.Join(", ", Enum.GetNames<LlmProviderType>())}.");
        }

        return Get(parsed);
    }
}
