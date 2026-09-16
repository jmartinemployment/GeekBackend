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

    public IContentGenerationProvider Get(LlmProviderType providerType) =>
        _serviceProvider.GetRequiredKeyedService<IContentGenerationProvider>(providerType);

    public IContentGenerationProvider GetDefault()
    {
        // No fallback: a misconfigured DefaultProvider must not be quietly swapped for some other
        // provider. Silently substituting one is how every create ends up billed against a model
        // nobody chose. Bad configuration stops here instead.
        if (!Enum.TryParse<LlmProviderType>(_options.DefaultProvider, ignoreCase: true, out var parsed))
        {
            throw new InvalidOperationException(
                $"LlmProviders:DefaultProvider is '{_options.DefaultProvider}', which is not a known "
                + $"provider. Valid values: {string.Join(", ", Enum.GetNames<LlmProviderType>())}.");
        }

        return Get(parsed);
    }
}
