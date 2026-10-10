using System.Text.Json;
using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.ContentCreator.Partner;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.Providers;
using GeekApplication.Interfaces.ContentWriterV3;
using GeekApplication.Models.ContentCreator;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// Every model call on the generate path is the provider the operator chose.
/// </summary>
public class GccProviderChoiceTests
{
    [Fact]
    public void An_unmapped_provider_is_refused_rather_than_written_by_openai()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => GccGenerateService.ToLlm((ContentGeneratorProvider)99));

        Assert.StartsWith("Refused:", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Extraction_runs_on_the_provider_the_operator_chose()
    {
        var providers = new GccAngleQuoteProbeTests.RecordingProviders();
        var extraction = GccPartnerExtractionFakes.Scripted(providers, GccPartnerExtractionFakes.EmptyPageExtraction);

        await extraction.ExtractFromPagesAsync(
            [new GccQuoteablePage("https://melio.test/a", "A", [], ["Melio pays bills."])],
            ["Melio"],
            LlmProviderType.Anthropic,
            CancellationToken.None);

        Assert.Equal([LlmProviderType.Anthropic], providers.Requested);
    }
}
