using GeekAPI.Services.ContentCreatorV2.Competitor;
using GeekAPI.Services.ContentCreatorV2.Generation;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.Providers;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeekBackend.Tests.ContentCreatorV2;

/// <summary>
/// Competitor extraction requires a content provider. Tests that are not exercising extraction itself
/// use a factory with no provider configured, which makes extraction fail closed and silent — it
/// returns an empty document rather than throwing or inventing payloads.
/// </summary>
internal sealed class NoProviderFactory : IContentProviderFactory
{
    public IContentGenerationProvider Get(LlmProviderType providerType) =>
        throw new InvalidOperationException("No content provider configured in tests.");

    public IContentGenerationProvider GetDefault() =>
        throw new InvalidOperationException("No content provider configured in tests.");
}

internal static class CompetitorExtractionTestDoubles
{
    /// <summary>An extraction service that yields an empty document without calling any model.</summary>
    public static GccV2CompetitorExtractionService Inert() =>
        new(new GccV2SchemaConstrainedGenerator(),
            new NoProviderFactory(),
            NullLogger<GccV2CompetitorExtractionService>.Instance);
}
