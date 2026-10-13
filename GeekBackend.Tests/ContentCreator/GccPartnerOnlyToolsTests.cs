using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.ContentCreator.Guardrail;
using GeekAPI.Services.Workflow.Domain.Entities;
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
/// A pillar or blog names the project's declared partners and no other tool, and each partner's name
/// is linked to the path its own tool page is published under.
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
    public void A_tools_name_is_linked_to_the_path_it_was_handed_and_the_writer_is_shown_no_path()
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

        // The writer is shown the name. The path is not its to write: neither the real one nor one
        // assembled from the department and the name.
        Assert.Contains("\n- Ramp", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("/tools/accounting/accounts-payable/ramp", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("/tools/marketing/ramp", prompt, StringComparison.Ordinal);

        // The code links the name to the path the tool was handed with.
        var body = new Section(
            "h2", "Where the hours go",
            [new TextParagraph([new Run("Ramp codes the spend on capture.")])], null, []);
        var linked = GccToolLinker.Link([body], context.KnownCrawlTools!);

        var link = Assert.Single(linked.Links);
        Assert.Equal("/tools/accounting/accounts-payable/ramp", link.Href);
        Assert.Equal("Ramp", link.Words);
    }

    /// <summary>
    /// The real pillar path. The site links Melio, Dext and Lightyear under the keyword; the project's
    /// partners are Lightyear and Ramp. The writer is handed Lightyear and Ramp to name, and is told of
    /// no tool it must leave out.
    /// </summary>
    [Fact]
    public async Task A_pillar_is_handed_the_projects_partners_to_name_and_is_told_of_no_tool_to_leave_out()
    {
        var project = Project("https://lightyear.cloud", "https://ramp.com");
        var create = Create(project.Id, GccKnownToolsResolverTests.RunId);
        var provider = new CapturingProvider();

        await Build(provider, project).GeneratePillarBodyAsync(
            create, null, ContentGeneratorProvider.OpenAi, null, CancellationToken.None);

        var prompt = provider.FirstBodyPrompt;
        // The tools block lists the partners by name, in the order the project declares them, and no
        // other tool. No path is printed: GccToolLinker holds it.
        var tools = GeekBackend.Tests.Workflow.PromptBuilders.KnownToolsBlockTests.BlockOf(prompt)
            .Where(line => line.StartsWith("- ", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(["- Lightyear", "- Ramp"], tools);
        AssertNoRequestNamesAToolToLeaveOut(provider);
        Assert.DoesNotContain("public path", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("/tools/marketing/lightyear", prompt, StringComparison.Ordinal);
    }

    /// <summary>
    /// The whole write step, end to end: the writer's reply is plain text that names the partners, and
    /// the saved page carries each partner's name linked once to its own tool page -- placed by code,
    /// recorded on the run, and passed by the same guard that checks every link on the page.
    /// </summary>
    [Fact]
    public async Task A_pillar_whose_writer_only_names_the_partners_is_saved_with_each_name_linked_to_its_tool_page()
    {
        var project = Project("https://lightyear.cloud", "https://ramp.com");
        var create = Create(project.Id, GccKnownToolsResolverTests.RunId);
        var provider = new CapturingProvider(call => call == 0
            ? """
              {"sections":[{"tag":"h2","heading":"Where the hours go","paragraphs":[{"type":"text","runs":[{"text":"Ramp codes the spend on capture, and Lightyear routes each invoice for approval. Ramp then syncs it to the ledger."}]}],"href":null,"children":[],"provenance":"plan"}]}
              """
            : ScriptedBody.PlannedBatch(call));
        var written = new List<GccGenerateJobEventWrite>();
        GccRunLog.Begin(Guid.NewGuid(), (events, _) => { written.AddRange(events); return Task.CompletedTask; }, NullLogger.Instance);

        var envelope = await Build(provider, project).GeneratePillarBodyAsync(
            create, null, ContentGeneratorProvider.OpenAi, null, CancellationToken.None);

        // Every linked run on the saved page that leads to a tool page: the name, and nothing around it.
        using var saved = System.Text.Json.JsonDocument.Parse(envelope);
        var linked = LinkedRuns(saved.RootElement.GetProperty("body"))
            .Where(run => run.Href.StartsWith("/tools/", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(
            [("Ramp", "/tools/marketing/ramp"), ("Lightyear", "/tools/marketing/lightyear")],
            linked);

        // What was linked is on the run's record, with nothing left unlinked.
        var links = Assert.Single(written, w => w.Kind == "links");
        using var record = System.Text.Json.JsonDocument.Parse(links.PayloadJson);
        Assert.Equal(
            ["Lightyear", "Ramp"],
            record.RootElement.GetProperty("links").EnumerateArray().Select(l => l.GetProperty("tool").GetString()).Order());
        Assert.Equal(0, record.RootElement.GetProperty("notLinked").GetArrayLength());

        // The guard read those links and refused nothing: the page was written, and neither link check fired.
        var verdict = Assert.Single(written, w => w.Kind == "verdict");
        using var judged = System.Text.Json.JsonDocument.Parse(verdict.PayloadJson);
        var checks = judged.RootElement.GetProperty("findings").EnumerateArray()
            .Select(f => f.GetProperty("check").GetString())
            .ToList();
        Assert.DoesNotContain("links", checks);
        Assert.DoesNotContain("link-text", checks);
        Assert.DoesNotContain("partner-mentions", checks);
    }

    /// <summary>
    /// Which of the site's tools are kept out of the site's own blocks a piece reads: not a partner,
    /// and named by neither the Brief nor a partner's own pages.
    /// </summary>
    [Fact]
    public void A_tool_the_brief_or_a_partners_own_pages_name_is_not_kept_out_of_what_a_piece_reads()
    {
        var (keptOut, shown) = GccRequiredToolMentions.Unlisted(
            siteTools: ["Stripe", "Avalara", "Sovos", "Lightyear", "Bill", "stripe"],
            required: ["Lightyear", "Ramp"],
            briefText: "Fragmented billing: several subscription engines (like Stripe, PayPal, and Chargebee). A bill arrives late.",
            partnerText: "Quaderno integrates directly with Stripe. Moving from Avalara takes an afternoon.");

        // A partner is neither. "a bill arrives" does not name Bill. A name is listed once.
        Assert.Equal(["Sovos", "Bill"], keptOut);
        // The Brief is named first as where: it is the operator's own statement.
        Assert.Equal(
            [("Stripe", GccRequiredToolMentions.NamedInTheBrief), ("Avalara", GccRequiredToolMentions.NamedOnPartnerPages)],
            shown);
    }

    /// <summary>
    /// The real pillar path, as the 17:47 UTC run of 2026-10-13 met it: the site lists a tool under
    /// the keyword that is not a partner, and the writer names it. The page is saved, whether the
    /// Brief names the tool, a partner's page names it, or nothing the writer was handed does. No
    /// request tells the writer to leave a tool out, and no check lists the name.
    /// </summary>
    [Theory]
    [InlineData("Melio holds part of the payment data.")]
    [InlineData("Lightyear connects to Stampli.")]
    [InlineData("Dext reads the receipt.")]
    public async Task A_pillar_that_names_a_tool_the_project_does_not_list_is_saved(string sentence)
    {
        var project = Project("https://lightyear.cloud", "https://ramp.com");
        var create = Create(project.Id, GccKnownToolsResolverTests.RunId) with
        {
            BriefJson = """{"nicheFraming":{"coreProblem":"Payment data sits in several tools (like Melio and PayPal), and nobody reconciles them."}}""",
            ResearchJson = Research(
                partnerPassage: "Lightyear connects natively to Stampli and syncs every payment.",
                competitorPassage: "Dext reads the receipt and codes it."),
        };
        var provider = new CapturingProvider(call => call == 0 ? NamingReply(sentence) : ScriptedBody.PlannedBatch(call));
        var written = new List<GccGenerateJobEventWrite>();
        GccRunLog.Begin(Guid.NewGuid(), (events, _) => { written.AddRange(events); return Task.CompletedTask; }, NullLogger.Instance);

        var envelope = await Build(provider, project).GeneratePillarBodyAsync(
            create, null, ContentGeneratorProvider.OpenAi, null, CancellationToken.None);

        Assert.Contains(sentence, envelope, StringComparison.Ordinal);
        AssertNoRequestNamesAToolToLeaveOut(provider);
        var verdict = Assert.Single(written, w => w.Kind == "verdict");
        Assert.DoesNotContain("does not list as a partner", verdict.PayloadJson, StringComparison.Ordinal);
    }

    /// <summary>
    /// What is kept out of the site's own blocks is on the run's record, with the tools shown after all
    /// and where each was named: the Brief names Melio here, and nothing names Dext.
    /// </summary>
    [Fact]
    public async Task The_sites_tools_kept_out_of_its_own_blocks_are_on_the_runs_record()
    {
        var project = Project("https://lightyear.cloud", "https://ramp.com");
        var create = Create(project.Id, GccKnownToolsResolverTests.RunId) with
        {
            BriefJson = """{"nicheFraming":{"coreProblem":"Payment data sits in several tools (like Melio and PayPal), and nobody reconciles them."}}""",
        };
        var written = new List<GccGenerateJobEventWrite>();
        GccRunLog.Begin(Guid.NewGuid(), (events, _) => { written.AddRange(events); return Task.CompletedTask; }, NullLogger.Instance);

        await Build(new CapturingProvider(), project).GeneratePillarBodyAsync(
            create, null, ContentGeneratorProvider.OpenAi, null, CancellationToken.None);

        var record = Assert.Single(written, w => w.Kind == "site-tools");
        using var payload = System.Text.Json.JsonDocument.Parse(record.PayloadJson);
        Assert.Equal(
            ["Dext"],
            payload.RootElement.GetProperty("keptOutOfTheSitesOwnBlocks").EnumerateArray().Select(n => n.GetString()));
        var kept = Assert.Single(payload.RootElement.GetProperty("shown").EnumerateArray());
        Assert.Equal("Melio", kept.GetProperty("name").GetString());
        Assert.Equal(GccRequiredToolMentions.NamedInTheBrief, kept.GetProperty("namedIn").GetString());
    }

    /// <summary>No request of a run tells the writer of a tool it must not name.</summary>
    private static void AssertNoRequestNamesAToolToLeaveOut(CapturingProvider provider) =>
        Assert.All(provider.Requests, request =>
        {
            var asked = string.Join("\n", request.Messages.Select(m => m.Content));
            Assert.DoesNotContain("DOES NOT NAME", asked, StringComparison.Ordinal);
            Assert.DoesNotContain("Do not name them", asked, StringComparison.Ordinal);
        });

    /// <summary>A first body reply that names both partners, as a pillar must, and then says <paramref name="sentence"/>.</summary>
    private static string NamingReply(string sentence) =>
        $$"""{"sections":[{"tag":"h2","heading":"Where the hours go","paragraphs":[{"type":"text","runs":[{"text":"Ramp codes the spend on capture, and Lightyear routes each invoice for approval. {{sentence}}"}]}],"href":null,"children":[],"provenance":"plan"}]}""";

    /// <summary>A page of Lightyear's own, and when asked a competitor's page beside it.</summary>
    private static string Research(string partnerPassage, string? competitorPassage = null) =>
        GccResearchFetchService.Serialize(new GccResearchDocument(SerpIndex: null, Quoteables: []) with
        {
            Quoteables =
            [
                new GccQuoteablePage(
                    "https://lightyear.cloud/integrations", "Lightyear integrations", [], [partnerPassage],
                    RetrievalMode: GccQuoteablePage.RetrievalModeRagChunk),
            ],
            CompetitorQuoteables = competitorPassage is null
                ? []
                :
                [
                    new GccQuoteablePage(
                        "https://contender.test/compare", "Other vendor compared", [], [competitorPassage],
                        RetrievalMode: GccQuoteablePage.RetrievalModeRagChunk),
                ],
        });

    /// <summary>
    /// The whole pillar write step with ten body sections: five body calls, each on the run's record with
    /// its number, the total and the floor it was held to, and each after the first told what the page
    /// already says.
    /// </summary>
    [Fact]
    public async Task A_pillar_is_written_in_five_calls_each_recorded_and_each_later_one_told_what_came_before()
    {
        var project = Project("https://lightyear.cloud", "https://ramp.com");
        var create = Create(project.Id, GccKnownToolsResolverTests.RunId);
        var provider = new CapturingProvider();
        var written = new List<GccGenerateJobEventWrite>();
        GccRunLog.Begin(Guid.NewGuid(), (events, _) => { written.AddRange(events); return Task.CompletedTask; }, NullLogger.Instance);

        await Build(provider, project).GeneratePillarBodyAsync(
            create, null, ContentGeneratorProvider.OpenAi, null, CancellationToken.None);

        var batches = new List<(int Call, int Of, int Floor)>();
        foreach (var batch in written.Where(w => w.Kind == "batch"))
        {
            using var payload = System.Text.Json.JsonDocument.Parse(batch.PayloadJson);
            var root = payload.RootElement;
            batches.Add((root.GetProperty("call").GetInt32(), root.GetProperty("of").GetInt32(), root.GetProperty("floor").GetInt32()));
        }

        Assert.Equal([(1, 5, 600), (2, 5, 600), (3, 5, 600), (4, 5, 600), (5, 5, 600)], batches);

        var bodyPrompts = provider.Requests
            .Where(r => r.JsonSchemaName == "sections")
            .Select(r => string.Join("\n", r.Messages.Select(m => m.Content)))
            .ToList();
        Assert.Equal(5, bodyPrompts.Count);
        Assert.DoesNotContain("ALREADY WRITTEN ON THIS PAGE", bodyPrompts[0], StringComparison.Ordinal);
        Assert.All(bodyPrompts.Skip(1), prompt => Assert.Contains("ALREADY WRITTEN ON THIS PAGE", prompt, StringComparison.Ordinal));
        // The scripted model answers each call with one section; the fifth call is shown all four before it.
        Assert.Contains("- Planned section 1 -- Body.", bodyPrompts[1], StringComparison.Ordinal);
        Assert.All(
            Enumerable.Range(1, 4),
            n => Assert.Contains($"- Planned section {n} -- Body.", bodyPrompts[4], StringComparison.Ordinal));
        Assert.DoesNotContain("- Planned section 5", bodyPrompts[4], StringComparison.Ordinal);
    }

    private static IEnumerable<(string Text, string Href)> LinkedRuns(System.Text.Json.JsonElement node)
    {
        switch (node.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Object:
                if (node.TryGetProperty("text", out var text) && text.ValueKind == System.Text.Json.JsonValueKind.String
                    && node.TryGetProperty("href", out var href) && href.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    yield return (text.GetString()!, href.GetString()!);
                }

                foreach (var property in node.EnumerateObject())
                {
                    foreach (var hit in LinkedRuns(property.Value)) yield return hit;
                }

                break;

            case System.Text.Json.JsonValueKind.Array:
                foreach (var item in node.EnumerateArray())
                {
                    foreach (var hit in LinkedRuns(item)) yield return hit;
                }

                break;
        }
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

    private sealed class CapturingProvider(Func<int, string>? body = null) : IContentGenerationProvider
    {
        private int bodyCalls;

        public LlmProviderType ProviderType => LlmProviderType.OpenAi;

        public string ModelFor(ChatCompletionRequest request) => "gpt-4.1";
        public List<ChatCompletionRequest> Requests { get; } = [];

        public Task<ChatCompletionResult> CompleteAsync(
            ChatCompletionRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var asked = string.Join("\n", request.Messages.Select(m => m.Content));
            var content = request.JsonSchemaName == "sections"
                ? (body ?? ScriptedBody.PlannedBatch)(bodyCalls++)
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
