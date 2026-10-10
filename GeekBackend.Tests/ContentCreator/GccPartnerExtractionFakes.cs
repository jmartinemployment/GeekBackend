using GeekAPI.Services.ContentCreator.Generation;
using GeekAPI.Services.ContentCreator.Partner;
using GeekAPI.Services.Workflow.Providers;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// Shared fakes for constructing a real <see cref="GccPartnerExtractionService"/> in tests that
/// need <see cref="GccGenerateService"/>'s full constructor but don't exercise partner extraction
/// themselves (pillar/blog/provenance tests). <see cref="GccGenerateServiceToolPageGroundingTests"/>
/// uses <see cref="ScriptedSchemaConstrainedGenerator"/> directly to control what extraction returns.
/// </summary>
internal static class GccPartnerExtractionFakes
{
    /// <summary>Never actually invoked by tests that don't call GenerateToolPageAsync with a
    /// non-null create -- throws if that assumption is ever wrong, rather than silently
    /// returning a success-shaped empty result that would mask the bug.</summary>
    internal sealed class UnusedSchemaConstrainedGenerator : IGccSchemaConstrainedGenerator
    {
        public Task<GccSchemaConstrainedCompletion<T>> CompleteAsync<T>(
            GccSchemaConstrainedRequest request, IContentGenerationProvider provider,
            System.Text.Json.JsonSerializerOptions? deserializeOptions, CancellationToken ct)
            where T : notnull =>
            throw new InvalidOperationException(
                "UnusedSchemaConstrainedGenerator was called -- this test wasn't expected to reach partner extraction.");
    }

    internal static GccPartnerExtractionService NeverInvoked(IContentProviderFactory providers) =>
        new(new UnusedSchemaConstrainedGenerator(), providers, NullLogger<GccPartnerExtractionService>.Instance);

    /// <summary>Returns the same scripted <see cref="PartnerPageExtraction"/> for every page the
    /// real <see cref="GccPartnerExtractionService"/> asks it to extract from -- one call per
    /// page, always requesting <c>T = PartnerPageExtraction</c>, per
    /// <c>GccPartnerExtractionService.ExtractOnePageAsync</c>.</summary>
    internal sealed class ScriptedSchemaConstrainedGenerator(PartnerPageExtraction result)
        : IGccSchemaConstrainedGenerator
    {
        public Task<GccSchemaConstrainedCompletion<T>> CompleteAsync<T>(
            GccSchemaConstrainedRequest request, IContentGenerationProvider provider,
            System.Text.Json.JsonSerializerOptions? deserializeOptions, CancellationToken ct)
            where T : notnull
        {
            if (typeof(T) != typeof(PartnerPageExtraction))
                throw new NotSupportedException($"Fake only supports PartnerPageExtraction, got {typeof(T)}.");
            var completion = new GccSchemaConstrainedCompletion<PartnerPageExtraction>(
                result, "test-model", null, null);
            return Task.FromResult((GccSchemaConstrainedCompletion<T>)(object)completion);
        }
    }

    /// <summary>All-null <see cref="PartnerPageExtraction"/> -- extraction ran, found nothing.</summary>
    internal static readonly PartnerPageExtraction EmptyPageExtraction = new(
        null, null, null, null, null, null, null, null, null, null, null, null, null, null, null,
        null, null, null, null, null);

    internal static GccPartnerExtractionService Scripted(
        IContentProviderFactory providers, PartnerPageExtraction result) =>
        new(new ScriptedSchemaConstrainedGenerator(result), providers,
            NullLogger<GccPartnerExtractionService>.Instance);
}
