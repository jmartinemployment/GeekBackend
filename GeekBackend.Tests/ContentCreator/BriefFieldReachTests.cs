using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.ContentCreator.Partner;
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
    private const string Host = "partner.test";
    private const string ToolFaqA = "Does Partner Widget sync with QuickBooks Online?";
    private const string ToolFaqB = "Does Partner Widget post to Xero?";

    /// <summary>Every field the brief form saves, each with a value.</summary>
    private const string CompleteBrief = """
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
          "paaQuestions": "What does month end cost a distributor?\nWho owns the close calendar?",
          "blogFaqQuestions": "How long does a close take by hand?",
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
                "faqQuestions": "Does Partner Widget sync with QuickBooks Online?"
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
        new("paaQuestions", b => b["paaQuestions"] = "What does a late close cost a wholesaler?\nWho signs off the reconciliations?"),
        new("blogFaqQuestions", b => b["blogFaqQuestions"] = "How many people does a close take?"),
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
        new("perTool.faqQuestions", b => Tool(b)["faqQuestions"] = ToolFaqB),
    ];

    /// <summary>The brief with the tool's own framing taken out, so the tool inherits the category's.</summary>
    private static string WithoutToolFraming(string briefJson)
    {
        var brief = (JsonObject)JsonNode.Parse(briefJson)!;
        ((JsonObject)Framing(brief)["perTool"]!).Remove(Host);
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
        """{"ledeType":"directAddress","heading":"Reclaiming The Hours You Lose","paragraphs":[{"type":"text","runs":[{"text":"A hook paragraph that opens the page."}]}]}""";

    private const string MetadataJson =
        """{"title":"A Title","summary":"A standfirst.","metaDescription":"A meta description.","keywords":["k"],"sectionOutline":["A"]}""";

    private const string ToolMetadataJson =
        """{"departmentListExcerpt":"x","summary":"x","mainSummary":"x","heroSummary":"x","homeSummary":"x","blogSummary":"x","toolPageExcerpt":"x","advertisingSummary":"x","metaDescription":"x"}""";

    private const string FirstBatchJson =
        """{"sections":[{"tag":"h2","heading":"A","paragraphs":[{"type":"text","runs":[{"text":"Body."}]}],"href":null,"children":[],"provenance":"plan"}]}""";

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
    private sealed class Recorder(string page) : IContentGenerationProvider
    {
        private readonly List<(string Label, string Text)> calls = [];
        private int bodyCalls;

        public LlmProviderType ProviderType => LlmProviderType.OpenAi;

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
            if (request.JsonSchemaName == "sections")
            {
                var n = bodyCalls++;
                var body = page == "tool"
                    ? ToolBodyBatches[Math.Min(n, ToolBodyBatches.Length - 1)]
                    : n < 1 ? FirstBatchJson : ScriptedBody.PlannedBatch(n);
                return ("body call", body);
            }

            if (request.JsonSchemaName == "section")
            {
                if (page == "tool" && !asked.Contains("=== PARTNER EVIDENCE", StringComparison.Ordinal))
                    return ("FAQ from the partner's own FAQ", Faq("Frequently Asked Questions", ["Is it secure?"]));

                var heading = page == "pillar" ? "People Also Ask" : "Frequently Asked Questions";
                var label = page switch
                {
                    "pillar" => "People Also Ask",
                    "tool" => "FAQ from the operator's questions",
                    _ => "FAQ",
                };
                return (label, Faq(heading, QuestionsIn(asked)));
            }

            if (asked.Contains("image-generation prompts", StringComparison.Ordinal))
                return ("image prompts", ScriptedBody.ImagePrompts());
            if (asked.Contains("\"negativePrompt\"", StringComparison.Ordinal) || page == "image")
                return ("image prompt", """{"prompt":"A ledger on a desk.","style":"photo","negativePrompt":"text","aspectRatio":"16:9","imageModel":"m","stylePreset":"p","notes":"n"}""");
            if (page == "email")
                return ("email", """{"subject":"A subject","body":"A body.","ctaLabel":"Read the guide"}""");
            if (page == "social")
                return ("social post", """{"text":"A post."}""");
            if (asked.Contains("sectionOutline", StringComparison.Ordinal))
                return ("title and outline", MetadataJson);
            if (asked.Contains("departmentListExcerpt", StringComparison.Ordinal))
                return ("summaries", ToolMetadataJson);
            return ("opening", page == "pillar" ? LedeAndIntroJson : LedeJson);
        }

        private static IReadOnlyList<string> QuestionsIn(string asked) =>
        [
            .. asked.Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.StartsWith("- Q", StringComparison.Ordinal) && l.Contains(':', StringComparison.Ordinal))
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

    private sealed class FakeProviderFactory(IContentGenerationProvider provider) : IContentProviderFactory
    {
        public IContentGenerationProvider Get(LlmProviderType providerType) => provider;
        public IContentGenerationProvider GetDefault() => provider;
    }

    // ---- the pages ----------------------------------------------------------------------------------

    private static readonly Guid CreateId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ProjectId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTime At = new(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc);
    private static readonly string[] PartnerUrls = ["https://partner.test/"];

    private static GccCreateDto Create(string type, string topic, string briefJson, string? researchJson, Guid? projectId) => new(
        Id: CreateId, ClientId: CreateId, OwnerUserId: CreateId,
        StartingContentType: type, Topic: topic, Notes: null,
        // A site crawl id, which the dispatcher requires before it writes a tool page or an image prompt.
        ProjectSiteRunId: ProjectId, SiteSectionJson: null, BriefJson: briefJson, ResearchJson: researchJson,
        Status: "draft", CreatedAtUtc: At, UpdatedAtUtc: At, ProjectId: projectId);

    private static GccGenerateService Build(
        IContentGenerationProvider provider, GccPartnerExtractionService extraction, GccProjectDto? project) => new(
        new ContentPromptBuilder(),
        TestContentTypePrompts.Registry(),
        new FakeProviderFactory(provider),
        new SoftwareApplicationSchemaBuilder(),
        new BlogPostingSchemaBuilder(),
        new ArticleSchemaBuilder(new SoftwareApplicationSchemaBuilder()),
        Options.Create(new CompanyProfileOptions()),
        NullLogger<GccGenerateService>.Instance,
        GccCompetitorAnalysisResolverTests.Build(
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

    /// <summary>One partner page, and what a search of the partner's crawl found for each FAQ question.</summary>
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
            ])
        {
            FaqEvidence =
            [
                new GccFaqEvidence(Host, ToolFaqA, [new GccQuoteablePage(
                    "https://partner.test/integrations", "Integrations", [], ["Every Partner Widget payment syncs to QuickBooks Online."])]),
                new GccFaqEvidence(Host, ToolFaqB, [new GccQuoteablePage(
                    "https://partner.test/xero", "Xero", [], ["Partner Widget posts each bill to Xero."])]),
            ],
        });

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
    private static async Task<IReadOnlyList<(string Label, string Text)>> WriteAsync(string page, string briefJson)
    {
        var provider = new Recorder(page);
        var plain = Build(provider, GccPartnerExtractionFakes.NeverInvoked(new FakeProviderFactory(provider)), null);
        string saved;
        switch (page)
        {
            case "pillar":
                saved = await plain.GeneratePillarBodyAsync(
                    Create("pillar", "AI implementation", briefJson, null, null), null, ContentGeneratorProvider.OpenAi, null, CancellationToken.None);
                break;
            case "blog":
                saved = await plain.GenerateBlogBodyAsync(
                    Create("blog", "AI implementation", briefJson, null, null), null, ContentGeneratorProvider.OpenAi, null, CancellationToken.None);
                break;
            case "email":
                saved = await plain.GenerateEmailAsync(
                    Create("email", "AI implementation", briefJson, null, null), null, ContentGeneratorProvider.OpenAi, null, CancellationToken.None);
                break;
            case "social":
                saved = await plain.GenerateSocialPostAsync(
                    Create("linkedin", "AI implementation", briefJson, null, null), "linkedin", null, ContentGeneratorProvider.OpenAi, null, CancellationToken.None);
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
                var project = new GccProjectDto(
                    ProjectId, CreateId, "Acme", null, null, "active", null, null, null,
                    PartnerUrls, [], new DateOnly(2026, 10, 10), null, null, null, null, null, At, At);
                var service = Build(provider, GccPartnerExtractionFakes.Scripted(new FakeProviderFactory(provider), extraction), project);
                var toolName = GccRequiredToolMentions.AnchorLookup(briefJson, PartnerUrls).Values.Single();
                // What the fan-out does for each partner: the tool branch of the one dispatcher, told the product.
                saved = await service.GenerateStartingContentAsync(
                    Create("tool", "Partner Widget", briefJson, ToolResearch(), ProjectId) with { Notes = "Operator notes." },
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

        return
        [
            ("searches for evidence", Lines(GccGroundingResolver.PartnerQuestions(create, Host))),
            ("searches for FAQ questions", Lines(GccGroundingResolver.FaqQuestions(create, Host))),
        ];
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
    [Theory]
    [InlineData("pillar")]
    [InlineData("blog")]
    [InlineData("tool")]
    [InlineData("email")]
    [InlineData("social")]
    [InlineData("image")]
    [InlineData("searches")]
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
    /// this table made with the fix, and so that it cannot reopen unseen. The whole list, with the fields
    /// kept from a call on a decision, is plans/brief-field-audit.md.
    /// <para>
    /// Closed the same day, each a changed line below: the pillar's opening now carries the operator's
    /// whole framing, the tool page's two FAQ calls and its summaries call carry the reader and the
    /// voice, and the email and the social post carry the framing.
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
            eeatSignals => opening | People Also Ask | every body call
            lengthBand => opening | People Also Ask | every body call
            paaQuestions => People Also Ask | every body call | the saved page
            blogFaqQuestions => nothing
            briefVersion => nothing
            nicheFraming.taxonomyPath, first level => the saved page
            # GAP: only the first level of the path is read, as the department in the page's address.
            nicheFraming.taxonomyPath, later levels => nothing
            nicheFraming.diagnosisQuestions => the saved page
            nicheFraming.coreProblem => opening
            nicheFraming.painPoints => opening | body call
            nicheFraming.automationToPitch => opening | body call #2
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
            eeatSignals => opening | FAQ | every body call
            lengthBand => opening | FAQ | every body call
            paaQuestions => every body call
            blogFaqQuestions => FAQ | the saved page
            briefVersion => nothing
            nicheFraming.taxonomyPath, first level => the saved page
            nicheFraming.taxonomyPath, later levels => nothing
            nicheFraming.diagnosisQuestions => the saved page
            nicheFraming.coreProblem => body call
            nicheFraming.painPoints => body call
            nicheFraming.automationToPitch => body call | body call #2
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
            """
        },
        {
            "tool", true,
            """
            primaryIntent => opening | every body call | FAQ from the partner's own FAQ | FAQ from the operator's questions | summaries
            secondaryIntent => opening | every body call | FAQ from the partner's own FAQ | FAQ from the operator's questions | summaries
            buyingStage => opening | every body call | FAQ from the partner's own FAQ | FAQ from the operator's questions | summaries
            audienceSegment => opening | every body call | FAQ from the partner's own FAQ | FAQ from the operator's questions | summaries
            audienceNotes => opening | every body call | FAQ from the partner's own FAQ | FAQ from the operator's questions | summaries
            angle => opening | every body call
            toneOfVoice => opening | every body call | FAQ from the partner's own FAQ | FAQ from the operator's questions | summaries
            eeatSignals => opening | every body call | FAQ from the partner's own FAQ | FAQ from the operator's questions | summaries
            lengthBand => opening | every body call | FAQ from the partner's own FAQ | FAQ from the operator's questions | summaries
            paaQuestions => nothing
            blogFaqQuestions => nothing
            briefVersion => nothing
            nicheFraming.taxonomyPath, first level => nothing
            nicheFraming.taxonomyPath, later levels => nothing
            nicheFraming.diagnosisQuestions => summaries | the saved page
            nicheFraming.coreProblem => nothing
            nicheFraming.painPoints => body call
            nicheFraming.automationToPitch => nothing
            nicheFraming.evidence.problem => nothing
            nicheFraming.evidence.solution => nothing
            nicheFraming.evidence.terms => nothing
            nicheFraming.faqQuestions => nothing
            perTool.coreProblem => body call
            perTool.painPoints => body call
            perTool.automationToPitch => body call
            perTool.evidence.problem => nothing
            perTool.evidence.solution => nothing
            perTool.evidence.terms => nothing
            perTool.faqQuestions => FAQ from the operator's questions | summaries | the saved page
            """
        },
        {
            "tool", false,
            """
            nicheFraming.taxonomyPath, first level => nothing
            nicheFraming.taxonomyPath, later levels => nothing
            nicheFraming.diagnosisQuestions => summaries | the saved page
            nicheFraming.coreProblem => body call
            nicheFraming.painPoints => body call
            nicheFraming.automationToPitch => body call
            nicheFraming.evidence.problem => nothing
            nicheFraming.evidence.solution => nothing
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
            blogFaqQuestions => nothing
            briefVersion => nothing
            nicheFraming.taxonomyPath, first level => nothing
            nicheFraming.taxonomyPath, later levels => nothing
            nicheFraming.diagnosisQuestions => nothing
            nicheFraming.coreProblem => email
            nicheFraming.painPoints => email
            nicheFraming.automationToPitch => email
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
            blogFaqQuestions => nothing
            briefVersion => nothing
            nicheFraming.taxonomyPath, first level => nothing
            nicheFraming.taxonomyPath, later levels => nothing
            nicheFraming.diagnosisQuestions => nothing
            nicheFraming.coreProblem => social post
            nicheFraming.painPoints => social post
            nicheFraming.automationToPitch => social post
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
            blogFaqQuestions => nothing
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
            blogFaqQuestions => nothing
            briefVersion => nothing
            nicheFraming.taxonomyPath, first level => nothing
            nicheFraming.taxonomyPath, later levels => nothing
            nicheFraming.diagnosisQuestions => nothing
            nicheFraming.coreProblem => nothing
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
            perTool.faqQuestions => searches for FAQ questions
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
    };
}
