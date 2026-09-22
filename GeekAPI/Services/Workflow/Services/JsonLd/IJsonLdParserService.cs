namespace GeekAPI.Services.Workflow.Services.JsonLd;

public interface IJsonLdParserService
{
    JsonLdSiteSummary Summarize(IReadOnlyList<string> rawBlocks);

    /// <summary>Every distinct schema.org @type declared across a page's JSON+LD blocks.</summary>
    IReadOnlyList<string> DistinctDeclaredTypes(IReadOnlyList<string> rawBlocks);
}
