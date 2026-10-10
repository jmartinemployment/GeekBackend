using GeekAPI.Services.ContentCreator.Partner;
using GeekAPI.Services.ContentCreator.Generation;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.Providers;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// Partner extraction requires a content provider. Tests that are not exercising extraction itself
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

internal static class PartnerExtractionTestDoubles
{
    /// <summary>A partner extraction service that yields an empty document without calling any model.</summary>
    public static GccPartnerExtractionService InertPartner() =>
        new(new GccSchemaConstrainedGenerator(),
            new NoProviderFactory(),
            NullLogger<GccPartnerExtractionService>.Instance);
}
