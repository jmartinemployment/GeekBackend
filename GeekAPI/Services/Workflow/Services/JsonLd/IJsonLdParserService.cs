namespace GeekAPI.Services.Workflow.Services.JsonLd;

public interface IJsonLdParserService
{
    JsonLdSiteSummary Summarize(IReadOnlyList<string> rawBlocks);
}
