using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services;
using GeekAPI.Services.Workflow.Services.PromptBuilders;
using GeekAPI.Services.Workflow.Services.SchemaBuilders;
using GeekApplication.Interfaces.ContentWriterV3;
using GeekApplication.Models.ContentCreator;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// A pillar or blog names and links the project's declared partners, and no other tool.
/// </summary>
/// <remarks>
/// Jeff, 2026-10-05, on the Accounts Payable pillar: "links or anchor tags to Partners not listed" --
/// Melio and Plooto, on a project whose partners are Lightyear, Ramp, Bill, Stampli and ApprovalMax.
/// They came from the list of tools the publisher's own site links under the keyword, which was handed
/// to the writer with "name these and link each one", at a hand-built path no tool page is published
/// under.
/// </remarks>
public sealed class GccPartnerOnlyToolsTests
{
    private const string ToolBase = "https://geekatyourspot.com/tools";

    private static GccCreateDto Create(Guid? projectId = null, Guid? siteRun = null) => new(
        Id: Guid.NewGuid(), ClientId: Guid.NewGuid(), OwnerUserId: Guid.NewGuid(),
        StartingContentType: "pillar", Topic: "Automated Accounts Payable",
        Notes: null, ProjectSiteRunId: siteRun, SiteSectionJson: null, BriefJson: null,
        ResearchJson: null, Status: "draft", CreatedAtUtc: DateTime.UtcNow, UpdatedAtUtc: DateTime.UtcNow,
        ProjectId: projectId);

    [Fact]
    public void A_partners_tool_page_is_at_the_path_the_page_itself_is_published_under()
    {
        var create = Create() with { Topic = "Accounts Payable: Automated Approval Workflows" };

        var pages = GccPartnerToolPages.For(
            create, ["https://www.ramp.com", "https://lightyear.cloud/pricing", "https://ramp.com/bill-pay"], ToolBase);

        // One per partner, in the operator's order; the same host twice is one partner.
        Assert.Equal(["Ramp", "Lightyear"], pages.Select(p => p.ProductName));
        Assert.All(pages, page =>
        {
            // GenerateToolPageAsync publishes the page at GccContentPath.For(tool base, create, slug).
            Assert.Equal(GccGenerateService.ToolSlug(page.ProductName), page.Slug);
            Assert.Equal(new Uri(GccContentPath.For(ToolBase, create, page.Slug)).AbsolutePath, page.Path);
        });
        Assert.Equal("/tools/marketing/accounts-payable/ramp", pages[0].Path);
    }

    [Fact]
    public void The_writer_is_given_the_path_it_was_handed_not_one_assembled_from_the_name()
    {
        var context = new ProjectGenerationContext(
            ProjectName: "Acme", ProjectUrl: "https://acme.test", TargetKeyword: "accounts payable automation",
            Department: "marketing", SiteName: "Acme", DetectedTone: string.Empty, DetectedFocus: string.Empty,
            CrawledHeadings: [], CrawledParagraphs: [], JsonLdStructuredSummary: null, KeywordSources: [],
            PeopleAlsoAskQuestions: [], PublisherName: "Geek", PublisherLogoUrl: "https://geek.test/logo.png",
            AuthorName: "Author", ArticleBaseUrl: "https://geek.test/articles", BlogBaseUrl: "https://geek.test/blog",
            ToolBaseUrl: "https://geek.test/tools", ImplementerPositioning: "an AI implementation partner",
            Provider: LlmProviderType.OpenAi,
            KnownCrawlTools: [new KnownCrawlTool("Ramp", Href: null, PublicPath: "/tools/accounting/accounts-payable/ramp")]);

        var prompt = string.Join("\n", new ContentPromptBuilder()
            .BuildStandaloneBlogBodyPrompt(context, new BlogMetadataDraft("Title", "Meta", ["ai"], ["Overview"]))
            .Messages.Select(m => m.Content));

        Assert.Contains("- Ramp — public path: /tools/accounting/accounts-payable/ramp", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("/tools/marketing/ramp", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void The_writer_is_told_which_tools_not_to_name()
    {
        var instruction = GccRequiredToolMentions.UnlistedInstruction(["Melio", "Plooto"])!;

        Assert.StartsWith("TOOLS THIS PIECE DOES NOT NAME: Melio, Plooto.", instruction);
        Assert.Null(GccRequiredToolMentions.UnlistedInstruction([]));
    }

    /// <summary>
    /// The real pillar path. The site links Melio, Dext and Lightyear under the keyword; the project's
    /// partners are Lightyear and Ramp. The writer is handed Lightyear and Ramp to name and link, and
    /// Melio and Dext as names to leave out.
    /// </summary>
    [Fact]
    public async Task A_pillar_is_handed_the_projects_partners_to_link_and_the_sites_other_tools_to_leave_out()
    {
        var project = Project("https://lightyear.cloud", "https://ramp.com");
        var create = Create(project.Id, GccKnownToolsResolverTests.RunId);
        var provider = new CapturingProvider();

        await Build(provider, project).GeneratePillarBodyAsync(
            create, null, ContentGeneratorProvider.OpenAi, null, CancellationToken.None);

        var prompt = provider.FirstBodyPrompt;
        Assert.Contains("- Lightyear — public path: /tools/marketing/lightyear", prompt, StringComparison.Ordinal);
        Assert.Contains("- Ramp — public path: /tools/marketing/ramp", prompt, StringComparison.Ordinal);
        Assert.Contains("TOOLS THIS PIECE DOES NOT NAME: Melio, Dext.", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("- Melio — public path", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("- Dext — public path", prompt, StringComparison.Ordinal);
    }

    private static GccProjectDto Project(params string[] partnerUrls) => new(
        Id: Guid.NewGuid(), ClientId: Guid.NewGuid(), Name: "Acme", Code: null, Description: null,
        Status: "active", SiteUrl: "https://geek.test", ProjectSiteRunId: GccKnownToolsResolverTests.RunId,
        Department: "marketing", PartnerUrls: partnerUrls, CompetitorUrls: [],
        StartDate: new DateOnly(2026, 1, 1), DueDate: null, FinishedDate: null, EstimatedHours: null,
        Budget: null, BudgetCurrency: null, CreatedAtUtc: DateTime.UtcNow, UpdatedAtUtc: DateTime.UtcNow);

    private const string LedeJson =
        """{"lede":{"ledeType":"summary","heading":"Lede","paragraphs":[{"type":"text","runs":[{"text":"Body."}]}]},"introduction":{"tag":"h2","heading":"Lede","paragraphs":[{"type":"text","runs":[{"text":"Body."}]},{"type":"text","runs":[{"text":"Book a free consultation.","href":"#consultationAppointment2xl"}]}],"href":null,"children":[]}}""";
    private const string MetadataJson =
        """{"title":"A Title","summary":"A standfirst.","metaDescription":"A meta description.","keywords":["k"],"sectionOutline":["A"]}""";

    private sealed class CapturingProvider : IContentGenerationProvider
    {
        private int bodyCalls;

        public LlmProviderType ProviderType => LlmProviderType.OpenAi;
        public List<ChatCompletionRequest> Requests { get; } = [];

        public Task<ChatCompletionResult> CompleteAsync(
            ChatCompletionRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var asked = string.Join("\n", request.Messages.Select(m => m.Content));
            var content = request.JsonSchemaName == "sections"
                ? ScriptedBody.PlannedBatch(bodyCalls++)
                : asked.Contains("ledeType", StringComparison.OrdinalIgnoreCase) ? LedeJson
                : asked.Contains("image-generation prompts", StringComparison.OrdinalIgnoreCase)
                    ? ScriptedBody.ImagePrompts()
                : MetadataJson;
            return Task.FromResult(new ChatCompletionResult(content, "test-model", null, null));
        }

        public string FirstBodyPrompt =>
            string.Join("\n", Requests.First(r => r.JsonSchemaName == "sections").Messages.Select(m => m.Content));
    }

    private sealed class FakeProviderFactory(IContentGenerationProvider provider) : IContentProviderFactory
    {
        public IContentGenerationProvider Get(LlmProviderType providerType) => provider;
        public IContentGenerationProvider GetDefault() => provider;
    }

    private static GccGenerateService Build(IContentGenerationProvider provider, GccProjectDto project)
    {
        var projects = new GccCompetitorAnalysisResolverTests.FakeProjects(project);
        var sitePages = new GccCompetitorAnalysisResolverTests.FakePages([GccKnownToolsResolverTests.UseCasePage()]);
        return new GccGenerateService(
            new ContentPromptBuilder(),
            TestContentTypePrompts.Registry(),
            new FakeProviderFactory(provider),
            new SoftwareApplicationSchemaBuilder(),
            new BlogPostingSchemaBuilder(),
            new ArticleSchemaBuilder(new SoftwareApplicationSchemaBuilder()),
            Options.Create(new CompanyProfileOptions()),
            NullLogger<GccGenerateService>.Instance,
            GccCompetitorAnalysisResolverTests.Build(
                projects, new GccCompetitorAnalysisResolverTests.FakePages(), new GccCompetitorAnalysisResolverTests.FakeRag()),
            GccPartnerExtractionFakes.NeverInvoked(new FakeProviderFactory(provider)),
            projects,
            new GccPublisherProfileResolver(
                projects, new GccCompetitorAnalysisResolverTests.FakePages(), NullLogger<GccPublisherProfileResolver>.Instance),
            new GccKnownToolsResolver(sitePages, NullLogger<GccKnownToolsResolver>.Instance),
            new GccToolPageFanOutFixture.FakeExtractionBank());
    }
}
