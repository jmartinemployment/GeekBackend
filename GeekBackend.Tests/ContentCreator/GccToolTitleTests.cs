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
/// hand). The product stays in its own field, because the page's URL needs the product, not the title: the
/// URL stays /tools/.../ramp.
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
    public void The_product_name_is_read_with_the_page()
    {
        var parsed = GccBodyEnvelope.Read(Envelope("Ramp: Automated Approval Workflows", "Ramp"), Web);

        Assert.Equal("Ramp: Automated Approval Workflows", parsed.Title);
        Assert.Equal("Ramp", parsed.ProductName);
    }

    [Fact]
    public void A_page_written_before_the_field_has_no_product_name()
    {
        var parsed = GccBodyEnvelope.Read(Envelope("Ramp", productName: null), Web);

        Assert.Equal("Ramp", parsed.Title);
        Assert.Null(parsed.ProductName);
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

    // ---- the hero text -------------------------------------------------------------------------

    private static string EnvelopeWithSummary(string title, string? summary)
    {
        var document = new ContentDocument(
            new Section("h2", "Opening", [new TextParagraph([new Run("An opening.")])], null, []),
            [new Section("h2", "How it routes", [new TextParagraph([new Run("Approvals route by amount.")])], null, [])]);
        var envelope = new Dictionary<string, object?> { ["title"] = title };
        if (summary is not null) envelope["summary"] = summary;
        envelope["metaDescription"] = "A meta description.";
        envelope["body"] = document;
        return JsonSerializer.Serialize(envelope, Web);
    }

    [Theory]
    [InlineData("pillar")]
    [InlineData("blog")]
    [InlineData("tool")]
    public async Task Every_long_form_page_exports_an_h1_with_its_summary_directly_under_it(string type)
    {
        var artifact = new GccArtifactDto(
            Guid.NewGuid(), Guid.NewGuid(), ProjectId, null, type, "A Page", "draft", DateTime.UtcNow, DateTime.UtcNow);
        var repo = new HttpGccRepository(
            new HttpClient(new Repository(artifact, EnvelopeWithSummary("A Page Title", "A hero summary & more."))) { BaseAddress = new Uri("http://repo.test/") },
            NullLogger<HttpGccRepository>.Instance);
        var export = new GccArtifactExportService(
            repo, Options.Create(new CompanyProfileOptions()), NullLogger<GccArtifactExportService>.Instance);

        var page = Assert.Single(await export.ExportProjectAsync(ProjectId, CancellationToken.None));

        var h1 = page.Content.IndexOf("<h1>A Page Title</h1>", StringComparison.Ordinal);
        var hero = page.Content.IndexOf("<p class=\"hero-summary\">A hero summary &amp; more.</p>", StringComparison.Ordinal);
        var lede = page.Content.IndexOf("An opening.", StringComparison.Ordinal);
        Assert.True(h1 >= 0, "no h1");
        Assert.True(hero > h1, "the summary is not directly after the h1");
        Assert.True(lede > hero, "the lede does not follow the summary");
    }

    [Fact]
    public async Task A_page_with_no_summary_writes_no_empty_hero_paragraph()
    {
        var artifact = new GccArtifactDto(
            Guid.NewGuid(), Guid.NewGuid(), ProjectId, null, "pillar", "A Page", "draft", DateTime.UtcNow, DateTime.UtcNow);
        var repo = new HttpGccRepository(
            new HttpClient(new Repository(artifact, EnvelopeWithSummary("A Page Title", summary: null))) { BaseAddress = new Uri("http://repo.test/") },
            NullLogger<HttpGccRepository>.Instance);
        var export = new GccArtifactExportService(
            repo, Options.Create(new CompanyProfileOptions()), NullLogger<GccArtifactExportService>.Instance);

        var page = Assert.Single(await export.ExportProjectAsync(ProjectId, CancellationToken.None));

        Assert.Contains("<h1>A Page Title</h1>", page.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("hero-summary", page.Content, StringComparison.Ordinal);
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
