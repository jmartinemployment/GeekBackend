using System.Net;
using System.Text;
using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Services;
using GeekApplication.Models.ContentCreator;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// A tool page's title is "{Tool name}: {keyword}" -- "Ramp: Automated Approval Workflows" (Jeff, 2026-10-07:
/// a tool is written within the keyword and the problem it solves, and otherwise he would edit every title by
/// hand). The product stays in its own field, because the page's URL and Revise both need the product, not
/// the title: the URL stays /tools/.../ramp.
/// </summary>
public sealed class GccToolTitleTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static readonly Guid ProjectId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static string Envelope(string title, string? productName)
    {
        var document = new ContentDocument(
            new Section("h2", "Opening", [new TextParagraph([new Run("An opening.")])], null, []),
            [new Section("h2", "How it routes", [new TextParagraph([new Run("Approvals route by amount.")])], null, [])]);
        var envelope = new Dictionary<string, object?> { ["title"] = title };
        if (productName is not null) envelope["productName"] = productName;
        envelope["metaDescription"] = "A meta description.";
        envelope["body"] = document;
        return JsonSerializer.Serialize(envelope, Web);
    }

    // ---- the envelope ---------------------------------------------------------------------------

    [Fact]
    public void The_product_name_is_read_and_written_back_with_the_page()
    {
        var parsed = GccBodyEnvelope.Read(Envelope("Ramp: Automated Approval Workflows", "Ramp"), Web);

        Assert.Equal("Ramp: Automated Approval Workflows", parsed.Title);
        Assert.Equal("Ramp", parsed.ProductName);

        var written = GccBodyEnvelope.Write(parsed, parsed.Document!, Web);
        using var doc = JsonDocument.Parse(written);
        Assert.Equal("Ramp: Automated Approval Workflows", doc.RootElement.GetProperty("title").GetString());
        Assert.Equal("Ramp", doc.RootElement.GetProperty("productName").GetString());
    }

    [Fact]
    public void A_page_written_before_the_field_has_no_product_name_and_is_not_given_one()
    {
        var parsed = GccBodyEnvelope.Read(Envelope("Ramp", productName: null), Web);

        Assert.Equal("Ramp", parsed.Title);
        Assert.Null(parsed.ProductName);

        var written = GccBodyEnvelope.Write(parsed, parsed.Document!, Web);
        using var doc = JsonDocument.Parse(written);
        Assert.False(doc.RootElement.TryGetProperty("productName", out _));
    }

    // ---- the export -----------------------------------------------------------------------------

    [Theory]
    [InlineData("Ramp: Automated Approval Workflows", "Ramp")]
    [InlineData("Ramp", null)]
    public async Task The_exported_tool_page_is_titled_with_the_keyword_and_its_file_and_url_stay_the_products(
        string title, string? productName)
    {
        var artifact = new GccArtifactDto(
            Guid.NewGuid(), Guid.NewGuid(), ProjectId, null, "tool", "Ramp", "draft", DateTime.UtcNow, DateTime.UtcNow);
        var repo = new HttpGccRepository(
            new HttpClient(new Repository(artifact, Envelope(title, productName))) { BaseAddress = new Uri("http://repo.test/") },
            NullLogger<HttpGccRepository>.Instance);
        var export = new GccArtifactExportService(
            repo, Options.Create(new CompanyProfileOptions()), NullLogger<GccArtifactExportService>.Instance);

        var documents = await export.ExportProjectAsync(ProjectId, CancellationToken.None);

        var page = Assert.Single(documents);
        Assert.Equal("tools/ramp.html", page.FileName);
        Assert.Contains($"<title>{title}", page.Content, StringComparison.Ordinal);
        Assert.Contains("/tools/accounting/accounts-payable/ramp\"", page.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("ramp-automated-approval-workflows", page.Content, StringComparison.Ordinal);
    }

    private sealed class Repository(GccArtifactDto artifact, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.PathAndQuery;
            object? result =
                path == $"/repo/content-creator/projects/{ProjectId}/artifacts" ? new[] { artifact }
                : path.StartsWith("/repo/content-creator/creates/") ? new GccCreateDto(
                    artifact.CreateId, Guid.NewGuid(), Guid.NewGuid(), "tool",
                    "Accounts Payable: Automated Approval Workflows", null, null, null, null, null, "draft",
                    DateTime.UtcNow, DateTime.UtcNow, "accounting", ProjectId)
                : path.StartsWith("/repo/content-creator/versions?artifactId=")
                    ? new[] { new GccArtifactVersionDto(Guid.NewGuid(), artifact.Id, 1, body, null, 0, DateTime.UtcNow) }
                    : null;
            return Task.FromResult(result is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(result, Web), Encoding.UTF8, "application/json"),
                });
        }
    }
}
