using GeekApplication.Blog;
using System.Text.Json;

namespace GeekBackend.Tests;

public class UseCasePostNormalizerTests
{
    [Theory]
    [InlineData("Prospecting & Lead Intelligence | Sales AI Use Cases", "Prospecting & Lead Intelligence")]
    [InlineData("Automated Fraud Detection and Risk Assessment | Accounting AI Use Case", "Automated Fraud Detection and Risk Assessment")]
    [InlineData("Unlocking Conversational Financial Intelligence for Your Business", "Unlocking Conversational Financial Intelligence for Your Business")]
    [InlineData("Financial Forecasting &amp; Reporting | Accounting AI Use Cases", "Financial Forecasting & Reporting")]
    public void ToDisplayTitle_strips_seo_suffix(string input, string expected) =>
        Assert.Equal(expected, UseCasePostNormalizer.ToDisplayTitle(input));

    [Fact]
    public void PatchSchemaHeadlines_updates_graph_article_node()
    {
        const string schema = """
            {
              "@context": "https://schema.org",
              "@graph": [
                {
                  "@type": "TechnicalArticle",
                  "headline": "Prospecting & Lead Intelligence | Sales AI Use Cases"
                },
                {
                  "@type": "SoftwareApplication",
                  "name": "HubSpot"
                }
              ]
            }
            """;

        var patched = UseCasePostNormalizer.PatchSchemaHeadlines(schema, "Prospecting & Lead Intelligence");

        using var doc = JsonDocument.Parse(patched);
        var headline = doc.RootElement.GetProperty("@graph")[0].GetProperty("headline").GetString();
        var appName = doc.RootElement.GetProperty("@graph")[1].GetProperty("name").GetString();

        Assert.Equal("Prospecting & Lead Intelligence", headline);
        Assert.Equal("HubSpot", appName);
    }
}
