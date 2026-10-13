using System.Net;
using System.Text;
using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.GeekCrawler;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Providers;
using GeekApplication.Interfaces.ContentWriterV3;
using GeekApplication.Models.ContentCreator;
using GeekApplication.Models.GeekCrawler;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using ApiProjectsController = GeekAPI.Controllers.ContentCreator.GccProjectsController;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// "PDF / LinkedIn document" as a Generate type (<c>plans/enable-pdf.md</c>, step 4): the gate is
/// open, and a whole run is driven from the route's rules to the one save.
/// </summary>
/// <remarks>
/// A run goes through the real coordinator, the real searches' resolver over a stand-in index, the
/// real writer over a stand-in model, and a repository that holds what it is given. Until this test a
/// deck had only been written with its research handed to the writer, which is how nothing had noticed
/// that a deck asked for alone was searched for nothing.
/// </remarks>
public sealed class LinkedInDocumentRunTests
{
    private const string Deck = "linkedin-document";
    private const string Keyword = "AI implementation";
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static readonly Guid ProjectId = BriefFieldReachTests.ProjectId;
    private static readonly Guid SiteRun = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid PartnerRun = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid SecondRun = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private const string PartnerPassage = "Partner matches every bank line to its ledger entry before the close begins.";
    private const string SecondPassage = "Second routes each reconciliation to the reviewer who owns that account.";

    // ---- the gate ----------------------------------------------------------------------------------

    [Fact]
    public void The_type_is_no_longer_refused_as_disabled_and_the_types_with_no_writer_still_are()
    {
        Assert.False(GccGenerateService.IsContentTypeDisabledPendingImplementation(Deck));
        Assert.False(GccGenerateService.IsContentTypeDisabledPendingImplementation("linkedinDocument"));
        Assert.Null(GccGenerationCoordinator.ValidateRequestedTypes([Deck]));
        Assert.Null(GccGenerationCoordinator.ValidateRequestedTypes(["pillar", Deck]));

        Assert.True(GccGenerateService.IsContentTypeDisabledPendingImplementation("whitepaper"));
        Assert.StartsWith("Refused: 'whitepaper'", GccGenerationCoordinator.ValidateRequestedTypes([Deck, "whitepaper"]));
    }

    [Theory]
    [InlineData("linkedin-document", true)]
    [InlineData("linkedinDocument", true)]
    [InlineData(" LinkedIn-Document ", true)]
    [InlineData("linkedin", false)]
    [InlineData("pillar", false)]
    [InlineData(null, false)]
    public void The_decks_type_is_read_in_one_place_however_it_is_spelled(string? type, bool isTheDecks) =>
        Assert.Equal(isTheDecks, GccLinkedInDocument.IsType(type));

    /// <summary>
    /// A type that is not in the resolver's table is searched for nothing and written from the Brief
    /// alone, with nothing saying so. The deck reads what the Pillar reads.
    /// </summary>
    [Fact]
    public void A_deck_is_searched_for_exactly_what_a_pillar_is()
    {
        Assert.NotEmpty(GccGroundingResolver.RetrieveFor(Deck));
        Assert.Equal(GccGroundingResolver.RetrieveFor("pillar"), GccGroundingResolver.RetrieveFor(Deck));
        Assert.Equal(GccGroundingResolver.RetrieveFor(Deck), GccGroundingResolver.RetrieveFor("linkedinDocument"));
        // Like the Pillar, it quotes nothing, so no evidence is required of it for a citation.
        Assert.Empty(GccGroundingResolver.RequiredFor(Deck));
    }

    // ---- a whole run ---------------------------------------------------------------------------------

    [Fact]
    public async Task A_deck_ticked_alone_is_searched_for_written_in_two_requests_and_saved_as_one_page_of_the_project()
    {
        var model = new BriefFieldReachTests.Recorder("deck");
        var repository = new ProjectRepository { RequestsSoFar = () => model.Calls.Count };
        var index = new Index();

        await RunAsync(model, repository, [Deck], index, Project(BriefFieldReachTests.DeckPartnerUrls));

        // Searched before it was written: the site, and each partner's own crawl.
        Assert.Contains((SiteRun, CrawlTypes.ProjectSite), index.Asked);
        Assert.Contains((PartnerRun, CrawlTypes.Partner), index.Asked);
        Assert.Contains((SecondRun, CrawlTypes.Partner), index.Asked);

        // The old deck goes first, before a request is sent, and no other type's page with it.
        Assert.Equal("?type=linkedin-document", repository.PagesDeleteQuery);
        Assert.Equal(0, repository.RequestsBeforeTheDelete);

        // Two requests, and each carries what the searches brought back for both partners.
        Assert.Equal(["cover and post", "slides"], model.Calls.Select(call => call.Label));
        Assert.All(model.Calls, call =>
        {
            Assert.Contains(PartnerPassage, call.Text, StringComparison.Ordinal);
            Assert.Contains(SecondPassage, call.Text, StringComparison.Ordinal);
        });

        // One save, of one page: the deck, under its own type, named for the keyword like the Pillar.
        var piece = Assert.Single(Assert.Single(repository.Saves).Pieces);
        Assert.Equal(Deck, piece.Type);
        Assert.Equal(Keyword, piece.Name);
        Assert.Null(piece.ParentPageId);

        // What was saved reads back as a deck and sets as its PDF.
        var deck = GccLinkedInDocument.ReadSaved(piece.BodyDocumentJson, Web);
        Assert.NotNull(deck);
        Assert.Equal(
            [GccDeckSlideKind.Cover, GccDeckSlideKind.Problem, GccDeckSlideKind.Partner, GccDeckSlideKind.Partner,
             GccDeckSlideKind.Approach, GccDeckSlideKind.Next],
            deck.Slides.Select(slide => slide.Kind));
        Assert.Equal(["Partner", "Second"], deck.Slides.Select(slide => slide.Partner).OfType<string>());
        Assert.False(string.IsNullOrWhiteSpace(deck.Post));
    }

    [Fact]
    public async Task A_deck_ticked_beside_a_pillar_is_written_in_the_same_run_and_both_are_saved_in_one_write()
    {
        var model = new PillarAndDeck();
        var repository = new ProjectRepository { RequestsSoFar = () => model.Requests };

        await RunAsync(model, repository, ["pillar", Deck], new Index(), Project(BriefFieldReachTests.DeckPartnerUrls));

        Assert.Equal("?type=pillar&type=linkedin-document", repository.PagesDeleteQuery);
        Assert.Equal(0, repository.RequestsBeforeTheDelete);

        var saved = Assert.Single(repository.Saves).Pieces;
        Assert.Equal(["linkedin-document", "pillar"], saved.Select(piece => piece.Type).Order(StringComparer.Ordinal));
        Assert.NotNull(GccLinkedInDocument.ReadSaved(saved.Single(piece => piece.Type == Deck).BodyDocumentJson, Web));
        // The pillar is a page and not a deck, whatever its sections number.
        Assert.Null(GccLinkedInDocument.ReadSaved(saved.Single(piece => piece.Type == "pillar").BodyDocumentJson, Web));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(9)]
    public async Task A_project_with_too_few_or_too_many_partners_is_refused_before_anything_is_deleted_searched_or_sent(int partners)
    {
        var model = new BriefFieldReachTests.Recorder("deck");
        var repository = new ProjectRepository { RequestsSoFar = () => model.Calls.Count };
        var index = new Index();
        IReadOnlyList<string> urls = [.. Enumerable.Range(1, partners).Select(n => $"https://partner{n}.test/")];

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RunAsync(model, repository, ["pillar", Deck], index, Project(urls)));

        Assert.StartsWith("Refused: a PDF / LinkedIn document has one slide for each partner tool", refused.Message);
        Assert.Contains($"this project has {partners} (", refused.Message, StringComparison.Ordinal);
        // The pillar ticked beside it is not deleted for the deck's refusal either.
        Assert.Empty(repository.Calls);
        Assert.Empty(index.Asked);
        Assert.Empty(model.Calls);
    }

    [Fact]
    public async Task A_partner_whose_crawl_returns_nothing_refuses_the_deck_by_name_before_a_request_is_sent()
    {
        var model = new BriefFieldReachTests.Recorder("deck");
        var repository = new ProjectRepository { RequestsSoFar = () => model.Calls.Count };
        var index = new Index(empty: SecondRun);

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RunAsync(model, repository, [Deck], index, Project(BriefFieldReachTests.DeckPartnerUrls)));

        Assert.StartsWith("Refused: the PDF / LinkedIn document must name every declared partner, and ", refused.Message);
        Assert.Contains("second", refused.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://partner.test", refused.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("returned no passage for this topic", refused.Message, StringComparison.Ordinal);
        Assert.Empty(model.Calls);
        Assert.Empty(repository.Saves);
    }

    // ---- the route -----------------------------------------------------------------------------------

    [Theory]
    [InlineData(0, "this project has none")]
    [InlineData(1, "this project has 1 (")]
    [InlineData(9, "this project has 9 (")]
    public async Task The_route_answers_400_for_a_project_with_too_few_or_too_many_partners_and_starts_nothing(int partners, string says)
    {
        var project = GccProjectGenerateRouteTests.Project(GccProjectGenerateRouteTests.CompleteBrief, briefVersion: 1) with
        {
            PartnerUrls = [.. Enumerable.Range(1, partners).Select(n => $"https://partner{n}.test/")],
        };
        var repo = new GccProjectGenerateRouteTests.Repo(project, backing: null);

        var result = await GccProjectGenerateRouteTests.Controller(repo).Generate(
            project.Id, new ApiProjectsController.GenerateRequest(["pillar", Deck], "OpenAi"), CancellationToken.None);

        var refused = Assert.IsType<BadRequestObjectResult>(result);
        Assert.StartsWith("Refused: a PDF / LinkedIn document has one slide for each partner tool", (string)refused.Value!);
        Assert.Contains(says, (string)refused.Value!, StringComparison.Ordinal);
        Assert.Empty(repo.Starts);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(8)]
    public async Task The_route_starts_a_run_for_a_deck_on_a_project_with_two_to_eight_partners(int partners)
    {
        var project = GccProjectGenerateRouteTests.Project(GccProjectGenerateRouteTests.CompleteBrief, briefVersion: 1) with
        {
            PartnerUrls = [.. Enumerable.Range(1, partners).Select(n => $"https://partner{n}.test/")],
        };
        var repo = new GccProjectGenerateRouteTests.Repo(project, backing: null);

        var result = await GccProjectGenerateRouteTests.Controller(repo).Generate(
            project.Id, new ApiProjectsController.GenerateRequest([Deck], "OpenAi"), CancellationToken.None);

        Assert.IsType<AcceptedResult>(result);
        Assert.Equal([Deck], Assert.Single(repo.Starts).RequestedTypes);
    }

    // ---- fixtures ------------------------------------------------------------------------------------

    private static GccProjectDto Project(IReadOnlyList<string> partnerUrls) => new(
        ProjectId, BriefFieldReachTests.CreateId, "Acme", null, null, "active", "https://acme.test", SiteRun, null,
        [.. partnerUrls], [], new DateOnly(2026, 10, 10), null, null, null, null, null, BriefFieldReachTests.At, BriefFieldReachTests.At);

    private static Task<object> RunAsync(
        IContentGenerationProvider model, ProjectRepository repository, IReadOnlyList<string> types, Index index, GccProjectDto project)
    {
        var service = BriefFieldReachTests.Build(
            model, GccPartnerExtractionFakes.NeverInvoked(new BriefFieldReachTests.FakeProviderFactory(model)), project);
        var pages = new GccCompetitorAnalysisResolverTests.FakePages();
        var grounding = new GccGroundingResolver(
            new GccCompetitorAnalysisResolverTests.FakeProjects(project), index.Rag(project), new GccTypedPassageReader(pages),
            new GccPublisherPositionsReader(pages, NullLogger<GccPublisherPositionsReader>.Instance),
            NullLogger<GccGroundingResolver>.Instance);
        return new GccGenerationCoordinator(grounding, NullLogger<GccGenerationCoordinator>.Instance).RunGenerateAsync(
            new HttpGccRepository(
                new HttpClient(repository) { BaseAddress = new Uri("http://repo.test/") }, NullLogger<HttpGccRepository>.Instance),
            service,
            BriefFieldReachTests.Create("pillar", Keyword, BriefFieldReachTests.CompleteBrief, null, ProjectId),
            section: null,
            ContentGeneratorProvider.OpenAi,
            types,
            mustMentionBlock: null,
            CancellationToken.None);
    }

    /// <summary>
    /// The index of crawls: the project's site and each partner's, each answering a search with a page
    /// of its own. A run named in <paramref name="empty"/> is indexed and answers with nothing.
    /// </summary>
    private sealed class Index(Guid? empty = null)
    {
        private readonly List<(Guid Run, string? CrawlType)> asked = [];

        /// <summary>Every search made, as the crawl it was made of and the kind of crawl it was asked as.</summary>
        public IReadOnlyList<(Guid Run, string? CrawlType)> Asked
        {
            get { lock (asked) return [.. asked]; }
        }

        public GccCompetitorAnalysisResolverTests.FakeRag Rag(GccProjectDto project)
        {
            // The first two partners are the two the stand-in model writes about; any more get a run of their own.
            var runs = project.PartnerUrls
                .Select((url, i) => (Url: url, Run: i switch { 0 => PartnerRun, 1 => SecondRun, _ => Guid.NewGuid() }))
                .ToList();
            return new GccCompetitorAnalysisResolverTests.FakeRag(
                hosts: [.. runs.Select(partner =>
                    new GeekCrawlerRagHostIndex(partner.Url, new Uri(partner.Url).Host, true, partner.Run.ToString()))],
                query: (run, crawlType) =>
                {
                    lock (asked) asked.Add((run, crawlType));
                    return new GeekCrawlerRagQueryResult { RunId = run, Pages = run == empty ? [] : [PageOf(run)] };
                });
        }

        private static GccQuoteablePage PageOf(Guid run) =>
            run == PartnerRun
                ? Page("https://partner.test/reconciliation", "Partner reconciliation", PartnerPassage)
            : run == SecondRun
                ? Page("https://second.test/review", "Second review", SecondPassage)
            : run == SiteRun
                ? Page("https://acme.test/close-guide", "Our close guide", "We published this guide to the close last quarter.")
                : Page($"https://run-{run:N}.test/a", "A partner page", "This partner reconciles accounts.");

        private static GccQuoteablePage Page(string url, string title, string passage) =>
            new(url, title, [new HeadingDto(2, title)], [passage], RetrievalMode: GccQuoteablePage.RetrievalModeRagChunk);
    }

    /// <summary>The model for a run that writes a pillar and a deck at once: each request goes to the
    /// scripted writer of the page it is for, told apart by the reply it asks for.</summary>
    private sealed class PillarAndDeck : IContentGenerationProvider
    {
        private readonly BriefFieldReachTests.Recorder pillar = new("pillar");
        private readonly BriefFieldReachTests.Recorder deck = new("deck");

        public int Requests => pillar.Calls.Count + deck.Calls.Count;

        public LlmProviderType ProviderType => LlmProviderType.OpenAi;

        public string ModelFor(ChatCompletionRequest request) => "gpt-4.1";

        public Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default) =>
            (request.JsonSchemaName is GccLinkedInDocument.CoverAndPostSchemaName or GccLinkedInDocument.SlidesSchemaName
                ? deck
                : pillar).CompleteAsync(request, cancellationToken);
    }

    /// <summary>
    /// GeekRepository, as the routes a Generate calls: the project's pages listed, its pages of some
    /// types deleted, and a run's pieces saved.
    /// </summary>
    private sealed class ProjectRepository : HttpMessageHandler
    {
        public Func<int> RequestsSoFar { get; init; } = () => 0;
        public List<string> Calls { get; } = [];
        public List<SaveGccGeneratedPiecesCommand> Saves { get; } = [];
        public string? PagesDeleteQuery { get; private set; }
        public int? RequestsBeforeTheDelete { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var project = $"/repo/content-creator/projects/{ProjectId}";
            if (request.Method == HttpMethod.Get && path == $"{project}/artifacts")
            {
                Calls.Add("list");
                return Json(Array.Empty<GccArtifactDto>());
            }

            if (request.Method == HttpMethod.Delete && path == $"{project}/pages")
            {
                Calls.Add("delete pages");
                PagesDeleteQuery = request.RequestUri.Query;
                RequestsBeforeTheDelete ??= RequestsSoFar();
                return Json(new GccPagesDeleteResult(false, 0, 0));
            }

            if (request.Method == HttpMethod.Post && path == $"{project}/generated")
            {
                Calls.Add("save");
                var command = JsonSerializer.Deserialize<SaveGccGeneratedPiecesCommand>(
                    await request.Content!.ReadAsStringAsync(ct), Web)!;
                Saves.Add(command);
                return Json(GccGeneratedPiecesSaveResult.Written([.. command.Pieces.Select(piece =>
                {
                    var saved = new GccArtifactDto(
                        Guid.NewGuid(), BriefFieldReachTests.CreateId, ProjectId, piece.ParentPageId, piece.Type, piece.Name, "draft",
                        BriefFieldReachTests.At, BriefFieldReachTests.At, 1, BriefFieldReachTests.At);
                    return new GccSavedPieceDto(
                        saved,
                        new GccArtifactVersionDto(
                            Guid.NewGuid(), saved.Id, 1, piece.BodyDocumentJson, piece.MetadataJson, 1, BriefFieldReachTests.At),
                        NewPage: true);
                })]));
            }

            return new HttpResponseMessage(HttpStatusCode.NotImplemented);
        }

        private static HttpResponseMessage Json(object value) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(value, Web), Encoding.UTF8, "application/json"),
            };
    }
}
