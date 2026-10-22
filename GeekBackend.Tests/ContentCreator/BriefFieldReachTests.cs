using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.ContentCreator.Partner;
using GeekAPI.Services.GeekCrawler;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services;
using GeekAPI.Services.Workflow.Services.PromptBuilders;
using GeekAPI.Services.Workflow.Services.SchemaBuilders;
using GeekApplication.Interfaces.ContentWriterV3;
using GeekApplication.Models.ContentCreator;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// Which brief field reaches which call (Jeff, 2026-10-10: "Incorporate and use everything from Brief").
/// </summary>
/// <remarks>
/// <para>
/// One field of a complete brief is changed at a time and each page is written again through its real
/// generate path, with a provider that records what it is sent and always answers the same. A call whose
/// prompt differs between the two runs is a call that field reaches; a call whose prompt is the same to
/// the character is one it does not. The saved page is compared the same way, which shows what code puts
/// on the page from the brief without a model.
/// </para>
/// <para>
/// A changed value is used, not a search for the field's text, because a choice field never arrives as
/// typed: the angle reaches the writer as a description and a list of opening types, and a search for
/// "problem_solution" would call that field unused.
/// </para>
/// </remarks>
public sealed class BriefFieldReachTests(ITestOutputHelper output)
{
    internal const string Host = "partner.test";
    private const string ToolFaqA = "Does Partner Widget sync with QuickBooks Online?";
    private const string ToolFaqB = "Does Partner Widget post to Xero?";

    /// <summary>The rows of one FAQ box, as the form saves them: each question with the answer entered for it.</summary>
    private static JsonArray Faq(params (string Question, string Answer)[] rows) =>
        new([.. rows.Select(row => (JsonNode)new JsonObject { ["question"] = row.Question, ["answer"] = row.Answer })]);

    /// <summary>The first row of an FAQ box.</summary>
    private static JsonObject FirstRow(JsonNode? box) => (JsonObject)((JsonArray)box!)[0]!;

    /// <summary>Every field the brief form saves, each with a value.</summary>
    internal const string CompleteBrief = """
        {
          "briefVersion": 2,
          "primaryIntent": "informational",
          "secondaryIntent": "local",
          "buyingStage": "awareness",
          "audienceSegment": "affinity",
          "audienceNotes": "Controllers at regional distributors who close the books by hand.",
          "angle": "problem_solution",
          "toneOfVoice": "consultant_professional",
          "eeatSignals": ["first_hand_experience", "expertise", "authoritativeness", "trustworthiness"],
          "lengthBand": "",
          "paaQuestions": [
            { "question": "What does month end cost a distributor?", "answer": "The controller's first week of every month goes to it." },
            { "question": "Who owns the close calendar?", "answer": "The controller owns it, and each reconciliation has one named preparer." }
          ],
          "blogFaqQuestions": [
            { "question": "How long does a close take by hand?", "answer": "Most of a working week once the period has ended." }
          ],
          "nicheFraming": {
            "taxonomyPath": "Accounting -> Month End Close -> Reconciliation",
            "diagnosisQuestions": "How many accounts are reconciled by hand each month?",
            "coreProblem": "The close runs on spreadsheets nobody else can read.",
            "painPoints": "Reconciliations start after the period ends.\n\nNobody knows which version of the workbook is current.",
            "automationToPitch": "A close calendar that assigns, chases and signs off each reconciliation.",
            "evidence": [
              { "problem": "Reconciliations start after the period ends.", "solution": "Continuous matching of bank lines to ledger entries.", "terms": ["continuous matching", "bank feed"] }
            ],
            "faqQuestions": "Is a close calendar worth it for a small team?",
            "perTool": {
              "partner.test": {
                "coreProblem": "Setup takes a consultant a fortnight.",
                "painPoints": "The vendor master has to be mapped by hand.",
                "automationToPitch": "A setup wizard that maps the vendor master on import.",
                "evidence": [
                  { "problem": "The vendor master has to be mapped by hand.", "solution": "Automated setup wizard maps the vendor master.", "terms": ["setup wizard", "vendor master"] }
                ],
                "faqQuestions": [
                  { "question": "Does Partner Widget sync with QuickBooks Online?", "answer": "Every Partner Widget payment is synced to QuickBooks Online as it is made." }
                ]
              }
            }
          }
        }
        """;

    private sealed record Field(string Name, Action<JsonObject> Change);

    private static JsonObject Framing(JsonObject brief) => (JsonObject)brief["nicheFraming"]!;

    private static JsonObject Tool(JsonObject brief) => (JsonObject)((JsonObject)Framing(brief)["perTool"]!)[Host]!;

    private static JsonObject Row(JsonObject set) => (JsonObject)((JsonArray)set["evidence"]!)[0]!;

    private static readonly IReadOnlyList<Field> Fields =
    [
        new("primaryIntent", b => b["primaryIntent"] = "transactional"),
        new("secondaryIntent", b => b["secondaryIntent"] = "comparison"),
        new("buyingStage", b => b["buyingStage"] = "action"),
        new("audienceSegment", b => b["audienceSegment"] = "in_market"),
        new("audienceNotes", b => b["audienceNotes"] = "Plant managers who approve every purchase order themselves."),
        new("angle", b => b["angle"] = "comparative"),
        new("toneOfVoice", b => b["toneOfVoice"] = "commercial_balanced"),
        new("eeatSignals", b => b["eeatSignals"] = new JsonArray("expertise")),
        new("lengthBand", b => b["lengthBand"] = "long"),
        new("paaQuestions", b => b["paaQuestions"] = Faq(
            ("What does a late close cost a wholesaler?", "A late close delays every report that depends on it."),
            ("Who signs off the reconciliations?", "The controller signs each one off."))),
        new("paaQuestions, an answer alone", b => FirstRow(b["paaQuestions"])["answer"] = "It takes the finance team away from everything else for days."),
        new("blogFaqQuestions", b => b["blogFaqQuestions"] = Faq(("How many people does a close take?", "Usually the whole finance team."))),
        new("blogFaqQuestions, an answer alone", b => FirstRow(b["blogFaqQuestions"])["answer"] = "Long enough that the numbers are stale when they arrive."),
        new("briefVersion", b => b["briefVersion"] = 1),
        new("nicheFraming.taxonomyPath, first level", b => Framing(b)["taxonomyPath"] = "Sales -> Month End Close -> Reconciliation"),
        new("nicheFraming.taxonomyPath, later levels", b => Framing(b)["taxonomyPath"] = "Accounting -> Payroll -> Timesheets"),
        new("nicheFraming.diagnosisQuestions", b => Framing(b)["diagnosisQuestions"] = "Who approves a journal entry today?"),
        new("nicheFraming.coreProblem", b => Framing(b)["coreProblem"] = "Approvals wait in one person's inbox."),
        new("nicheFraming.painPoints", b => Framing(b)["painPoints"] = "Approvers are found by asking around.\n\nNothing records who signed what."),
        new("nicheFraming.automationToPitch", b => Framing(b)["automationToPitch"] = "Routing rules that send each entry to its approver and record the sign-off."),
        new("nicheFraming.evidence.problem", b => Row(Framing(b))["problem"] = "Approvers are found by asking around."),
        new("nicheFraming.evidence.solution", b => Row(Framing(b))["solution"] = "Rule-based approval routing with an audit trail."),
        new("nicheFraming.evidence.terms", b => Row(Framing(b))["terms"] = new JsonArray("approval routing", "audit trail")),
        new("nicheFraming.faqQuestions", b => Framing(b)["faqQuestions"] = "Does a close calendar need an accountant to run it?"),
        new("perTool.coreProblem", b => Tool(b)["coreProblem"] = "Go-live slips because the chart of accounts is wrong."),
        new("perTool.painPoints", b => Tool(b)["painPoints"] = "The chart of accounts is rebuilt by hand."),
        new("perTool.automationToPitch", b => Tool(b)["automationToPitch"] = "An importer that rebuilds the chart of accounts from the old ledger."),
        new("perTool.evidence.problem", b => Row(Tool(b))["problem"] = "The chart of accounts is rebuilt by hand."),
        new("perTool.evidence.solution", b => Row(Tool(b))["solution"] = "Ledger importer rebuilds the chart of accounts."),
        new("perTool.evidence.terms", b => Row(Tool(b))["terms"] = new JsonArray("ledger importer", "chart of accounts")),
        new("perTool.faqQuestions", b => Tool(b)["faqQuestions"] = Faq((ToolFaqB, "Partner Widget posts each bill to Xero."))),
        new("perTool.faqQuestions, an answer alone", b => FirstRow(Tool(b)["faqQuestions"])["answer"] = "Yes, and a failed sync is retried overnight."),
    ];

    /// <summary>The brief with the tool's own section taken out, so the tool has the category's alone.</summary>
    private static string WithoutToolFraming(string briefJson)
    {
        var brief = (JsonObject)JsonNode.Parse(briefJson)!;
        ((JsonObject)Framing(brief)["perTool"]!).Remove(Host);
        return brief.ToJsonString();
    }

    /// <summary>
    /// The brief as the operator's research is entered since 2026-10-11: every failure under a partner
    /// and none for the category.
    /// </summary>
    private static string WithEveryFailureUnderAPartner(string briefJson)
    {
        var brief = (JsonObject)JsonNode.Parse(briefJson)!;
        Framing(brief)["painPoints"] = "";
        Framing(brief)["evidence"] = new JsonArray();
        return brief.ToJsonString();
    }

    private static string Changed(string briefJson, Field field)
    {
        var brief = (JsonObject)JsonNode.Parse(briefJson)!;
        field.Change(brief);
        return brief.ToJsonString();
    }

    // ---- the provider -------------------------------------------------------------------------------

    private const string Sentence = "This sentence stands in for a paragraph of body text.";

    private const string LedeAndIntroJson =
        """{"lede":{"ledeType":"summary","heading":"A","paragraphs":[{"type":"text","runs":[{"text":"Body."}]}]},"introduction":{"tag":"h2","heading":"A","paragraphs":[{"type":"text","runs":[{"text":"Body."}]}],"href":null,"children":[]}}""";

    private const string LedeJson =
        """{"ledeType":"directAddress","heading":"Reclaiming The Hours You Lose","paragraphs":[{"type":"text","runs":[{"text":"A hook paragraph \u2014 one that opens the page."}]}]}""";

    private const string MetadataJson =
        """{"title":"A Title","summary":"A standfirst \u2014 in a sentence.","metaDescription":"A meta description.","keywords":["k"],"sectionOutline":["A"]}""";

    private const string ToolMetadataJson =
        """{"departmentListExcerpt":"x","summary":"x \u2014 y","mainSummary":"x","heroSummary":"x","homeSummary":"x","blogSummary":"x","toolPageExcerpt":"x","advertisingSummary":"x","metaDescription":"x"}""";

    internal const string DeckCoverAndPostJson =
        """{"cover":{"title":"The close that runs on one spreadsheet","line":"What a hand-run close costs \u2014 and what replaces it."},"post":"Most controllers lose the first week of the month to the close \u2014 and nobody budgets for it.\n\nThe slides walk through where the hours go and what takes them back."}""";

    private const string FirstBatchJson =
        """{"sections":[{"tag":"h2","heading":"A","paragraphs":[{"type":"text","runs":[{"text":"Body \u2014 written."}]}],"href":null,"children":[],"provenance":"plan"}]}""";

    private static readonly string[] ToolBodyBatches =
    [
        """{"sections":[{"tag":"h2","heading":"Where the setup hours actually go","paragraphs":[{"type":"text","runs":[{"text":"Partner Widget removes the manual pass."}]}],"href":null,"children":[]},{"tag":"h2","heading":"What the wizard takes off your desk","paragraphs":[{"type":"text","runs":[{"text":"Partner Widget captures the invoice on arrival."}]}],"href":null,"children":[]}]}""",
        """{"sections":[{"tag":"h2","heading":"Mapping your data before go-live","paragraphs":[{"type":"text","runs":[{"text":"Partner Widget needs the vendor master mapped first."}]},{"type":"quote","runs":[{"text":"reduces setup time by half"}],"cite":"https://partner.test/widget"}],"href":null,"children":[]},{"tag":"h2","heading":"Judging Partner Widget against the alternatives","paragraphs":[{"type":"text","runs":[{"text":"Partner Widget is priced per document."}]}],"href":null,"children":[]}]}""",
        """{"sections":[{"tag":"h2","heading":"Who Partner Widget suits","paragraphs":[{"type":"text","runs":[{"text":"Partner Widget fits a team already on a ledger."}]}],"href":null,"children":[]},{"tag":"h2","heading":"What to do next with Partner Widget","paragraphs":[{"type":"text","runs":[{"text":"Partner Widget rewards a scoped pilot."}]}],"href":null,"children":[]}]}""",
    ];

    /// <summary>
    /// Records every call under the name of what it writes, and answers by what was asked. The answer
    /// never depends on the brief, so two runs differ only in what the brief put into the prompts.
    /// </summary>
    /// <param name="firstBody">What the writer's first body reply is changed to, for a test of what the
    /// page does with something a writer wrote; null for the reply as scripted.</param>
    /// <param name="deckReply">What a reply to one of the deck's two requests is changed to, by the
    /// request's name here ("cover and post", "slides"); null for the replies as scripted.</param>
    internal sealed class Recorder(
        string page,
        string model = "gpt-4.1",
        Func<string, string>? firstBody = null,
        Func<string, string, string>? deckReply = null)
        : IContentGenerationProvider
    {
        private readonly List<(string Label, string Text)> calls = [];
        private int bodyCalls;

        public LlmProviderType ProviderType => LlmProviderType.OpenAi;

        public string ModelFor(ChatCompletionRequest request) => model;

        public IReadOnlyList<(string Label, string Text)> Calls
        {
            get { lock (calls) return [.. calls]; }
        }

        public Task<ChatCompletionResult> CompleteAsync(
            ChatCompletionRequest request, CancellationToken cancellationToken = default)
        {
            var asked = string.Join("\n", request.Messages.Select(m => m.Content));
            var (label, content) = Answer(request, asked);
            lock (calls)
            {
                var nth = calls.Count(c => c.Label == label || c.Label.StartsWith($"{label} #", StringComparison.Ordinal)) + 1;
                calls.Add((nth == 1 ? label : $"{label} #{nth}", asked));
            }

            return Task.FromResult(new ChatCompletionResult(content, "test-model", null, null));
        }

        private (string Label, string Content) Answer(ChatCompletionRequest request, string asked)
        {
            if (request.JsonSchemaName == GccLinkedInDocument.CoverAndPostSchemaName)
                return ("cover and post", deckReply?.Invoke("cover and post", DeckCoverAndPostJson) ?? DeckCoverAndPostJson);
            if (request.JsonSchemaName == GccLinkedInDocument.SlidesSchemaName)
            {
                var slides = DeckSlidesJson(asked);
                return ("slides", deckReply?.Invoke("slides", slides) ?? slides);
            }

            if (request.JsonSchemaName == "sections")
            {
                var n = bodyCalls++;
                var body = page == "tool"
                    ? ToolBodyBatches[Math.Min(n, ToolBodyBatches.Length - 1)]
                    : n < 1 ? FirstBatchJson : ScriptedBody.PlannedBatch(n);
                if (n == 0 && firstBody is not null) body = firstBody(body);
                return ("body call", body);
            }

            if (request.JsonSchemaName == "section")
            {
                if (page == "tool" && !asked.Contains(ContentPromptBuilder.OperatorAnswersHeading, StringComparison.Ordinal))
                    return ("FAQ from the partner's own FAQ", Faq("Frequently Asked Questions", ["Is it secure?"]));

                var heading = page == "pillar" ? "People Also Ask" : "Frequently Asked Questions";
                var label = page switch
                {
                    "pillar" => "People Also Ask",
                    "tool" => "FAQ from the operator's answers",
                    _ => "FAQ",
                };
                return (label, Faq(heading, QuestionsIn(asked)));
            }

            if (asked.Contains("image-generation prompts", StringComparison.Ordinal))
                return ("image prompts", ScriptedBody.ImagePrompts());
            if (asked.Contains("\"negativePrompt\"", StringComparison.Ordinal) || page == "image")
                return ("image prompt", """{"prompt":"A ledger on a desk.","style":"photo","negativePrompt":"text","aspectRatio":"16:9","imageModel":"m","stylePreset":"p","notes":"n"}""");
            if (page == "email")
                return ("email", """{"subject":"A subject","body":"A body \u2014 short.","ctaLabel":"Read the guide"}""");
            if (page == "social")
                return ("social post", """{"text":"A post \u2014 short."}""");
            if (asked.Contains("sectionOutline", StringComparison.Ordinal))
                return ("title and outline", MetadataJson);
            if (asked.Contains("departmentListExcerpt", StringComparison.Ordinal))
                return ("summaries", ToolMetadataJson);
            return ("opening", page == "pillar" ? LedeAndIntroJson : LedeJson);
        }

        /// <summary>
        /// One slide for each the request assigned, in its order. A partner's slide names the partner
        /// in a line, as the request asks; the first slide's first line carries an em dash.
        /// </summary>
        internal static string DeckSlidesJson(string asked)
        {
            var assigned = asked.Split('\n')
                .Select(line => System.Text.RegularExpressions.Regex.Match(line.Trim(), @"^\d+\. Cover: (?<what>.+)$"))
                .Where(match => match.Success)
                .Select(match => match.Groups["what"].Value)
                .ToList();
            return JsonSerializer.Serialize(new
            {
                slides = assigned.Select((what, i) =>
                {
                    var partner = System.Text.RegularExpressions.Regex.Match(what, "^(?<name>.+): the failure it answers");
                    return new
                    {
                        title = $"What slide {Words[i % Words.Length]} settles",
                        lines = partner.Success
                            ? new[] { $"{partner.Groups["name"].Value} takes the manual pass out of the close.", "The controller signs off once." }
                            : i == 0
                                ? new[] { "Reconciliations start late \u2014 every month.", "Nobody knows which workbook is current." }
                                : new[] { "Each reconciliation is assigned, chased and signed off.", "The close stops waiting on one inbox." },
                    };
                }),
            });
        }

        private static readonly string[] Words = ["one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten"];

        private static IReadOnlyList<string> QuestionsIn(string asked) =>
        [
            .. asked.Split('\n')
                .Select(l => l.Trim())
                .Where(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"^Q\d+: "))
                .Select(l => l[(l.IndexOf(':') + 1)..].Trim()),
        ];

        private static string Faq(string heading, IReadOnlyList<string> questions) =>
            JsonSerializer.Serialize(new
            {
                tag = "h2",
                heading,
                paragraphs = Array.Empty<object>(),
                href = (string?)null,
                children = questions.Select(q => new
                {
                    tag = "h3",
                    heading = q,
                    paragraphs = new[] { new { type = "text", runs = new[] { new { text = $"Partner Widget answers it. {Sentence}" } } } },
                    href = (string?)null,
                    children = Array.Empty<object>(),
                }),
            });
    }

    internal sealed class FakeProviderFactory(IContentGenerationProvider provider) : IContentProviderFactory
    {
        public IContentGenerationProvider Get(LlmProviderType providerType) => provider;
        public IContentGenerationProvider GetDefault() => provider;
    }

    // ---- the pages ----------------------------------------------------------------------------------

    internal static readonly Guid CreateId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    internal static readonly Guid ProjectId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    internal static readonly DateTime At = new(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc);
    private static readonly string[] PartnerUrls = ["https://partner.test/"];

    /// <summary>
    /// The partners of the project a deck is written for. A deck has a slide for each partner and
    /// takes two at the least, so its project declares a second beside the one every other page here
    /// is written for; the Brief's own section is the first's.
    /// </summary>
    internal static readonly string[] DeckPartnerUrls = ["https://partner.test/", "https://second.test/"];

    internal static GccCreateDto Create(string type, string topic, string briefJson, string? researchJson, Guid? projectId) => new(
        Id: CreateId, ClientId: CreateId, OwnerUserId: CreateId,
        StartingContentType: type, Topic: topic, Notes: null,
        // A site crawl id, which the dispatcher requires before it writes a tool page or an image prompt.
        ProjectSiteRunId: ProjectId, SiteSectionJson: null, BriefJson: briefJson, ResearchJson: researchJson,
        Status: "draft", CreatedAtUtc: At, UpdatedAtUtc: At, ProjectId: projectId);

    internal static GccGenerateService Build(
        IContentGenerationProvider provider,
        GccPartnerExtractionService extraction,
        GccProjectDto? project,
        GccCompetitorAnalysisResolver? competitors = null) => new(
        new ContentPromptBuilder(),
        TestContentTypePrompts.Registry(),
        new FakeProviderFactory(provider),
        new SoftwareApplicationSchemaBuilder(),
        new BlogPostingSchemaBuilder(),
        new ArticleSchemaBuilder(new SoftwareApplicationSchemaBuilder()),
        Options.Create(new CompanyProfileOptions()),
        NullLogger<GccGenerateService>.Instance,
        competitors ?? GccCompetitorAnalysisResolverTests.Build(
            new GccCompetitorAnalysisResolverTests.FakeProjects(null),
            new GccCompetitorAnalysisResolverTests.FakePages(),
            new GccCompetitorAnalysisResolverTests.FakeRag()),
        extraction,
        new GccCompetitorAnalysisResolverTests.FakeProjects(project),
        new GccPublisherProfileResolver(
            new GccCompetitorAnalysisResolverTests.FakeProjects(null),
            new GccCompetitorAnalysisResolverTests.FakePages(),
            NullLogger<GccPublisherProfileResolver>.Instance),
        new GccKnownToolsResolver(
            new GccCompetitorAnalysisResolverTests.FakePages(),
            NullLogger<GccKnownToolsResolver>.Instance),
        new GccToolPageFanOutFixture.FakeExtractionBank());

    /// <summary>One partner page, as the brief's searches of the partner's crawl leave it.</summary>
    private static string ToolResearch() =>
        GccResearchFetchService.Serialize(new GccResearchDocument(
            SerpIndex: null,
            Quoteables:
            [
                new GccQuoteablePage(
                    Url: "https://partner.test/widget",
                    Title: "Partner Widget",
                    Headings: [new HeadingDto(2, "Pricing")],
                    Paragraphs:
                    [
                        "Partner Widget is billed monthly.",
                        "Partner Widget reduces setup time by half, and the vendor master maps itself.",
                    ],
                    RetrievalMode: GccQuoteablePage.RetrievalModeRagChunk),
            ]));

    /// <summary>The approved page an email or a social post is written from (<see cref="GccRepurpose"/>).</summary>
    private static readonly GccRepurposeSource ApprovedPage = new(
        Guid.Parse("99999999-9999-9999-9999-999999999999"), "pillar", "AI implementation", "AI Implementation: A Guide",
        "Where it goes wrong\n\nThe vendor master has to be mapped by hand before anything else can start.");

    private static IReadOnlyList<GccGroundedPassage> PartnerPassages() =>
    [
        new GccGroundedPassage("https://partner.test/widget", "Partner Widget",
        [
            new TextParagraph([new Run("Partner Widget is billed monthly.")]),
            new TextParagraph([new Run("Partner Widget reduces setup time by half, and the vendor master maps itself.")]),
        ]),
    ];

    /// <summary>
    /// Writes one page through the path the project's Generate takes for it, and returns every call it
    /// made with the page it saved as the last entry.
    /// </summary>
    /// <param name="model">The model the page's requests are sent to, which decides what one may carry.</param>
    /// <param name="competitors">The project's competitor crawls, for a test of what a page is shown of
    /// them; none when not given, as on a project that declares no competitor.</param>
    private static async Task<IReadOnlyList<(string Label, string Text)>> WriteAsync(
        string page,
        string briefJson,
        string? pageResearch = null,
        string model = "gpt-4.1",
        Func<GccCompetitorAnalysisResolver>? competitors = null,
        Func<string, string>? firstBody = null,
        Func<string, string, string>? deckReply = null,
        IReadOnlyList<string>? deckPartnerUrls = null)
    {
        var provider = new Recorder(page, model, firstBody, deckReply);
        var plain = Build(provider, GccPartnerExtractionFakes.NeverInvoked(new FakeProviderFactory(provider)), null);
        // The project the pages belong to, declaring the partner the brief's per-tool fields are entered
        // under. The pillar and the blog were written for no project until 2026-10-11, so nothing a
        // partner's own field could reach on them was ever in a prompt this audit read.
        var project = new GccProjectDto(
            ProjectId, CreateId, "Acme", null, null, "active", null, null, null,
            PartnerUrls, [], new DateOnly(2026, 10, 10), null, null, null, null, null, At, At);
        var forProject = Build(
            provider, GccPartnerExtractionFakes.NeverInvoked(new FakeProviderFactory(provider)), project, competitors?.Invoke());
        string saved;
        switch (page)
        {
            case "pillar":
                saved = await forProject.GeneratePillarBodyAsync(
                    Create("pillar", "AI implementation", briefJson, pageResearch, ProjectId), null, ContentGeneratorProvider.OpenAi, null, CancellationToken.None);
                break;
            case "blog":
                saved = await forProject.GenerateBlogBodyAsync(
                    Create("blog", "AI implementation", briefJson, pageResearch, ProjectId), null, ContentGeneratorProvider.OpenAi, null, CancellationToken.None);
                break;
            case "deck":
            {
                // "PDF / LinkedIn document": the cover with its post, then the slides, through the deck's
                // own writer, for a project with a partner for each slide.
                var deckService = Build(
                    provider, GccPartnerExtractionFakes.NeverInvoked(new FakeProviderFactory(provider)),
                    project with { PartnerUrls = [.. deckPartnerUrls ?? DeckPartnerUrls] }, competitors?.Invoke());
                saved = await deckService.GenerateLinkedInDocumentAsync(
                    Create("linkedin-document", "AI implementation", briefJson, pageResearch, ProjectId),
                    null, ContentGeneratorProvider.OpenAi, null, CancellationToken.None);
                break;
            }

            case "email":
                saved = await plain.RepurposeEmailAsync(
                    Create("email", "AI implementation", briefJson, null, null), ApprovedPage, null, ContentGeneratorProvider.OpenAi, CancellationToken.None);
                break;
            case "social":
                saved = await plain.RepurposeSocialPostAsync(
                    Create("linkedin", "AI implementation", briefJson, null, null), "linkedin", ApprovedPage, null, ContentGeneratorProvider.OpenAi, CancellationToken.None);
                break;
            case "image":
                saved = await plain.GenerateStartingContentAsync(
                    Create("image-prompt", "AI implementation", briefJson, null, null), null, ContentGeneratorProvider.OpenAi, CancellationToken.None);
                break;
            case "tool":
            {
                var extraction = GccPartnerExtractionFakes.EmptyPageExtraction with
                {
                    Citables = [new PartnerCitableItem("Partner Widget reduces setup time by half.", "reduces setup time by half")],
                    FeatureInventory = [new PartnerFeatureItem("Automated setup wizard", "Onboarding", null, "automated setup wizard")],
                    Integrations = [new PartnerIntegrationItem("QuickBooks Online", "accounting", null, null)],
                    Faqs = [new PartnerFaqItem("Is it secure?", "Yes, access is role based.", null)],
                };
                var service = Build(
                    provider, GccPartnerExtractionFakes.Scripted(new FakeProviderFactory(provider), extraction), project,
                    competitors?.Invoke());
                var toolName = GccRequiredToolMentions.AnchorLookup(briefJson, PartnerUrls).Values.Single();
                // What the fan-out does for each partner: the tool branch of the one dispatcher, told the product.
                saved = await service.GenerateStartingContentAsync(
                    Create("tool", "Partner Widget", briefJson, pageResearch ?? ToolResearch(), ProjectId) with { Notes = "Operator notes." },
                    null, ContentGeneratorProvider.OpenAi, CancellationToken.None,
                    passages: PartnerPassages(), toolName: toolName);
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(page), page, "not a page this audit writes");
        }

        return [.. provider.Calls, ("the saved page", WithoutDates(saved))];
    }

    /// <summary>
    /// A deck written through its real writer with the stand-in model: every request it made, and the
    /// page it saved as the last entry. For the tests of the deck's own writer.
    /// </summary>
    /// <param name="reply">What a reply is changed to, by the request's name here ("cover and post",
    /// "slides"); null for the replies as scripted.</param>
    /// <param name="partnerUrls">The project's partners; two when not given.</param>
    internal static Task<IReadOnlyList<(string Label, string Text)>> WriteDeckAsync(
        string briefJson,
        string? research = null,
        Func<string, string, string>? reply = null,
        IReadOnlyList<string>? partnerUrls = null,
        string model = "gpt-4.1",
        Func<GccCompetitorAnalysisResolver>? competitors = null) =>
        WriteAsync("deck", briefJson, research, model, competitors, deckReply: reply, deckPartnerUrls: partnerUrls);

    /// <summary>
    /// A whole tool page as its Generate saves it, written from the complete brief through the same path
    /// this audit reads: for a test that reads the saved page and not the calls that made it.
    /// </summary>
    internal static async Task<(string ToolName, string SavedPage)> SavedToolPageAsync()
    {
        var calls = await WriteAsync("tool", CompleteBrief);
        return (GccRequiredToolMentions.AnchorLookup(CompleteBrief, PartnerUrls).Values.Single(), calls[^1].Text);
    }

    /// <summary>The saved page without the publish times its structured data stamps, which differ run to run.</summary>
    private static string WithoutDates(string savedJson)
    {
        var text = new StringBuilder(savedJson.Length);
        var i = 0;
        while (i < savedJson.Length)
        {
            // An ISO date-time: four digits, a dash, and on to the closing quote.
            if (i + 10 < savedJson.Length && char.IsDigit(savedJson[i]) && char.IsDigit(savedJson[i + 3]) && savedJson[i + 4] == '-'
                && savedJson[i + 7] == '-' && savedJson[i + 10] == 'T')
            {
                while (i < savedJson.Length && savedJson[i] != '"' && savedJson[i] != '\\') i++;
                text.Append("<time>");
                continue;
            }

            text.Append(savedJson[i]);
            i++;
        }

        return text.ToString();
    }

    /// <summary>The searches a partner's crawl gets, as one text: what each asks for and how.</summary>
    private static IReadOnlyList<(string Label, string Text)> Searches(string briefJson)
    {
        var create = Create("tool", "Partner Widget", briefJson, null, ProjectId);
        static string Lines(IEnumerable<GccGroundingResolver.PartnerQuestion> questions) =>
            string.Join("\n", questions.Select(q => $"{q.Need} | {q.Keyword} | {q.TopK}"));

        return [("searches for evidence", Lines(GccGroundingResolver.PartnerQuestions(create, Host)))];
    }

    // ---- the audit ----------------------------------------------------------------------------------

    private static async Task<IReadOnlyList<(string Label, string Text)>> RunAsync(string page, string briefJson) =>
        page == "searches" ? Searches(briefJson) : await WriteAsync(page, briefJson);

    /// <summary>
    /// One line per field: the calls whose text changed when the field did. The body calls are named as
    /// "every body call" when all of them changed, so the line does not depend on how many a page takes.
    /// </summary>
    private static async Task<IReadOnlyList<string>> ReachAsync(string page, string baseBrief, IEnumerable<Field> fields)
    {
        var before = await RunAsync(page, baseBrief);
        var lines = new List<string>();
        foreach (var field in fields)
        {
            var after = await RunAsync(page, Changed(baseBrief, field));
            var reached = new List<string>();
            foreach (var label in before.Select(c => c.Label).Union(after.Select(c => c.Label)))
            {
                var was = before.Where(c => c.Label == label).Select(c => c.Text).SingleOrDefault();
                var now = after.Where(c => c.Label == label).Select(c => c.Text).SingleOrDefault();
                if (was is null) reached.Add($"{label} (only made with the changed value)");
                else if (now is null) reached.Add($"{label} (not made with the changed value)");
                else if (!string.Equals(was, now, StringComparison.Ordinal)) reached.Add(label);
            }

            var bodyCalls = before.Select(c => c.Label).Where(IsBodyCall).ToList();
            if (bodyCalls.Count > 0 && bodyCalls.All(reached.Contains))
            {
                var at = reached.FindIndex(IsBodyCall);
                reached.RemoveAll(IsBodyCall);
                reached.Insert(at, "every body call");
            }

            lines.Add($"{field.Name} => {(reached.Count == 0 ? "nothing" : string.Join(" | ", reached))}");
        }

        return lines;
    }

    private static bool IsBodyCall(string label) => label.StartsWith("body call", StringComparison.Ordinal);

    private static IReadOnlyList<string> Lines(string table) =>
    [
        .. table.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')),
    ];

    /// <summary>
    /// The control: a page written twice from the same brief is asked the same things, to the character.
    /// Without this a difference between two runs could be a clock or an id, not the brief.
    /// </summary>
    /// <summary>A brief whose every choice is a code with an underscore in it, so a code left in a prompt is findable.</summary>
    private static readonly string BriefOfCodes = CompleteBrief
        .Replace("\"primaryIntent\": \"informational\"", "\"primaryIntent\": \"commercial_investigation\"", StringComparison.Ordinal)
        .Replace("\"audienceSegment\": \"affinity\"", "\"audienceSegment\": \"in_market\"", StringComparison.Ordinal);

    private static readonly string[] StoredCodes =
    [
        "commercial_investigation", "in_market", "problem_solution", "consultant_professional", "first_hand_experience",
    ];

    /// <summary>
    /// The form stores a choice as a code and the writer is told it in words (<c>BriefChoiceWords</c>).
    /// Until 2026-10-10 a body call read "WHO THIS IS FOR: in_market".
    /// </summary>
    [Theory]
    [InlineData("pillar")]
    [InlineData("blog")]
    [InlineData("tool")]
    [InlineData("email")]
    [InlineData("social")]
    [InlineData("image")]
    [InlineData("deck")]
    public async Task No_call_is_shown_a_choice_as_the_code_the_form_stores(string page)
    {
        var calls = await RunAsync(page, BriefOfCodes);

        Assert.NotEmpty(calls);
        foreach (var (label, text) in calls.Where(c => c.Label != "the saved page"))
        {
            foreach (var code in StoredCodes)
            {
                Assert.False(text.Contains(code, StringComparison.Ordinal), $"{page}, {label}: the prompt carries the stored code \"{code}\".");
            }
        }
    }

    /// <summary>
    /// One call, one copy of the brief. Until 2026-10-10 a pillar call was told it twice and a blog or
    /// tool page call three times, each copy in its own wording, because the source context printed
    /// what the prompt builder had already printed.
    /// </summary>
    [Theory]
    [InlineData("pillar")]
    [InlineData("blog")]
    [InlineData("tool")]
    [InlineData("email")]
    [InlineData("social")]
    [InlineData("image")]
    [InlineData("deck")]
    public async Task No_call_is_told_the_brief_twice(string page)
    {
        var calls = await RunAsync(page, CompleteBrief);

        Assert.NotEmpty(calls);
        foreach (var (label, text) in calls.Where(c => c.Label != "the saved page"))
        {
            Assert.True(Count(text, "Primary intent:") <= 1, $"{page}, {label}: the intent is printed {Count(text, "Primary intent:")} times.");
            Assert.True(Count(text, "Buying stage:") <= 1, $"{page}, {label}: the buying stage is printed {Count(text, "Buying stage:")} times.");
            Assert.True(Count(text, "Tone of voice:") <= 1, $"{page}, {label}: the tone is printed {Count(text, "Tone of voice:")} times.");
            var audience = Count(text, "WHO THIS IS FOR:") + Count(text, "Audience segment:") + Count(text, "Audience:");
            Assert.True(audience <= 1, $"{page}, {label}: the audience is printed {audience} times.");
        }
    }

    /// <summary>
    /// Lines that begin with the label. A body prompt also names "brief:Primary intent: ..." inside its
    /// rule on heading tags, which is not a copy of the brief.
    /// </summary>
    private static int Count(string text, string label) =>
        text.Split('\n').Count(line => line.TrimStart().StartsWith(label, StringComparison.Ordinal));

    /// <summary>
    /// With more partner research than its model accepts in one request, a page's opening and each of
    /// its body requests are sent the passages chosen for them and no others, never more than the model
    /// accepts, and the run's record of each request lists the same passages. The pillar, the blog and
    /// the tool page alike.
    /// </summary>
    /// <remarks>
    /// Read off the requests the real writers make, so a request that was recorded as carrying a choice
    /// and sent something else fails here, and so does one sent over its model's limit. The model is the
    /// smaller of the two GeekAPI knows, 128,000 tokens a request, so that research a test can hold is
    /// more than it accepts. Until 2026-10-12 the tool page was sent everything whatever its size, and
    /// the pillar and the blog were cut at 350,000 characters whatever their model accepted.
    /// </remarks>
    [Theory]
    [InlineData("pillar", 5)]
    [InlineData("blog", 3)]
    [InlineData("tool", 5)]
    public async Task With_more_research_than_its_model_accepts_a_request_is_sent_what_its_record_says_was_chosen_for_it(string page, int bodyRequests)
    {
        // About 1,500 tokens a passage and 130 of them: more than 128,000 with nothing else in the request.
        var filler = string.Join(' ', Enumerable.Range(0, 480).Select(n => $"item{(n * 7919) % 10007}"));
        var passages = Enumerable.Range(0, 130).Select(i => $"Passage {i:000} of the partner's research. {filler}").ToList();
        var (calls, recorded) = await WriteWithResearchAsync(page, passages, model: "gpt-4o");
        const int all = 130;

        // Once for the page: how its research divides by label.
        var labels = JsonDocument.Parse(Assert.Single(recorded, e => e.Kind == "evidence-labels").PayloadJson).RootElement;
        Assert.Equal(all, labels.GetProperty("passages").GetInt32());
        Assert.Equal(65, labels.GetProperty("byLabel").GetProperty(GccResearchLabel.FitPriceAndLimits).GetInt32());

        // Once for each request that carries the research: the opening, which is request 0, then the body's.
        var evidence = recorded.Where(e => e.Kind == "evidence").Select(e => JsonDocument.Parse(e.PayloadJson).RootElement).ToList();
        Assert.Equal(Enumerable.Range(0, bodyRequests + 1), evidence.Select(e => e.GetProperty("call").GetInt32()));
        var body = calls.Where(c => c.Label == "body call" || c.Label.StartsWith("body call #", StringComparison.Ordinal)).ToList();
        Assert.Equal(bodyRequests, body.Count);
        var opening = Assert.Single(calls, c => c.Label == "opening");

        static string UrlOf(JsonElement listed) => listed.GetProperty("url").GetString()!;
        static bool IsOfTheBriefsSearches(string url) => url.Contains("/research/", StringComparison.Ordinal);
        static int PassageOf(string url) =>
            int.Parse(url.Split('/')[^1], System.Globalization.CultureInfo.InvariantCulture);

        var carriedByThePage = new HashSet<int>();
        foreach (var record in evidence)
        {
            Assert.False(record.GetProperty("whole").GetBoolean());
            Assert.Equal(bodyRequests, record.GetProperty("of").GetInt32());
            Assert.Equal(all, record.GetProperty("research").GetProperty("passages").GetInt32());

            // Counted before it was sent: with everything it is over what the model accepts, and as sent
            // it is not, the room reserved for its reply included.
            var size = record.GetProperty("request");
            Assert.Equal("gpt-4o", size.GetProperty("model").GetString());
            Assert.Equal(128_000, size.GetProperty("accepts").GetInt32());
            Assert.InRange(size.GetProperty("withEverything").GetInt32() + size.GetProperty("reply").GetInt32(), 128_001, int.MaxValue);
            Assert.InRange(size.GetProperty("withReply").GetInt32(), 1, 128_000);
            Assert.InRange(record.GetProperty("carried").GetProperty("chars").GetInt32(), 1, record.GetProperty("room").GetInt32());

            var chosenUrls = record.GetProperty("chosen").EnumerateArray().Select(UrlOf).ToList();
            var leftOutUrls = record.GetProperty("leftOut").EnumerateArray().Select(UrlOf).ToList();
            var chosen = chosenUrls.Where(IsOfTheBriefsSearches).Select(PassageOf).ToList();
            var leftOut = leftOutUrls.Where(IsOfTheBriefsSearches).Select(PassageOf).ToList();
            Assert.NotEmpty(chosen);
            Assert.NotEmpty(leftOut);
            Assert.Equal(all, chosenUrls.Count + leftOutUrls.Count);

            // The request carries the passages its record lists as chosen, none it lists as left out,
            // and is told that what it has is a part.
            var call = record.GetProperty("call").GetInt32();
            var text = call == 0 ? opening.Text : body[call - 1].Text;
            Assert.All(chosen, i => Assert.Contains(passages[i], text, StringComparison.Ordinal));
            Assert.All(leftOut, i => Assert.DoesNotContain(passages[i], text, StringComparison.Ordinal));
            Assert.Contains(GccGenerateService.ResearchIsAPartNote, text, StringComparison.Ordinal);
            carriedByThePage.UnionWith(chosen);
        }

        // Between them the page's requests reach more of the research than any one of them carries.
        Assert.InRange(carriedByThePage.Count, evidence.Max(e => e.GetProperty("chosen").GetArrayLength()) + 1, 130);

        // No request is told a label. "problem" is a word any prompt uses; the other six are spelled as no
        // prose is, so finding one would mean a label had been printed.
        foreach (var call in calls.Where(c => c.Label != "the saved page"))
        {
            Assert.All(
                GccResearchLabel.All.Where(label => label.Contains('-')),
                label => Assert.DoesNotContain(label, call.Text, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// The same research that the smaller model cannot take in one request is sent whole to the larger
    /// one, to every request, and nothing is chosen from it: no number but the model's own decides.
    /// </summary>
    /// <remarks>
    /// About 550,000 characters of partner research. Until 2026-10-12 anything over 350,000 was cut to
    /// 40,000 for each partner before any model was asked what it accepts.
    /// </remarks>
    [Theory]
    [InlineData("pillar")]
    [InlineData("blog")]
    [InlineData("tool")]
    public async Task Research_a_larger_model_accepts_is_sent_whole_however_large_it_is(string page)
    {
        var filler = string.Join(' ', Enumerable.Range(0, 480).Select(n => $"item{(n * 7919) % 10007}"));
        var passages = Enumerable.Range(0, 130).Select(i => $"Passage {i:000} of the partner's research. {filler}").ToList();
        Assert.InRange(passages.Sum(passage => passage.Length), 500_000, int.MaxValue);

        var (calls, recorded) = await WriteWithResearchAsync(page, passages, model: "gpt-4.1");

        var evidence = recorded.Where(e => e.Kind == "evidence").Select(e => JsonDocument.Parse(e.PayloadJson).RootElement).ToList();
        Assert.NotEmpty(evidence);
        Assert.All(evidence, record =>
        {
            Assert.True(record.GetProperty("whole").GetBoolean());
            var size = record.GetProperty("request");
            Assert.Equal(1_047_576, size.GetProperty("accepts").GetInt32());
            Assert.Equal(size.GetProperty("tokens").GetInt32(), size.GetProperty("withEverything").GetInt32());
            Assert.InRange(size.GetProperty("withReply").GetInt32(), 128_001, 1_047_576);
        });
        var withResearch = calls
            .Where(c => c.Label == "opening" || c.Label == "body call" || c.Label.StartsWith("body call #", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(evidence.Count, withResearch.Count);
        Assert.All(withResearch, call => Assert.All(passages, passage => Assert.Contains(passage, call.Text, StringComparison.Ordinal)));
        Assert.All(calls, call => Assert.DoesNotContain(GccGenerateService.ResearchIsAPartNote, call.Text, StringComparison.Ordinal));
    }

    /// <summary>
    /// Research that fits is sent whole to every request of a pillar and a blog, the opening included,
    /// and nothing tells a writer it has a part. A project that fits is not changed by any of this.
    /// </summary>
    [Theory]
    [InlineData("pillar")]
    [InlineData("blog")]
    public async Task Research_that_fits_is_sent_whole_to_every_request_and_nothing_says_it_is_a_part(string page)
    {
        var filler = new string('x', 2_900);
        var passages = Enumerable.Range(0, 20).Select(i => $"Passage {i:000} of the partner's research. {filler}").ToList();
        var (calls, recorded) = await WriteWithResearchAsync(page, passages);

        var evidence = recorded.Where(e => e.Kind == "evidence").Select(e => JsonDocument.Parse(e.PayloadJson).RootElement).ToList();
        Assert.NotEmpty(evidence);
        Assert.All(evidence, record =>
        {
            Assert.True(record.GetProperty("whole").GetBoolean());
            Assert.Equal(JsonValueKind.Null, record.GetProperty("chosen").ValueKind);
        });
        var withResearch = calls
            .Where(c => c.Label == "opening" || c.Label == "body call" || c.Label.StartsWith("body call #", StringComparison.Ordinal))
            .ToList();
        Assert.InRange(withResearch.Count, 4, int.MaxValue);
        Assert.All(withResearch, call => Assert.All(passages, passage => Assert.Contains(passage, call.Text, StringComparison.Ordinal)));
        Assert.All(calls, call => Assert.DoesNotContain(GccGenerateService.ResearchIsAPartNote, call.Text, StringComparison.Ordinal));
    }

    /// <summary>
    /// A run whose model GeekAPI holds no limit for is refused at its start: before its old pages are
    /// deleted, before anything is searched for, and before any request is sent. Its requests could not
    /// be counted against what the model accepts, and finding that out at the first request would be
    /// after the old pages had gone.
    /// </summary>
    [Fact]
    public async Task A_run_for_a_model_whose_limit_is_not_known_is_refused_before_anything_is_deleted_or_sent()
    {
        var provider = new Recorder("pillar", model: "gpt-5");
        var service = Build(provider, GccPartnerExtractionFakes.NeverInvoked(new FakeProviderFactory(provider)), null);
        var repository = new CountingRepository();
        // The refusal comes before the searches, so the run never reaches what does them.
        var coordinator = new GccGenerationCoordinator(grounding: null!, NullLogger<GccGenerationCoordinator>.Instance);

        Assert.Equal(OpenAiModelLimits.Refusal("gpt-5"), service.ModelRefusal(ContentGeneratorProvider.OpenAi));
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.RunGenerateAsync(
            new HttpGccRepository(
                new HttpClient(repository) { BaseAddress = new Uri("http://repo.test/") }, NullLogger<HttpGccRepository>.Instance),
            service,
            Create("pillar", "AI implementation", CompleteBrief, null, ProjectId),
            section: null,
            ContentGeneratorProvider.OpenAi,
            outputTypes: ["pillar"],
            mustMentionBlock: null,
            CancellationToken.None));

        Assert.Equal(OpenAiModelLimits.Refusal("gpt-5"), refused.Message);
        Assert.Equal(0, repository.Requests);
        Assert.Empty(provider.Calls);

        // A model that is listed is not refused here.
        var listed = new Recorder("pillar");
        Assert.Null(
            Build(listed, GccPartnerExtractionFakes.NeverInvoked(new FakeProviderFactory(listed)), null)
                .ModelRefusal(ContentGeneratorProvider.OpenAi));
    }

    /// <summary>The repository, counting what it is asked: a refused run must ask it for nothing.</summary>
    private sealed class CountingRepository : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError));
        }
    }

    // ---- nothing is cut by a count ------------------------------------------------------------------

    internal const int PassagesOnThePartnersPage = 9;
    internal const int CompetitorPages = 7;
    internal const int OwnSitePages = 7;

    internal static string PartnerPassage(int n) => $"Partner Widget passage {n:00}, on the page with more than six.";

    // A competitor's name is kept out of what a writer reads, so none of this text carries the host's.
    internal static string CompetitorPassage(int page, int n) => $"The other vendor's page {page:00} states point {n:00} of its own.";

    internal static string OwnSitePassage(int page, int n) => $"Our published page {page:00} covers matter {n:00} already.";

    internal static string CompetitorHeading(int page, int n) => $"Other vendor outline {page:00} heading {n:00}";

    /// <summary>
    /// Research that every count the blocks used to cut by would cut: a partner page of nine passages
    /// (six were printed), seven competitor pages of seven passages (five pages of six), seven of the
    /// publisher's own pages of seven passages (five of six).
    /// </summary>
    internal static string ResearchPastEveryOldCount()
    {
        return GccResearchFetchService.Serialize(new GccResearchDocument(SerpIndex: null, Quoteables: []) with
        {
            Quoteables =
            [
                new GccQuoteablePage(
                    $"https://{Host}/long", "Partner Widget", [],
                    [.. Enumerable.Range(1, PassagesOnThePartnersPage).Select(PartnerPassage)],
                    RetrievalMode: GccQuoteablePage.RetrievalModeRagChunk),
                // A page on the partner's help address is the partner's, on a tool page as on the others.
                new GccQuoteablePage(
                    $"https://help.{Host}/setup", "Partner Widget help", [],
                    ["Partner Widget help: setup takes one afternoon."],
                    RetrievalMode: GccQuoteablePage.RetrievalModeRagChunk),
            ],
            CompetitorQuoteables =
            [
                .. Enumerable.Range(1, CompetitorPages).Select(c => new GccQuoteablePage(
                    $"https://contender.test/page-{c:00}", $"Other vendor page {c:00}", [],
                    [.. Enumerable.Range(1, 7).Select(n => CompetitorPassage(c, n))],
                    RetrievalMode: GccQuoteablePage.RetrievalModeRagChunk)),
            ],
            SiteQuoteables =
            [
                .. Enumerable.Range(1, OwnSitePages).Select(o => new GccQuoteablePage(
                    $"https://acme.test/published-{o:00}", $"Published {o:00}", [],
                    [.. Enumerable.Range(1, 7).Select(n => OwnSitePassage(o, n))],
                    RetrievalMode: GccQuoteablePage.RetrievalModeRagChunk)),
            ],
            PublisherPositions =
            [
                new GccPublisherPosition("The Methodology", ["We map the close before we automate it."], "https://acme.test/"),
            ],
        });
    }

    /// <summary>
    /// The competitor's crawl: an outline of thirty headings for each page the search returned (twenty-five
    /// were printed), and one for a page no search returned.
    /// </summary>
    internal static GccCompetitorAnalysisResolver CompetitorCrawl() => GccCompetitorAnalysisResolverTests.Build(
        new GccCompetitorAnalysisResolverTests.FakeProjects(GccCompetitorAnalysisResolverTests.Project("https://contender.test")),
        new GccCompetitorAnalysisResolverTests.FakePages(
        [
            .. Enumerable.Range(1, CompetitorPages).Select(c => GccCompetitorAnalysisResolverTests.BlockPage(
                $"https://contender.test/page-{c:00}",
                [.. Enumerable.Range(1, 30).Select(n => GccCompetitorAnalysisResolverTests.Heading(2, CompetitorHeading(c, n)))])),
            GccCompetitorAnalysisResolverTests.BlockPage(
                "https://contender.test/never-returned",
                GccCompetitorAnalysisResolverTests.Heading(2, "An outline no search returned")),
        ]),
        new GccCompetitorAnalysisResolverTests.FakeRag(
            hosts: [new GeekCrawlerRagHostIndex("https://contender.test", "contender.test", true, Guid.NewGuid().ToString())]));

    /// <summary>
    /// Nothing a search brought back is left out of a request for a count (Jeff, 2026-10-12: "STOP NOT
    /// USING DATA, EVEN IF THAT MEANS IT COSTS MORE"). Written through the real writers, with research
    /// past every count the blocks used to cut by: a page's passages past its sixth, competitor pages
    /// past the fifth, the publisher's own pages past the fifth and outline headings past the
    /// twenty-fifth.
    /// </summary>
    [Theory]
    [InlineData("pillar")]
    [InlineData("blog")]
    [InlineData("tool")]
    public async Task Nothing_a_search_brought_back_is_left_out_of_a_request_for_a_count(string page)
    {
        var calls = await WriteAsync(page, CompleteBrief, ResearchPastEveryOldCount(), competitors: CompetitorCrawl);

        var opening = Assert.Single(calls, c => c.Label == "opening").Text;
        var body = calls.Where(c => c.Label == "body call" || c.Label.StartsWith("body call #", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(body);

        // The opening and every body request: every passage of the partner's page and the partner's
        // help page.
        foreach (var text in body.Select(b => b.Text).Append(opening))
        {
            Assert.All(Enumerable.Range(1, PassagesOnThePartnersPage), n => Assert.Contains($"- {PartnerPassage(n)}", text, StringComparison.Ordinal));
            Assert.Contains("- Partner Widget help: setup takes one afternoon.", text, StringComparison.Ordinal);
            // The publisher's positions: in a tool page's opening too, since 2026-10-12.
            Assert.Contains("We map the close before we automate it.", text, StringComparison.Ordinal);
        }

        // Every body request: every competitor page and every one of the publisher's own, every passage
        // of each, and the outline of each competitor page the search returned, every heading of it.
        foreach (var (_, text) in body)
        {
            for (var c = 1; c <= CompetitorPages; c++)
            {
                Assert.Contains($"[Competitor page {c}: ", text, StringComparison.Ordinal);
                Assert.Contains($"[Competitor page {c}]", text, StringComparison.Ordinal);
                Assert.All(Enumerable.Range(1, 7), n => Assert.Contains($"- {CompetitorPassage(c, n)}", text, StringComparison.Ordinal));
                Assert.All(Enumerable.Range(1, 30), n => Assert.Contains($"- {CompetitorHeading(c, n)} (h2)", text, StringComparison.Ordinal));
            }

            for (var o = 1; o <= OwnSitePages; o++)
            {
                Assert.All(Enumerable.Range(1, 7), n => Assert.Contains($"- {OwnSitePassage(o, n)}", text, StringComparison.Ordinal));
            }

            // Not the outline of a page no search returned: that is the rest of a competitor's site.
            Assert.DoesNotContain("An outline no search returned", text, StringComparison.Ordinal);
            // A competitor's address is never shown.
            Assert.DoesNotContain("contender", text, StringComparison.OrdinalIgnoreCase);
        }

        // A pillar and a blog tag a heading drawn from a competitor's outline; a tool page tags nothing
        // and is not told to.
        Assert.All(body, b => Assert.Equal(page != "tool", b.Text.Contains("\"competitor:<exact heading text>\"", StringComparison.Ordinal)));
    }

    /// <summary>
    /// A pillar, a blog or a tool page written through its real writer for the partner, with
    /// <paramref name="passages"/> as its partner research, a passage a page: the requests it made, and
    /// what went on the run's record.
    /// </summary>
    private static async Task<(IReadOnlyList<(string Label, string Text)> Calls, List<GccGenerateJobEventWrite> Recorded)> WriteWithResearchAsync(
        string page, IReadOnlyList<string> passages, string model = "gpt-4.1")
    {
        var research = GccResearchFetchService.Serialize(
            new GccResearchDocument(SerpIndex: null, Quoteables: []) with
            {
                Quoteables =
                [
                    .. passages.Select((passage, i) => new GccQuoteablePage(
                        $"https://{Host}/research/{i}", "Partner Widget", [], [passage],
                        RetrievalMode: GccQuoteablePage.RetrievalModeRagChunk,
                        Category: i % 2 == 0 ? "pricing" : "blog")),
                ],
            });
        var recorded = new List<GccGenerateJobEventWrite>();
        GccRunLog.Begin(
            Guid.NewGuid(),
            (events, _) =>
            {
                lock (recorded) recorded.AddRange(events);
                return Task.CompletedTask;
            },
            NullLogger.Instance);

        return (await WriteAsync(page, CompleteBrief, research, model), recorded);
    }

    /// <summary>
    /// A brief with every failure under a partner still tells the pillar's and the blog's failure
    /// sections what to argue, and only in a call that carries those failures.
    /// </summary>
    /// <remarks>
    /// The 13:14 UTC run of 2026-10-11 was the first brief entered that way: 25 failures, and neither
    /// page was told one. The record below could not show it, because the complete brief it changes a
    /// field of names failures for the category as well.
    /// </remarks>
    [Theory]
    [InlineData("pillar")]
    [InlineData("blog")]
    public async Task With_every_failure_under_a_partner_the_failure_sections_argue_the_partners(string page)
    {
        const string sentToThePartners = "entered those failures under the partner that answers each";

        var calls = await WriteAsync(page, WithEveryFailureUnderAPartner(CompleteBrief));

        var sent = calls.Where(c => c.Text.Contains(sentToThePartners, StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(sent);
        foreach (var call in sent)
        {
            // A section is never sent to a block its own call does not carry.
            Assert.Contains($"{GccPartnerAnswers.Heading} -- THE OPERATOR'S OWN RESEARCH", call.Text, StringComparison.Ordinal);
            Assert.Contains(
                $"1. {GccPartnerAnswers.FailureLabel}: The vendor master has to be mapped by hand.", call.Text, StringComparison.Ordinal);
            Assert.Contains("The operator named no failure apart from these.", call.Text, StringComparison.Ordinal);
        }

        // With a failure for the category, the sections cover the category's and are sent nowhere.
        var asEntered = await WriteAsync(page, CompleteBrief);
        Assert.DoesNotContain(asEntered, c => c.Text.Contains(sentToThePartners, StringComparison.Ordinal));
        Assert.Contains(asEntered, c => c.Text.Contains("add no failure the operator did not name:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("pillar")]
    [InlineData("blog")]
    [InlineData("tool")]
    [InlineData("email")]
    [InlineData("social")]
    [InlineData("image")]
    [InlineData("searches")]
    [InlineData("deck")]
    public async Task The_same_brief_gives_the_same_calls(string page)
    {
        var first = await RunAsync(page, CompleteBrief);
        var second = await RunAsync(page, CompleteBrief);

        Assert.Equal(first.Select(c => c.Label), second.Select(c => c.Label));
        foreach (var (label, text) in first)
            Assert.True(text == second.Single(c => c.Label == label).Text, $"{page}: '{label}' differs between two runs of one brief.");
    }

    /// <summary>
    /// Each field reaches the calls recorded for it, and no others. A line here changes only with the code
    /// that changes what a field reaches: a field that stops reaching a call, or starts to, fails this.
    /// </summary>
    /// <remarks>
    /// The record is what the code does on 2026-10-10, not what it should do. A line under "# GAP" is a
    /// field the operator enters that a call ignores; it is recorded so that closing it is a change to
    /// this table made with the fix, and so that it cannot reopen unseen. None is left. The audit that
    /// listed them was removed as finished on 2026-10-12 and is in git
    /// (<c>git show dce00d2:plans/brief-field-audit.md</c>).
    /// <para>
    /// Closed the same day, each a changed line below: the pillar's opening now carries the operator's
    /// whole framing, the tool page's two FAQ calls and its summaries call carry the reader and the
    /// voice, and the email and the social post carry the framing. And the blog's and the tool page's
    /// opening carry the operator's problem: their framing was on the first body section alone.
    /// </para>
    /// <para>
    /// <b>2026-10-12, the boxes of the niche framing</b> (Jeff: "WHY am I spending so much time
    /// entering and you not using"). "Nothing" was recorded here for boxes the operator fills in, as
    /// if a record of the gap were the fix. After that day no box of the category's framing or of a
    /// tool's own section reads "nothing" on the pillar, the blog or the tool page, but a row's
    /// search terms, whose use is the search, and a tool's own questions, which are its own page's.
    /// <see cref="No_box_of_the_niche_framing_is_entered_and_shown_to_no_long_page"/> holds that
    /// whatever the table says.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(Recorded))]
    public async Task Each_field_reaches_the_calls_recorded_for_it(string page, bool toolHasItsOwnFraming, string recorded)
    {
        var brief = toolHasItsOwnFraming ? CompleteBrief : WithoutToolFraming(CompleteBrief);
        var fields = toolHasItsOwnFraming ? Fields : [.. Fields.Where(f => f.Name.StartsWith("nicheFraming.", StringComparison.Ordinal))];

        var reach = await ReachAsync(page, brief, fields);

        output.WriteLine(string.Join(Environment.NewLine, reach));
        Assert.Equal(Lines(recorded), reach);
    }

    /// <summary>The boxes of the niche framing a writer argues from: the category's, and one tool's own.</summary>
    private static readonly string[] FramingBoxes =
    [
        "nicheFraming.coreProblem", "nicheFraming.painPoints", "nicheFraming.automationToPitch",
        "nicheFraming.evidence.problem", "nicheFraming.evidence.solution",
        "perTool.coreProblem", "perTool.painPoints", "perTool.automationToPitch",
        "perTool.evidence.problem", "perTool.evidence.solution",
    ];

    /// <summary>
    /// What the operator enters for the category and for a tool is shown to the writer of every long
    /// page, whatever the record below says: a line there reading "nothing" for one of these boxes
    /// fails here. On the tool's own page each is in every body request, so in all ten sections; on a
    /// deck each is in both of its requests.
    /// </summary>
    /// <remarks>
    /// Jeff, 2026-10-12: "WHY am I spending so much time entering and you not using". Until that day
    /// the record held "nothing" for a tool's remedies on its own page, for the category's problem and
    /// pitch on a page of a tool with its own, for a remedy saved for all partners on every page, and
    /// for a tool's failures saved without a row on the pillar and the blog; and what a tool page was
    /// told reached its first body request alone. Recording a gap was treated as closing it.
    /// </remarks>
    [Theory]
    [InlineData("pillar")]
    [InlineData("blog")]
    [InlineData("tool")]
    [InlineData("deck")]
    public async Task No_box_of_the_niche_framing_is_entered_and_shown_to_no_long_page(string page)
    {
        var reach = await ReachAsync(page, CompleteBrief, Fields.Where(f => FramingBoxes.Contains(f.Name)));

        Assert.Equal(FramingBoxes, reach.Select(line => line[..line.IndexOf(" => ", StringComparison.Ordinal)]));
        Assert.All(reach, line => Assert.False(line.EndsWith("=> nothing", StringComparison.Ordinal), $"{page}: {line}"));
        if (page == "tool")
        {
            Assert.All(reach, line => Assert.True(line.Contains("every body call", StringComparison.Ordinal), $"{page}: {line}"));
        }

        // A deck is two requests, and each box is in both: the cover and the post open on the
        // problem the operator named, and the slides are built on the rest.
        if (page == "deck")
        {
            Assert.All(reach, line => Assert.True(line.EndsWith("=> cover and post | slides", StringComparison.Ordinal), $"{page}: {line}"));
        }
    }

    /// <summary>
    /// A tool page's five body requests each carry what the operator entered for every partner and for
    /// that tool, once: both problems, every failure with the remedy noted for it, both pitches. The
    /// section that opens on the problem is sent to that block, and the hook is told both problems.
    /// </summary>
    /// <remarks>
    /// Until 2026-10-12 all a tool page was told of the framing was its first section's guidance: one
    /// of the two problems and one of the two pitches, the failures with no remedy, in the first of
    /// five body requests.
    /// </remarks>
    [Fact]
    public async Task A_tool_pages_every_body_request_carries_what_the_operator_entered_for_it_once()
    {
        var name = GccRequiredToolMentions.AnchorLookup(CompleteBrief, PartnerUrls).Values.Single();

        var calls = await WriteAsync("tool", CompleteBrief);

        var body = calls.Where(c => IsBodyCall(c.Label)).ToList();
        Assert.Equal(5, body.Count);
        Assert.All(body, call =>
        {
            Assert.Equal(1, Count(call.Text, $"{GccPartnerAnswers.ToolPageHeading} -- THE OPERATOR'S OWN RESEARCH"));
            // "The core problem" and this tool's "Core pain point", both.
            Assert.Equal(1, Count(call.Text, "The problem: The close runs on spreadsheets nobody else can read."));
            Assert.Equal(1, Count(call.Text, $"{name}'s own pain point: Setup takes a consultant a fortnight."));
            // The failures for every partner, the first with the remedy saved for all partners; then
            // the tool's own, with what the tool does about it.
            Assert.Contains("1. Where it goes wrong: Reconciliations start after the period ends.", call.Text, StringComparison.Ordinal);
            Assert.Contains("The remedy noted for every partner: Continuous matching of bank lines to ledger entries.", call.Text, StringComparison.Ordinal);
            Assert.Contains("2. Where it goes wrong: Nobody knows which version of the workbook is current.", call.Text, StringComparison.Ordinal);
            Assert.Contains("3. Where it goes wrong: The vendor master has to be mapped by hand.", call.Text, StringComparison.Ordinal);
            Assert.Contains($"What {name} does about it: Automated setup wizard maps the vendor master.", call.Text, StringComparison.Ordinal);
            // "The automation to pitch" and this tool's own pitch, both.
            Assert.Equal(1, Count(call.Text, "What answers it: A close calendar that assigns, chases and signs off each reconciliation."));
            Assert.Equal(1, Count(call.Text, $"What the operator would pitch {name} on: A setup wizard that maps the vendor master on import."));
        });

        // The opening section is the first request's, and is sent to the block that request carries.
        Assert.Contains(GccPartnerAnswers.ToolOpeningGuidance, body[0].Text, StringComparison.Ordinal);
        Assert.All(body.Skip(1), call => Assert.DoesNotContain(GccPartnerAnswers.ToolOpeningGuidance, call.Text, StringComparison.Ordinal));

        // The hook is told the problem, both statements of it, and none of the rest.
        var hook = Assert.Single(calls, c => c.Label == "opening").Text;
        Assert.Contains("The problem: The close runs on spreadsheets nobody else can read.", hook, StringComparison.Ordinal);
        Assert.Contains($"{name}'s own pain point: Setup takes a consultant a fortnight.", hook, StringComparison.Ordinal);
        Assert.DoesNotContain(GccPartnerAnswers.ToolPageHeading, hook, StringComparison.Ordinal);
        Assert.DoesNotContain("A setup wizard that maps the vendor master on import.", hook, StringComparison.Ordinal);
    }

    /// <summary>
    /// A partner's site is searched with every statement of the problem the operator entered: the
    /// tool's own "Core pain point" and the category's "The core problem", both.
    /// </summary>
    [Fact]
    public async Task Both_statements_of_the_problem_are_searched_for_on_a_partners_site()
    {
        var reach = await ReachAsync(
            "searches", CompleteBrief, Fields.Where(f => f.Name is "nicheFraming.coreProblem" or "perTool.coreProblem"));

        Assert.Equal(
            ["nicheFraming.coreProblem => searches for evidence", "perTool.coreProblem => searches for evidence"],
            reach);
    }

    public static TheoryData<string, bool, string> Recorded() => new()
    {
        {
            "pillar", true,
            """
            primaryIntent => opening | People Also Ask | every body call | title and outline
            secondaryIntent => opening | People Also Ask | every body call | title and outline
            buyingStage => opening | People Also Ask | every body call | title and outline
            audienceSegment => opening | People Also Ask | every body call | title and outline
            audienceNotes => opening | People Also Ask | every body call | title and outline
            angle => opening | every body call | title and outline
            toneOfVoice => opening | People Also Ask | every body call | title and outline
            eeatSignals => opening | People Also Ask | every body call | title and outline
            lengthBand => opening | People Also Ask | every body call | title and outline
            # Each question is written from the answer the operator entered for it, in its own request
            # (2026-10-13), and no crawl is searched for one. A question is also a heading the outline
            # may take, which is why it is in every body call; its answer is in that request alone.
            paaQuestions => People Also Ask | every body call | the saved page
            paaQuestions, an answer alone => People Also Ask
            blogFaqQuestions => nothing
            blogFaqQuestions, an answer alone => nothing
            briefVersion => nothing
            nicheFraming.taxonomyPath, first level => the saved page
            # The first level is the department in the page's address, and one that is not a department
            # refuses the run. The later levels are the operator's record of where the keyword sits.
            nicheFraming.taxonomyPath, later levels => nothing
            nicheFraming.diagnosisQuestions => the saved page
            nicheFraming.coreProblem => opening
            nicheFraming.painPoints => opening | body call
            nicheFraming.automationToPitch => opening | body call #2
            # A row saved for all partners (2026-10-12): its failure is one the page argues even when
            # no saved paragraph states it, and its remedy is listed under no partner's name in every
            # body call (GccPartnerAnswers). Until then the remedy reached no writer. A row's search
            # terms are the search's alone, here and on every page.
            nicheFraming.evidence.problem => opening | every body call
            nicheFraming.evidence.solution => every body call
            nicheFraming.evidence.terms => nothing
            # No box of the form: the category has no FAQ of its own.
            nicheFraming.faqQuestions => nothing
            # A partner's own problem and what the operator would pitch it on, under that partner's name
            # beside its rows (GccPartnerAnswers, 2026-10-12; Jeff: "pillars & blog should have the
            # research data"). Its failures saved without a row are listed there too since 2026-10-12;
            # until then they reached only its own tool page.
            perTool.coreProblem => every body call
            perTool.painPoints => every body call
            perTool.automationToPitch => every body call
            # A partner's own rows: which partner answers which failure, and what it does about it
            # (GccPartnerAnswers, 2026-10-11). Beside the partners the page must name, in every body call.
            perTool.evidence.problem => every body call
            perTool.evidence.solution => every body call
            perTool.evidence.terms => nothing
            # A tool's questions are its own page's.
            perTool.faqQuestions => nothing
            perTool.faqQuestions, an answer alone => nothing
            """
        },
        {
            "blog", true,
            """
            primaryIntent => title and outline | opening | FAQ | every body call
            secondaryIntent => title and outline | opening | FAQ | every body call
            buyingStage => title and outline | opening | FAQ | every body call
            audienceSegment => title and outline | opening | FAQ | every body call
            audienceNotes => title and outline | opening | FAQ | every body call
            angle => title and outline | opening | every body call
            toneOfVoice => title and outline | opening | FAQ | every body call
            eeatSignals => title and outline | opening | FAQ | every body call
            lengthBand => title and outline | opening | FAQ | every body call
            paaQuestions => every body call
            paaQuestions, an answer alone => nothing
            blogFaqQuestions => FAQ | the saved page
            blogFaqQuestions, an answer alone => FAQ
            briefVersion => nothing
            nicheFraming.taxonomyPath, first level => the saved page
            nicheFraming.taxonomyPath, later levels => nothing
            nicheFraming.diagnosisQuestions => the saved page
            nicheFraming.coreProblem => opening | body call
            nicheFraming.painPoints => body call
            nicheFraming.automationToPitch => body call | body call #2
            # A row saved for all partners, as on the pillar: its failure argued, its remedy listed
            # under no partner's name in every body call (2026-10-12).
            nicheFraming.evidence.problem => every body call
            nicheFraming.evidence.solution => every body call
            nicheFraming.evidence.terms => nothing
            nicheFraming.faqQuestions => nothing
            # A partner's own problem and what the operator would pitch it on, under that partner's name
            # beside its rows (GccPartnerAnswers, 2026-10-12; Jeff: "pillars & blog should have the
            # research data"). Its failures saved without a row are listed there too since 2026-10-12.
            perTool.coreProblem => every body call
            perTool.painPoints => every body call
            perTool.automationToPitch => every body call
            # A partner's own rows: which partner answers which failure, and what it does about it
            # (GccPartnerAnswers, 2026-10-11). Beside the partners the page must name, in every body call.
            perTool.evidence.problem => every body call
            perTool.evidence.solution => every body call
            perTool.evidence.terms => nothing
            perTool.faqQuestions => nothing
            perTool.faqQuestions, an answer alone => nothing
            """
        },
        {
            "tool", true,
            """
            primaryIntent => opening | every body call | FAQ from the partner's own FAQ | FAQ from the operator's answers | summaries
            secondaryIntent => opening | every body call | FAQ from the partner's own FAQ | FAQ from the operator's answers | summaries
            buyingStage => opening | every body call | FAQ from the partner's own FAQ | FAQ from the operator's answers | summaries
            audienceSegment => opening | every body call | FAQ from the partner's own FAQ | FAQ from the operator's answers | summaries
            audienceNotes => opening | every body call | FAQ from the partner's own FAQ | FAQ from the operator's answers | summaries
            angle => opening | every body call
            toneOfVoice => opening | every body call | FAQ from the partner's own FAQ | FAQ from the operator's answers | summaries
            eeatSignals => opening | every body call | FAQ from the partner's own FAQ | FAQ from the operator's answers | summaries
            lengthBand => opening | every body call | FAQ from the partner's own FAQ | FAQ from the operator's answers | summaries
            paaQuestions => nothing
            paaQuestions, an answer alone => nothing
            blogFaqQuestions => nothing
            blogFaqQuestions, an answer alone => nothing
            briefVersion => nothing
            nicheFraming.taxonomyPath, first level => nothing
            nicheFraming.taxonomyPath, later levels => nothing
            nicheFraming.diagnosisQuestions => summaries | the saved page
            # Both, never one for the other (Jeff, 2026-10-12: "it is not an OR it is both PAIN
            # POINTS"). Until then this tool's own problem and pitch were told in the category's place,
            # so the category's two boxes read "nothing" here; and everything the page was told of
            # either set was in its first section's guidance, which the first of five body calls
            # alone prints. Now the problem is in the hook, and all of it is in a block every body
            # call carries (GccPartnerAnswers.ForToolPage), each failure with the remedy noted for it.
            nicheFraming.coreProblem => opening | every body call
            nicheFraming.painPoints => every body call
            nicheFraming.automationToPitch => every body call
            nicheFraming.evidence.problem => every body call
            nicheFraming.evidence.solution => every body call
            nicheFraming.evidence.terms => nothing
            nicheFraming.faqQuestions => nothing
            perTool.coreProblem => opening | every body call
            perTool.painPoints => every body call
            perTool.automationToPitch => every body call
            # The tool's own rows: until 2026-10-12 the remedy was used to search and never shown to
            # the tool's own page.
            perTool.evidence.problem => every body call
            perTool.evidence.solution => every body call
            perTool.evidence.terms => nothing
            perTool.faqQuestions => FAQ from the operator's answers | summaries | the saved page
            perTool.faqQuestions, an answer alone => FAQ from the operator's answers
            """
        },
        {
            "tool", false,
            """
            nicheFraming.taxonomyPath, first level => nothing
            nicheFraming.taxonomyPath, later levels => nothing
            nicheFraming.diagnosisQuestions => summaries | the saved page
            nicheFraming.coreProblem => opening | every body call
            nicheFraming.painPoints => every body call
            nicheFraming.automationToPitch => every body call
            nicheFraming.evidence.problem => every body call
            nicheFraming.evidence.solution => every body call
            nicheFraming.evidence.terms => nothing
            nicheFraming.faqQuestions => nothing
            """
        },
        {
            "email", true,
            """
            primaryIntent => email
            secondaryIntent => email
            buyingStage => email
            audienceSegment => email
            audienceNotes => email
            angle => email
            toneOfVoice => email
            eeatSignals => nothing
            lengthBand => nothing
            paaQuestions => nothing
            paaQuestions, an answer alone => nothing
            blogFaqQuestions => nothing
            blogFaqQuestions, an answer alone => nothing
            briefVersion => nothing
            nicheFraming.taxonomyPath, first level => nothing
            nicheFraming.taxonomyPath, later levels => nothing
            nicheFraming.diagnosisQuestions => nothing
            nicheFraming.coreProblem => email
            nicheFraming.painPoints => email
            nicheFraming.automationToPitch => email
            # A row's failure that no saved paragraph states is a failure the operator named (2026-10-12).
            nicheFraming.evidence.problem => email
            nicheFraming.evidence.solution => nothing
            nicheFraming.evidence.terms => nothing
            nicheFraming.faqQuestions => nothing
            perTool.coreProblem => nothing
            perTool.painPoints => nothing
            perTool.automationToPitch => nothing
            perTool.evidence.problem => nothing
            perTool.evidence.solution => nothing
            perTool.evidence.terms => nothing
            perTool.faqQuestions => nothing
            perTool.faqQuestions, an answer alone => nothing
            """
        },
        {
            "social", true,
            """
            primaryIntent => social post
            secondaryIntent => social post
            buyingStage => social post
            audienceSegment => social post
            audienceNotes => social post
            angle => social post
            toneOfVoice => social post
            eeatSignals => nothing
            lengthBand => nothing
            paaQuestions => nothing
            paaQuestions, an answer alone => nothing
            blogFaqQuestions => nothing
            blogFaqQuestions, an answer alone => nothing
            briefVersion => nothing
            nicheFraming.taxonomyPath, first level => nothing
            nicheFraming.taxonomyPath, later levels => nothing
            nicheFraming.diagnosisQuestions => nothing
            nicheFraming.coreProblem => social post
            nicheFraming.painPoints => social post
            nicheFraming.automationToPitch => social post
            # A row's failure that no saved paragraph states is a failure the operator named (2026-10-12).
            nicheFraming.evidence.problem => social post
            nicheFraming.evidence.solution => nothing
            nicheFraming.evidence.terms => nothing
            nicheFraming.faqQuestions => nothing
            perTool.coreProblem => nothing
            perTool.painPoints => nothing
            perTool.automationToPitch => nothing
            perTool.evidence.problem => nothing
            perTool.evidence.solution => nothing
            perTool.evidence.terms => nothing
            perTool.faqQuestions => nothing
            perTool.faqQuestions, an answer alone => nothing
            """
        },
        {
            "image", true,
            """
            primaryIntent => image prompt
            secondaryIntent => image prompt
            buyingStage => image prompt
            audienceSegment => image prompt
            audienceNotes => image prompt
            angle => image prompt
            toneOfVoice => image prompt
            eeatSignals => image prompt
            lengthBand => image prompt
            paaQuestions => nothing
            paaQuestions, an answer alone => nothing
            blogFaqQuestions => nothing
            blogFaqQuestions, an answer alone => nothing
            briefVersion => nothing
            nicheFraming.taxonomyPath, first level => nothing
            nicheFraming.taxonomyPath, later levels => nothing
            nicheFraming.diagnosisQuestions => nothing
            nicheFraming.coreProblem => nothing
            nicheFraming.painPoints => nothing
            nicheFraming.automationToPitch => nothing
            nicheFraming.evidence.problem => nothing
            nicheFraming.evidence.solution => nothing
            nicheFraming.evidence.terms => nothing
            nicheFraming.faqQuestions => nothing
            perTool.coreProblem => nothing
            perTool.painPoints => nothing
            perTool.automationToPitch => nothing
            perTool.evidence.problem => nothing
            perTool.evidence.solution => nothing
            perTool.evidence.terms => nothing
            perTool.faqQuestions => nothing
            perTool.faqQuestions, an answer alone => nothing
            """
        },
        {
            "searches", true,
            """
            primaryIntent => nothing
            secondaryIntent => nothing
            buyingStage => nothing
            audienceSegment => nothing
            audienceNotes => nothing
            angle => nothing
            toneOfVoice => nothing
            eeatSignals => nothing
            lengthBand => nothing
            paaQuestions => nothing
            paaQuestions, an answer alone => nothing
            blogFaqQuestions => nothing
            blogFaqQuestions, an answer alone => nothing
            briefVersion => nothing
            nicheFraming.taxonomyPath, first level => nothing
            nicheFraming.taxonomyPath, later levels => nothing
            nicheFraming.diagnosisQuestions => nothing
            # A partner with a pain point of its own is searched with that and with the category's
            # problem, both (2026-10-12). Until then the category's was not searched for on its site.
            nicheFraming.coreProblem => searches for evidence
            nicheFraming.painPoints => nothing
            nicheFraming.automationToPitch => nothing
            nicheFraming.evidence.problem => nothing
            nicheFraming.evidence.solution => searches for evidence
            nicheFraming.evidence.terms => searches for evidence
            nicheFraming.faqQuestions => nothing
            perTool.coreProblem => searches for evidence
            perTool.painPoints => nothing
            perTool.automationToPitch => nothing
            perTool.evidence.problem => nothing
            perTool.evidence.solution => searches for evidence
            perTool.evidence.terms => searches for evidence
            perTool.faqQuestions => nothing
            perTool.faqQuestions, an answer alone => nothing
            """
        },
        {
            "searches", false,
            """
            nicheFraming.taxonomyPath, first level => nothing
            nicheFraming.taxonomyPath, later levels => nothing
            nicheFraming.diagnosisQuestions => nothing
            nicheFraming.coreProblem => searches for evidence
            nicheFraming.painPoints => nothing
            nicheFraming.automationToPitch => nothing
            nicheFraming.evidence.problem => nothing
            nicheFraming.evidence.solution => searches for evidence
            nicheFraming.evidence.terms => searches for evidence
            nicheFraming.faqQuestions => nothing
            """
        },
        {
            // "PDF / LinkedIn document" (plans/enable-pdf.md), for a project with two partners. Its two
            // requests are the cover with the post, and the slides.
            "deck", true,
            """
            primaryIntent => cover and post | slides
            secondaryIntent => cover and post | slides
            buyingStage => cover and post | slides
            audienceSegment => cover and post | slides
            audienceNotes => cover and post | slides
            angle => cover and post | slides
            toneOfVoice => cover and post | slides
            eeatSignals => cover and post | slides
            # A deck's size is its slides: the Brief's "Length" is a page's until the form gives the
            # deck a band of its own.
            lengthBand => nothing
            # A deck has no FAQ. The questions and their answers are the pillar's, the blog's and the
            # tool page's.
            paaQuestions => nothing
            paaQuestions, an answer alone => nothing
            blogFaqQuestions => nothing
            blogFaqQuestions, an answer alone => nothing
            briefVersion => nothing
            # The department in the address of each partner's tool page, listed for the first comment.
            nicheFraming.taxonomyPath, first level => the saved page
            nicheFraming.taxonomyPath, later levels => nothing
            # The last slide, built by code. No request is shown the questions.
            nicheFraming.diagnosisQuestions => the saved page
            # The operator's framing for every partner, in both requests (GccNicheFraming.DeckGuidance).
            nicheFraming.coreProblem => cover and post | slides
            nicheFraming.painPoints => cover and post | slides
            nicheFraming.automationToPitch => cover and post | slides
            nicheFraming.evidence.problem => cover and post | slides
            nicheFraming.evidence.solution => cover and post | slides
            nicheFraming.evidence.terms => nothing
            nicheFraming.faqQuestions => nothing
            # What was entered under a partner, in both requests under its name (GccPartnerAnswers):
            # what that partner's slide is built on.
            perTool.coreProblem => cover and post | slides
            perTool.painPoints => cover and post | slides
            perTool.automationToPitch => cover and post | slides
            perTool.evidence.problem => cover and post | slides
            perTool.evidence.solution => cover and post | slides
            perTool.evidence.terms => nothing
            perTool.faqQuestions => nothing
            perTool.faqQuestions, an answer alone => nothing
            """
        },
    };

    /// <summary>
    /// The keyword in a page's title and meta description, through the real writers: the request that
    /// writes them asks for it, and a page saved without it says so in its own lines.
    /// </summary>
    /// <remarks>
    /// Jeff, 2026-10-13: "SEO skill requires, in which case it should be implemented." The stand-in
    /// writer answers every title request "A Title" and "A meta description.", and a tool page's
    /// summaries request "x", so each page here is one that was asked and did not do it. A tool page's
    /// title is put together by code from the product and the keyword, so it lists the meta description
    /// alone.
    /// </remarks>
    [Theory]
    [InlineData("pillar", "pillar", "AI implementation", "title and outline")]
    [InlineData("blog", "blog", "AI implementation", "title and outline")]
    [InlineData("tool", "tool page", "Partner Widget", "summaries")]
    public async Task The_keyword_is_asked_for_in_the_title_and_the_meta_description_and_a_page_without_it_says_so(
        string page, string named, string keyword, string request)
    {
        var calls = await WriteAsync(page, CompleteBrief);

        var asked = Assert.Single(calls, call => call.Label == request).Text;
        Assert.Contains(ContentPromptBuilder.SeoMetaDescriptionInstruction(keyword), asked, StringComparison.Ordinal);
        if (page == "tool")
        {
            Assert.DoesNotContain("TITLE AND THE KEYWORD", asked, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains(ContentPromptBuilder.SeoTitleInstruction(keyword), asked, StringComparison.Ordinal);
        }

        using var saved = System.Text.Json.JsonDocument.Parse(calls[^1].Text);
        var lines = saved.RootElement.GetProperty("warnings").EnumerateArray().Select(line => line.GetString() ?? string.Empty).ToList();
        var metaLine = $"The {named}'s meta description does not contain \"{keyword}\". Its SEO score needs it there.";
        var titleLine = $"The {named}'s title, \"A Title\", does not contain \"{keyword}\". Its SEO score needs it there.";

        if (page == "tool")
        {
            var tool = GccRequiredToolMentions.AnchorLookup(CompleteBrief, PartnerUrls).Values.Single();
            Assert.Contains($"Tool page '{tool}': {metaLine}", lines);
            Assert.DoesNotContain(lines, line => line.Contains("title", StringComparison.Ordinal) && line.Contains("does not contain", StringComparison.Ordinal));
            Assert.Equal($"{tool}: {keyword}", saved.RootElement.GetProperty("title").GetString());
        }
        else
        {
            Assert.Contains(titleLine, lines);
            Assert.Contains(metaLine, lines);
        }
    }

    /// <summary>
    /// Every request that writes words a reader will see is told not to use the em dash (Jeff,
    /// 2026-10-13). Read through the real writers, so a request added later without the rule fails here.
    /// The requests for an image's prompt are left out: what they write is read by an image model.
    /// </summary>
    [Theory]
    [InlineData("pillar")]
    [InlineData("blog")]
    [InlineData("tool")]
    [InlineData("email")]
    [InlineData("social")]
    [InlineData("deck")]
    public async Task Every_request_that_writes_for_a_reader_is_told_not_to_use_the_em_dash(string page)
    {
        var calls = await WriteAsync(page, CompleteBrief);

        var untold = calls
            .Where(call => call.Label is not ("the saved page" or "image prompts" or "image prompt"))
            .Where(call => !call.Text.Contains(GeekAPI.Services.ContentCreator.Guardrail.GccEmDash.Instruction, StringComparison.Ordinal))
            .Select(call => call.Label)
            .Distinct()
            .ToList();

        Assert.True(untold.Count == 0, $"The {page}'s requests not told: {string.Join(", ", untold)}.");
    }

    /// <summary>
    /// Every opening request of a pillar, a blog and a tool page asks for the Question lede and no other
    /// (Jeff, 2026-10-22: "change the Lede to The Question Lede"; geekatyourspot's GEO and SEO audit of
    /// 2026-10-20, H5). Read through the real writers, so an opening request added later without it fails.
    /// </summary>
    [Theory]
    [InlineData("pillar")]
    [InlineData("blog")]
    [InlineData("tool")]
    public async Task Every_opening_request_asks_for_the_question_lede_and_offers_no_other(string page)
    {
        var calls = await WriteAsync(page, CompleteBrief);
        var openings = calls.Where(c => c.Label == "opening").ToList();

        Assert.NotEmpty(openings);
        foreach (var (label, text) in openings)
        {
            Assert.True(text.Contains(ContentPromptBuilder.QuestionLedeInstruction, StringComparison.Ordinal), $"{page} '{label}' is not told the opening is a Question lede.");
            Assert.True(text.Contains("\"ledeType\": \"question\"", StringComparison.Ordinal), $"{page} '{label}' does not fix the ledeType to question.");
            Assert.False(text.Contains("Do NOT start with \"How\"", StringComparison.Ordinal), $"{page} '{label}' still forbids a question.");
        }
    }

    /// <summary>
    /// Every opening and body request of a pillar, a blog and a tool page is told to answer first and to
    /// name no made-up person (geekatyourspot's GEO and SEO audit of 2026-10-20; Jeff's seo-geo, ai-seo and
    /// geo-citability skills). Read through the real writers, so a request added later without them fails.
    /// </summary>
    [Theory]
    [InlineData("pillar")]
    [InlineData("blog")]
    [InlineData("tool")]
    public async Task Every_opening_and_body_request_is_told_to_answer_first_and_to_name_no_made_up_person(string page)
    {
        var calls = await WriteAsync(page, CompleteBrief);
        var writing = calls
            .Where(c => c.Label == "opening" || c.Label == "body call" || c.Label.StartsWith("body call #", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(writing);
        foreach (var (label, text) in writing)
        {
            Assert.True(text.Contains(ContentPromptBuilder.AnswerFirstInstruction, StringComparison.Ordinal), $"{page} '{label}' is not told to answer first.");
            Assert.True(text.Contains(ContentPromptBuilder.MadeUpPeopleInstruction, StringComparison.Ordinal), $"{page} '{label}' is not told to name no made-up person.");
        }
    }

    /// <summary>
    /// No request asks for a story ahead of the answer, or shows a named person no source names. Until
    /// 2026-10-20 the opening was told "Otherwise open with a story", with "Marcus Vance", "Sarah Jenkins"
    /// and "Dr. Aris Thorne" as its examples, and every body call was told to "Carry the story forward".
    /// </summary>
    [Theory]
    [InlineData("pillar")]
    [InlineData("blog")]
    [InlineData("tool")]
    [InlineData("email")]
    [InlineData("social")]
    [InlineData("deck")]
    public async Task No_request_asks_for_a_story_first_or_shows_a_made_up_name(string page)
    {
        var calls = await WriteAsync(page, CompleteBrief);
        string[] gone =
        [
            "open with a story", "Carry the story forward", "Picture your accounts team", "Marcus Vance", "Sarah Jenkins", "Aris Thorne", "pain first",
            // The twelve-type menu the writer picked a story from, gone 2026-10-22 for the Question lede.
            "Pick ONE ledeType", "Lede types (pick", "\"sceneSetting\"", "\"anecdotal\"", "hook/narrative",
        ];

        foreach (var (label, text) in calls)
        foreach (var phrase in gone)
        {
            Assert.False(text.Contains(phrase, StringComparison.OrdinalIgnoreCase), $"{page} '{label}' still says \"{phrase}\".");
        }
    }

    /// <summary>
    /// A number the writer made up, through each page's real writer: the sentence that states it is
    /// deleted, the sentences either side stay, the page is saved, and its lines name the sentence and
    /// the heading it stood under.
    /// </summary>
    /// <remarks>
    /// Jeff, 2026-10-13: "Stop the sentence, not the pillar or blog?" That day's 17:47 UTC run lost its
    /// pillar, 8,053 words, to this sentence: the writer's own 1,000, where three partner pages in
    /// front of it gave Texas's threshold as $500,000 with no count of sales. Until then one such
    /// figure refused the page, and no test wrote a page through to see it lost.
    /// </remarks>
    [Theory]
    [InlineData("pillar", "pillar", "written.")]
    [InlineData("blog", "blog", "written.")]
    [InlineData("tool", "tool page", "the manual pass.")]
    public async Task A_sentence_with_a_number_the_writer_made_up_is_deleted_and_the_page_is_saved(
        string page, string named, string before)
    {
        const string MadeUp = "If you sell 1,000 units of a digital service in Texas, you could miss a nexus trigger entirely.";
        const string After = "The rest stands.";

        var calls = await WriteAsync(
            page, CompleteBrief, firstBody: body =>
            {
                Assert.Single(System.Text.RegularExpressions.Regex.Matches(body, System.Text.RegularExpressions.Regex.Escape(before)));
                return body.Replace(before, $"{before} {MadeUp} {After}", StringComparison.Ordinal);
            });

        using var saved = System.Text.Json.JsonDocument.Parse(calls[^1].Text);
        var body = saved.RootElement.GetProperty("body").GetRawText();
        Assert.DoesNotContain("1,000", body, StringComparison.Ordinal);
        Assert.DoesNotContain("nexus trigger", body, StringComparison.Ordinal);
        Assert.Contains($"{before} {After}", body, StringComparison.Ordinal);

        var lines = saved.RootElement.GetProperty("warnings").EnumerateArray().Select(line => line.GetString() ?? string.Empty).ToList();
        var line = Assert.Single(lines, l => l.Contains("was deleted", StringComparison.Ordinal));
        Assert.Contains(
            $"The {named}: 1 sentence was deleted because it states a number that is in neither the Brief nor the research: "
            + $"\"{MadeUp}\" (1,000), under \"",
            line,
            StringComparison.Ordinal);
        Assert.EndsWith("The rest of the page is as written.", line, StringComparison.Ordinal);
        if (page == "tool") Assert.StartsWith("Tool page '", line, StringComparison.Ordinal);
    }

    /// <summary>
    /// A figure the writer states is cited to the research page it is on, through each page's real
    /// writer: the source's name linked to that page and the date the page was crawled, placed by code
    /// after the sentence (<see cref="GeekAPI.Services.ContentCreator.Guardrail.GccFigureSources"/>).
    /// The writer wrote no link; the saved page carries one, and no line says a figure went uncited.
    /// </summary>
    /// <remarks>
    /// Jeff's ai-seo, seo-content, geo-citability and seo-geo skills ask for a named, linked and dated
    /// source on every statistic; geekatyourspot's GEO audit of 2026-10-20 counted 0 pillars in 23
    /// linking an outside source. Until 2026-10-22 a page cited nothing: the writer was asked to name a
    /// source in words, and no code read whether it had.
    /// </remarks>
    [Theory]
    [InlineData("pillar", "written.")]
    [InlineData("blog", "written.")]
    [InlineData("tool", "the manual pass.")]
    public async Task A_figure_is_cited_to_the_research_page_it_is_on_with_the_source_linked_and_dated(string page, string before)
    {
        const string Stated = "Setup falls from 40 hours to 4 hours with Partner Widget.";
        const string After = "The rest stands.";
        const string PageUrl = "https://partner.test/widget";
        var crawled = new DateTimeOffset(2026, 10, 13, 9, 30, 0, TimeSpan.Zero);
        var research = GccResearchFetchService.Serialize(new GccResearchDocument(
            SerpIndex: null,
            Quoteables:
            [
                new GccQuoteablePage(
                    PageUrl, "Partner Widget", [new HeadingDto(2, "Pricing")],
                    ["Partner Widget is billed monthly.", "Partner Widget cuts setup from 40 hours to 4 hours a month."],
                    RetrievalMode: GccQuoteablePage.RetrievalModeRagChunk,
                    CrawledAtUtc: crawled),
            ]));

        var calls = await WriteAsync(
            page, CompleteBrief, pageResearch: research,
            firstBody: body => body.Replace(before, $"{before} {Stated} {After}", StringComparison.Ordinal));

        using var saved = JsonDocument.Parse(calls[^1].Text);
        var body = JsonSerializer.Deserialize<ContentDocument>(saved.RootElement.GetProperty("body").GetRawText(), GccDocumentJson.Options);
        Assert.NotNull(body);

        var runLists = RunListsOf(body!).ToList();
        var cited = Assert.Single(runLists, runs => string.Concat(runs.Select(run => run.Text)).Contains("Setup falls from 40 hours", StringComparison.Ordinal));
        // The citation calls the page what the project calls the partner: the name the tool page is titled
        // with and the linker links, read from the Brief for the partner's host.
        var productName = GccRequiredToolMentions.AnchorLookup(CompleteBrief, PartnerUrls).Values.Single();
        var citedText = string.Concat(cited.Select(run => run.Text));
        Assert.True(
            citedText.Contains($"Setup falls from 40 hours to 4 hours with Partner Widget ({productName}, as of Oct 13, 2026). The rest stands.", StringComparison.Ordinal),
            citedText);
        var citation = Assert.Single(cited, run => string.Equals(run.Href, PageUrl, StringComparison.Ordinal));
        Assert.Equal(productName, citation.Text);

        // The writer's reply carried no link: every request's reply format is the writer's, and the
        // stand-in writer types none. Only code placed one.
        Assert.DoesNotContain(calls.SkipLast(1), call => call.Text.Contains(PageUrl, StringComparison.Ordinal) && call.Label.Contains("reply", StringComparison.OrdinalIgnoreCase));

        var lines = saved.RootElement.GetProperty("warnings").EnumerateArray().Select(line => line.GetString() ?? string.Empty).ToList();
        Assert.DoesNotContain(lines, line => line.Contains("no page to cite it to", StringComparison.Ordinal));
    }

    /// <summary>Every run list of a document: each text paragraph's runs and each list item's, the opening first.</summary>
    private static IEnumerable<IReadOnlyList<Run>> RunListsOf(ContentDocument document)
    {
        static IEnumerable<IReadOnlyList<Run>> Of(Section section)
        {
            foreach (var paragraph in section.Paragraphs)
            {
                switch (paragraph)
                {
                    case TextParagraph text:
                        yield return text.Runs;
                        break;
                    case ListParagraph list:
                        foreach (var item in list.Items) yield return item;
                        break;
                }
            }

            foreach (var child in section.Children)
            {
                foreach (var runs in Of(child)) yield return runs;
            }
        }

        return Of(document.Lede).Concat(document.Sections.SelectMany(Of));
    }

    /// <summary>
    /// A page whose writer used the em dash is saved without one, through the real writers. The
    /// stand-in writer here uses one in its opening, its body, its standfirst, a tool page's summary,
    /// its email and its post, so every page this audit writes is one that came back with it.
    /// </summary>
    [Theory]
    [InlineData("pillar", "A standfirst, in a sentence.")]
    [InlineData("blog", "A hook paragraph, one that opens the page.")]
    [InlineData("tool", "x, y")]
    [InlineData("email", "A body, short.")]
    [InlineData("social", "A post, short.")]
    public async Task A_page_whose_writer_used_the_em_dash_is_saved_without_one(string page, string savedAs)
    {
        Assert.Contains("\\u2014", LedeJson, StringComparison.Ordinal);
        Assert.Contains("\\u2014", MetadataJson, StringComparison.Ordinal);
        Assert.Contains("\\u2014", ToolMetadataJson, StringComparison.Ordinal);
        Assert.Contains("\\u2014", FirstBatchJson, StringComparison.Ordinal);

        var saved = (await WriteAsync(page, CompleteBrief))[^1].Text;

        // What the writer wrote with one is on the page with a comma in its place.
        Assert.Contains(savedAs, saved, StringComparison.Ordinal);
        // As the character and as JSON writes it when it escapes one.
        Assert.DoesNotContain("\u2014", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u2014", saved, StringComparison.OrdinalIgnoreCase);
    }
}

