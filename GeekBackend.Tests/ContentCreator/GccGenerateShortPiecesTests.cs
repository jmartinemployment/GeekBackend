using System.Net;
using System.Text;
using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.GeekCrawler;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.Providers;
using GeekApplication.Interfaces.ContentWriterV3;
using GeekApplication.Models.ContentCreator;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// A Generate asked for an email, a social post or an ad (Jeff, 2026-10-13: "Add my Email, Social, and
/// Ads, back to generate"): the pages are written and saved first through their real writers, then
/// each piece is written from the project's Pillar and saved under it.
/// </summary>
/// <remarks>
/// The repository here is a stand-in that holds what it is given, so a page saved by the first write is
/// the page read back for the second. What GeekRepository itself does with each call is tested against
/// the real repository in <see cref="GccProjectPageRepositoryTests"/>.
/// </remarks>
public sealed class GccGenerateShortPiecesTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static readonly Guid ProjectId = BriefFieldReachTests.ProjectId;
    private static readonly Guid SiteRun = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private const string Keyword = "AI implementation";

    private const string Email =
        """{"subject":"The close nobody else can read","body":"The close runs on spreadsheets nobody else can read.","ctaLabel":"Read the guide"}""";

    private const string Post = """{"text":"The close runs on spreadsheets nobody else can read."}""";
    private const string PostWithItsOwnFigure = """{"text":"A close calendar gets the books shut 75% faster."}""";

    private const string NotWrittenFromThePillar =
        "Refused: the email is written from this project's Pillar, which this run did not write. Nothing was written for it.";

    // ---- the page the pieces are written from, decided ----------------------------------------------

    [Fact]
    public void The_pieces_are_written_from_the_pillar_then_the_blog_this_runs_before_the_one_saved()
    {
        var pillar = Held("pillar");
        var blog = Held("blog");
        var toolPage = Held("tool", "Ramp");
        // A pillar under another page is a piece, not one of the project's pages.
        var pieceNamedPillar = Held("pillar") with { ParentArtifactId = toolPage.Id };

        Assert.Equal(new GccGenerateSource("pillar", true), GccRepurpose.GenerateSourceOf(["pillar", "blog", "social"], [pillar, blog]).Source);
        Assert.Equal(new GccGenerateSource("pillar", true), GccRepurpose.GenerateSourceOf(["Pillar", "social"], []).Source);
        // "Pillar" is not asked for and the project has one: the saved one, though "Blog" is asked for.
        Assert.Equal(new GccGenerateSource("pillar", false), GccRepurpose.GenerateSourceOf(["blog", "social"], [blog, pillar]).Source);
        Assert.Equal(new GccGenerateSource("blog", true), GccRepurpose.GenerateSourceOf(["blog", "ads"], [toolPage]).Source);
        Assert.Equal(new GccGenerateSource("blog", false), GccRepurpose.GenerateSourceOf(["tool", "email-cold-outreach"], [blog, toolPage]).Source);
        Assert.Equal(new GccGenerateSource("blog", false), GccRepurpose.GenerateSourceOf(["ads"], [blog, pieceNamedPillar]).Source);

        // A run asked for no short piece has nothing to decide, and is told nothing.
        Assert.Equal((null, null), GccRepurpose.GenerateSourceOf(["pillar", "blog", "tool"], []));

        var (none, refusal) = GccRepurpose.GenerateSourceOf(["tool", "ads"], [toolPage, pieceNamedPillar]);
        Assert.Null(none);
        Assert.Equal(
            "Refused: 'ads' is written from this project's Pillar, or from its Blog when it has no Pillar, and it has neither. "
                + "Tick \"Pillar\" as well, or generate one first. Nothing was generated.",
            refusal);
        Assert.Equal("Pillar", new GccGenerateSource("pillar", true).Label);
        Assert.Equal("Blog", new GccGenerateSource("blog", false).Label);
    }

    [Fact]
    public void A_page_nobody_approved_is_written_from()
    {
        var draft = Held("pillar");

        var forGenerate = GccRepurpose.SourceOf(draft, PageBody("A guide", "The close runs on spreadsheets."));

        Assert.Equal("draft", draft.Status);
        Assert.Null(forGenerate.Refusal);
        Assert.Equal((draft.Id, "pillar", Keyword, "A guide"), (forGenerate.Source!.PageId, forGenerate.Source.Type, forGenerate.Source.Name, forGenerate.Source.Title));
        Assert.Contains("The close runs on spreadsheets.", forGenerate.Source.Text, StringComparison.Ordinal);

        // The writer is told what the page is, not that somebody approved it.
        Assert.Contains(
            "A pillar page of this publisher's: \"A guide\".",
            GccRepurpose.SourceBlock(forGenerate.Source, "email"), StringComparison.Ordinal);
    }

    // ---- one part of a run settles without failing the run ------------------------------------------

    [Fact]
    public void A_part_of_a_run_in_which_nothing_wrote_is_its_refusals_and_not_a_failure()
    {
        IReadOnlyList<GccRunSettlement.TypeAttempt> refused =
        [
            GccRunSettlement.TypeAttempt.Refused("pillar", "Refused: the pillar."),
            GccRunSettlement.TypeAttempt.Refused("blog", "Refused: the blog."),
        ];

        var part = GccRunSettlement.SettlePart(refused);

        Assert.Empty(part.Pieces);
        Assert.Equal(["pillar: Refused: the pillar.", "blog: Refused: the blog."], part.Refusals.Select(r => r.Line));
        // The same attempts settled as a whole run fail it, with the same words the two parts give.
        var failed = Assert.Throws<InvalidOperationException>(() => GccRunSettlement.Settle(refused));
        Assert.Equal(GccRunSettlement.NothingWrote(part.Refusals), failed.Message);
        Assert.Equal("pillar: Refused: the pillar. | blog: Refused: the blog.", failed.Message);

        // Nothing asked of a part is nothing, and a state a run cannot be in is still refused.
        Assert.Empty(GccRunSettlement.SettlePart([]).Pieces);
        Assert.Empty(GccRunSettlement.SettlePart([]).Refusals);
        Assert.Throws<InvalidOperationException>(() => GccRunSettlement.SettlePart(
            [new GccRunSettlement.TypeAttempt("social", null, null)]));
    }

    // ---- the run ------------------------------------------------------------------------------------

    [Fact]
    public async Task A_pillar_and_the_three_pieces_ticked_together_are_written_in_one_run_each_piece_from_the_pillar_it_wrote()
    {
        var model = new ByWhatIsWritten();
        var repository = new ProjectRepository { Model = model };
        // What the last runs left: a pillar with an email under it, a post a Repurpose wrote from a tool
        // page, and an ad a Generate saved as a page of the project before 2026-10-12.
        var oldPillar = repository.Hold("pillar", Keyword, PageBody("The old guide", "An old page."), status: "approved");
        var oldEmail = repository.Hold("email-cold-outreach", Keyword, "{}", under: oldPillar.Id);
        var toolPage = repository.Hold("tool", "Ramp", PageBody("Ramp", "A tool page."));
        var oldPost = repository.Hold("social", "Ramp", "{}", under: toolPage.Id);
        var oldAd = repository.Hold("ads", Keyword, "{}");
        var outcomes = new List<(string Type, bool Wrote, string? Why)>();

        var result = await RunAsync(
            model, repository, ["pillar", "email-cold-outreach", "social", "ads"], Searched(),
            onTypeOutcome: (type, written, why) =>
            {
                lock (outcomes) outcomes.Add((type, written is not null, why));
                return Task.CompletedTask;
            });

        // What it is written from is asked first, then the old pages and every old piece of the asked
        // types go, before a request is sent to the model.
        Assert.Equal(["list", "delete pages", "delete pieces", "save", "list", "read", "save"], repository.Calls);
        Assert.Equal("?type=pillar", repository.PagesDeleteQuery);
        Assert.Equal("?type=email-cold-outreach&type=social&type=ads", repository.PiecesDeleteQuery);
        Assert.Equal(0, repository.RequestsBeforeTheDeletes);
        Assert.DoesNotContain(repository.Everything, held => held.Id == oldPillar.Id || held.Id == oldEmail.Id || held.Id == oldPost.Id || held.Id == oldAd.Id);
        Assert.Contains(repository.Everything, held => held.Id == toolPage.Id);

        // The pillar is saved on its own, as a page of the project, before a piece is asked for.
        Assert.Equal(2, repository.Saves.Count);
        var page = Assert.Single(repository.Saves[0].Command.Pieces);
        Assert.Equal(("pillar", Keyword, (Guid?)null), (page.Type, page.Name, page.ParentPageId));
        Assert.Equal(0, repository.Saves[0].PieceRequestsSoFar);
        var pillar = Assert.Single(repository.Everything, held => held.Type == "pillar");

        // Each piece's writer was handed the pillar this run wrote, whole, and was asked once.
        var written = GccRepurpose.SourceOf(pillar, repository.BodyOf(pillar.Id)).Source!;
        Assert.Contains("Planned section", written.Text, StringComparison.Ordinal);
        Assert.Equal(["ads", "email-cold-outreach", "social"], model.PieceRequests.Select(sent => sent.Type).Order());
        Assert.All(model.PieceRequests, sent =>
        {
            Assert.Contains(written.Text, sent.Sent, StringComparison.Ordinal);
            Assert.Contains("A pillar page of this publisher's", sent.Sent, StringComparison.Ordinal);
            Assert.DoesNotContain("An old page.", sent.Sent, StringComparison.Ordinal);
        });

        // The three are saved in one write, under that pillar, carrying its name.
        Assert.Equal(["email-cold-outreach", "social", "ads"], repository.Saves[1].Command.Pieces.Select(p => p.Type));
        Assert.All(repository.Saves[1].Command.Pieces, piece =>
            Assert.Equal((Keyword, (Guid?)pillar.Id), (piece.Name, piece.ParentPageId)));
        Assert.Equal(
            ["ads", "email-cold-outreach", "social"],
            repository.Everything.Where(held => held.ParentArtifactId == pillar.Id).Select(held => held.Type).Order());

        using var recorded = JsonDocument.Parse(JsonSerializer.Serialize(result, Web));
        Assert.Equal(4, recorded.RootElement.GetProperty("created").GetArrayLength());
        Assert.Equal(0, recorded.RootElement.GetProperty("refusals").GetArrayLength());
        Assert.Equal(["pillar", "email-cold-outreach", "social", "ads"], outcomes.Where(o => o.Wrote).Select(o => o.Type));
        Assert.DoesNotContain(outcomes, o => !o.Wrote);
    }

    [Fact]
    public async Task An_email_ticked_alone_is_written_from_the_saved_pillar_which_is_not_approved_and_nothing_is_searched()
    {
        var model = new ByWhatIsWritten();
        var repository = new ProjectRepository { Model = model };
        var pillar = repository.Hold("pillar", Keyword, PageBody("Closing the books", "The close runs on spreadsheets nobody else can read."));
        var blog = repository.Hold("blog", Keyword, PageBody("A blog", "A blog post."), status: "approved");
        var underThePillar = repository.Hold("email-cold-outreach", Keyword, "{}", under: pillar.Id);
        var underTheBlog = repository.Hold("email-cold-outreach", Keyword, "{}", under: blog.Id);
        // Saved by a Generate before 2026-10-12: a page of the project, under no page.
        var fromAnOlderGenerate = repository.Hold("email-cold-outreach", Keyword, "{}");
        var postUnderThePillar = repository.Hold("social", Keyword, "{}", under: pillar.Id);

        // No search is there to be asked: a run that reached for one would fail on it.
        var result = await RunAsync(model, repository, ["email-cold-outreach"], grounding: null!);

        // No page is deleted, and every email of the project is.
        Assert.Equal(["list", "delete pieces", "list", "read", "save"], repository.Calls);
        Assert.Equal("?type=email-cold-outreach", repository.PiecesDeleteQuery);
        Assert.Equal(0, repository.RequestsBeforeTheDeletes);
        Assert.DoesNotContain(
            repository.Everything,
            held => held.Id == underThePillar.Id || held.Id == underTheBlog.Id || held.Id == fromAnOlderGenerate.Id);
        Assert.Contains(repository.Everything, held => held.Id == postUnderThePillar.Id);
        Assert.Equal("draft", Assert.Single(repository.Everything, held => held.Id == pillar.Id).Status);

        // Written from the pillar the project holds, not the approved blog, and saved under it.
        var sent = Assert.Single(model.PieceRequests);
        Assert.Contains("The close runs on spreadsheets nobody else can read.", sent.Sent, StringComparison.Ordinal);
        Assert.DoesNotContain("A blog post.", sent.Sent, StringComparison.Ordinal);
        var piece = Assert.Single(Assert.Single(repository.Saves).Command.Pieces);
        Assert.Equal(("email-cold-outreach", Keyword, (Guid?)pillar.Id), (piece.Type, piece.Name, piece.ParentPageId));
        using var email = JsonDocument.Parse(piece.BodyDocumentJson);
        Assert.Equal("The close nobody else can read", email.RootElement.GetProperty("subject").GetString());
        Assert.Equal("a ledger on a desk", email.RootElement.GetProperty("imagePrompt").GetProperty("prompt").GetString());

        using var recorded = JsonDocument.Parse(JsonSerializer.Serialize(result, Web));
        Assert.Equal(1, recorded.RootElement.GetProperty("created").GetArrayLength());
        Assert.Equal(0, recorded.RootElement.GetProperty("refusals").GetArrayLength());
        Assert.Equal(0, recorded.RootElement.GetProperty("warnings").GetArrayLength());
    }

    [Fact]
    public async Task A_project_with_no_pillar_has_its_pieces_written_from_its_blog()
    {
        var model = new ByWhatIsWritten();
        var repository = new ProjectRepository { Model = model };
        var blog = repository.Hold("blog", Keyword, PageBody("Closing the books", "The close runs on spreadsheets nobody else can read."));
        repository.Hold("tool", "Ramp", PageBody("Ramp", "A tool page."));

        await RunAsync(model, repository, ["social", "ads"], grounding: null!);

        Assert.All(model.PieceRequests, sent =>
        {
            Assert.Contains("A blog page of this publisher's: \"Closing the books\".", sent.Sent, StringComparison.Ordinal);
            Assert.DoesNotContain("A tool page.", sent.Sent, StringComparison.Ordinal);
        });
        Assert.Equal(["social", "ads"], Assert.Single(repository.Saves).Command.Pieces.Select(p => p.Type));
        Assert.All(repository.Saves[0].Command.Pieces, piece => Assert.Equal(blog.Id, piece.ParentPageId));
    }

    [Fact]
    public async Task A_project_with_no_pillar_and_no_blog_is_refused_before_anything_is_deleted_or_written()
    {
        var model = new ByWhatIsWritten();
        var repository = new ProjectRepository { Model = model };
        var toolPage = repository.Hold("tool", "Ramp", PageBody("Ramp", "A tool page."));
        var oldPost = repository.Hold("social", "Ramp", "{}", under: toolPage.Id);

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => RunAsync(model, repository, ["tool", "social"], grounding: null!));

        Assert.Equal(
            "Refused: 'social' is written from this project's Pillar, or from its Blog when it has no Pillar, and it has neither. "
                + "Tick \"Pillar\" as well, or generate one first. Nothing was generated.",
            refused.Message);
        // Asked, and nothing after: the tool page and the old post stand.
        Assert.Equal(["list"], repository.Calls);
        Assert.Equal([toolPage.Id, oldPost.Id], repository.Everything.Select(held => held.Id));
        Assert.Equal(0, model.Requests);
    }

    [Fact]
    public async Task A_pillar_that_is_ticked_and_refused_takes_its_pieces_with_it_and_they_are_not_written_from_the_blog()
    {
        var model = new ByWhatIsWritten { Fails = { "pillar" } };
        var repository = new ProjectRepository { Model = model };
        repository.Hold("pillar", Keyword, PageBody("The old guide", "An old page."), status: "approved");
        var outcomes = new List<(string Type, bool Wrote, string? Why)>();

        var result = await RunAsync(
            model, repository, ["pillar", "blog", "email-cold-outreach"], Searched(),
            onTypeOutcome: (type, written, why) =>
            {
                lock (outcomes) outcomes.Add((type, written is not null, why));
                return Task.CompletedTask;
            });

        // The blog this run wrote is kept; nothing was asked of the model for the email.
        var saved = Assert.Single(Assert.Single(repository.Saves).Command.Pieces);
        Assert.Equal(("blog", (Guid?)null), (saved.Type, saved.ParentPageId));
        Assert.Empty(model.PieceRequests);
        // The old pillar went with the delete, and no email is under the blog.
        Assert.Equal(["blog"], repository.Everything.Select(held => held.Type));

        using var recorded = JsonDocument.Parse(JsonSerializer.Serialize(result, Web));
        Assert.Equal(1, recorded.RootElement.GetProperty("created").GetArrayLength());
        Assert.Equal(
            [$"pillar: {ByWhatIsWritten.Failure("pillar")}", $"email-cold-outreach: {NotWrittenFromThePillar}"],
            recorded.RootElement.GetProperty("refusals").EnumerateArray().Select(line => line.GetString()));
        Assert.Contains(("blog", true, null), outcomes);
        Assert.Contains(("pillar", false, ByWhatIsWritten.Failure("pillar")), outcomes);
        Assert.Contains(("email-cold-outreach", false, NotWrittenFromThePillar), outcomes);
    }

    [Fact]
    public async Task When_the_pillar_is_refused_and_nothing_else_is_asked_for_the_run_fails_saying_each_reason_once()
    {
        var model = new ByWhatIsWritten { Fails = { "pillar" } };
        var repository = new ProjectRepository { Model = model };
        var outcomes = new List<string>();

        var failed = await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(
            model, repository, ["pillar", "email-cold-outreach"], Searched(),
            onTypeOutcome: (type, _, _) =>
            {
                lock (outcomes) outcomes.Add(type);
                return Task.CompletedTask;
            }));

        Assert.Equal(
            $"pillar: {ByWhatIsWritten.Failure("pillar")} | email-cold-outreach: {NotWrittenFromThePillar}",
            failed.Message);
        Assert.Empty(repository.Saves);
        Assert.Empty(model.PieceRequests);
        // The page is told nothing type by type: the run's failure is each reason, said once.
        Assert.Empty(outcomes);
    }

    [Fact]
    public async Task A_blog_that_is_refused_beside_a_saved_pillar_still_has_its_email_written()
    {
        var model = new ByWhatIsWritten { Fails = { "blog" } };
        var repository = new ProjectRepository { Model = model };
        var pillar = repository.Hold("pillar", Keyword, PageBody("Closing the books", "The close runs on spreadsheets nobody else can read."));

        var result = await RunAsync(model, repository, ["blog", "email-cold-outreach"], Searched());

        var piece = Assert.Single(Assert.Single(repository.Saves).Command.Pieces);
        Assert.Equal(("email-cold-outreach", (Guid?)pillar.Id), (piece.Type, piece.ParentPageId));
        using var recorded = JsonDocument.Parse(JsonSerializer.Serialize(result, Web));
        Assert.Equal(1, recorded.RootElement.GetProperty("created").GetArrayLength());
        Assert.Equal(
            [$"blog: {ByWhatIsWritten.Failure("blog")}"],
            recorded.RootElement.GetProperty("refusals").EnumerateArray().Select(line => line.GetString()));
    }

    /// <summary>
    /// The research is every page's, so a run whose search fails writes none of them. A run of pages
    /// alone fails there. One that also writes an email from the Pillar the project already has does
    /// not: the email needs no research.
    /// </summary>
    [Fact]
    public async Task Pages_that_cannot_be_searched_for_are_each_refused_and_the_email_is_still_written_from_the_saved_pillar()
    {
        var model = new ByWhatIsWritten();
        var repository = new ProjectRepository { Model = model };
        var pillar = repository.Hold("pillar", Keyword, PageBody("Closing the books", "The close runs on spreadsheets nobody else can read."));

        // The library answers no search.
        var result = await RunAsync(model, repository, ["blog", "tool", "email-cold-outreach"], Searched(answers: false));

        Assert.Equal((Guid?)pillar.Id, Assert.Single(Assert.Single(repository.Saves).Command.Pieces).ParentPageId);
        using var recorded = JsonDocument.Parse(JsonSerializer.Serialize(result, Web));
        var refusals = recorded.RootElement.GetProperty("refusals").EnumerateArray().Select(line => line.GetString()!).ToList();
        Assert.Equal(2, refusals.Count);
        Assert.StartsWith("blog: Refused: The evidence library returned nothing for", refusals[0]);
        Assert.StartsWith("tool: Refused: The evidence library returned nothing for", refusals[1]);
        // Only the email's request and its image prompt were sent: no page was written without research.
        Assert.Equal(2, model.Requests);
    }

    [Fact]
    public async Task A_piece_refused_for_a_figure_the_pillar_does_not_state_keeps_the_pillar_and_the_other_piece()
    {
        var model = new ByWhatIsWritten(post: PostWithItsOwnFigure);
        var repository = new ProjectRepository { Model = model };

        var result = await RunAsync(model, repository, ["pillar", "email-cold-outreach", "social"], Searched());

        Assert.Equal(2, repository.Saves.Count);
        Assert.Equal("pillar", Assert.Single(repository.Saves[0].Command.Pieces).Type);
        Assert.Equal("email-cold-outreach", Assert.Single(repository.Saves[1].Command.Pieces).Type);
        // The post was asked for once and not again.
        Assert.Single(model.PieceRequests, sent => sent.Type == "social");

        using var recorded = JsonDocument.Parse(JsonSerializer.Serialize(result, Web));
        Assert.Equal(2, recorded.RootElement.GetProperty("created").GetArrayLength());
        Assert.Equal(
            ["social: Refused: the social post states a figure found in neither the page it was written from nor the Brief: 75%. Nothing was saved for it."],
            recorded.RootElement.GetProperty("refusals").EnumerateArray().Select(line => line.GetString()));
    }

    // ---- fixtures ----------------------------------------------------------------------------------

    private static GccArtifactDto Held(string type, string name = Keyword) =>
        new(Guid.NewGuid(), BriefFieldReachTests.CreateId, ProjectId, null, type, name, "draft",
            BriefFieldReachTests.At, BriefFieldReachTests.At, 1, BriefFieldReachTests.At);

    /// <summary>A page's one version as a long-form writer stores it: the document inside its envelope.</summary>
    private static string PageBody(string title, string sentence) => JsonSerializer.Serialize(
        new
        {
            title,
            body = new ContentDocument(
                new Section("p", "", [new TextParagraph([new Run("An opening.")])], null, []),
                [new Section("h2", "Where it goes wrong", [new TextParagraph([new Run(sentence)])], null, [])]),
        },
        GccDocumentJson.Options);

    /// <summary>The project the run is on: its own site crawled, and no partner or competitor declared,
    /// so the one search a page needs is of the site.</summary>
    private static readonly GccProjectDto Project = new(
        ProjectId, BriefFieldReachTests.CreateId, "Acme", null, null, "active", "https://acme.test", SiteRun, null,
        [], [], new DateOnly(2026, 10, 10), null, null, null, null, null, BriefFieldReachTests.At, BriefFieldReachTests.At);

    /// <summary>The searches a page's research is resolved by, answered from the site's crawl, or by
    /// nothing at all, which is the library failing.</summary>
    private static GccGroundingResolver Searched(bool answers = true)
    {
        var pages = new GccCompetitorAnalysisResolverTests.FakePages();
        var rag = new GccCompetitorAnalysisResolverTests.FakeRag(
            query: answers
                ? (run, _) => new GeekCrawlerRagQueryResult
                {
                    RunId = run,
                    Pages =
                    [
                        new GccQuoteablePage(
                            "https://acme.test/close-guide", "Our close guide", [new HeadingDto(2, "How a close runs")],
                            ["We published this last quarter."], RetrievalMode: GccQuoteablePage.RetrievalModeRagChunk),
                    ],
                }
                : null);
        return new GccGroundingResolver(
            new GccCompetitorAnalysisResolverTests.FakeProjects(Project), rag, new GccTypedPassageReader(pages),
            new GccPublisherPositionsReader(pages, NullLogger<GccPublisherPositionsReader>.Instance),
            NullLogger<GccGroundingResolver>.Instance);
    }

    private static Task<object> RunAsync(
        ByWhatIsWritten model, ProjectRepository repository, IReadOnlyList<string> types, GccGroundingResolver grounding,
        Func<string, object?, string?, Task>? onTypeOutcome = null)
    {
        var service = BriefFieldReachTests.Build(
            model, GccPartnerExtractionFakes.NeverInvoked(new BriefFieldReachTests.FakeProviderFactory(model)), Project);
        return new GccGenerationCoordinator(grounding, NullLogger<GccGenerationCoordinator>.Instance).RunGenerateAsync(
            new HttpGccRepository(
                new HttpClient(repository) { BaseAddress = new Uri("http://repo.test/") }, NullLogger<HttpGccRepository>.Instance),
            service,
            BriefFieldReachTests.Create("pillar", Keyword, BriefFieldReachTests.CompleteBrief, null, ProjectId),
            section: null,
            ContentGeneratorProvider.OpenAi,
            types,
            mustMentionBlock: null,
            CancellationToken.None,
            onTypeOutcome);
    }

    /// <summary>
    /// The model for a whole run. A request is answered by what the run is writing when it is sent: a
    /// page's go to the scripted writer of that page, a piece's get the reply scripted for it.
    /// </summary>
    private sealed class ByWhatIsWritten(string email = Email, string post = Post) : IContentGenerationProvider
    {
        private readonly BriefFieldReachTests.Recorder pillar = new("pillar");
        private readonly BriefFieldReachTests.Recorder blog = new("blog");
        private int requests;

        /// <summary>The types whose every request fails, as a writer that could not write the page.</summary>
        public HashSet<string> Fails { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The user prompt of every request that asked for a piece, with the piece it was for.</summary>
        public List<(string Type, string Sent)> PieceRequests { get; } = [];

        public int Requests => Volatile.Read(ref requests);

        public static string Failure(string type) => $"Refused: the {type} could not be written, for this test.";

        public LlmProviderType ProviderType => LlmProviderType.OpenAi;
        public string ModelFor(ChatCompletionRequest request) => "gpt-4.1";

        public Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref requests);
            var writing = GccRunLog.Piece ?? string.Empty;
            if (Fails.Contains(writing)) throw new InvalidOperationException(Failure(writing));

            if (!GccRepurpose.IsShortType(writing))
                return (writing == "blog" ? blog : pillar).CompleteAsync(request, cancellationToken);

            if (request.Messages[0].Content.Contains("image-generation prompt", StringComparison.Ordinal))
                return Reply("""{"prompt":"a ledger on a desk"}""");

            lock (PieceRequests) PieceRequests.Add((writing, request.Messages[^1].Content));
            return Reply(GccRepurpose.IsEmail(writing) ? email : post);
        }

        private static Task<ChatCompletionResult> Reply(string content) =>
            Task.FromResult(new ChatCompletionResult(content, "test-model", null, null));
    }

    /// <summary>
    /// GeekRepository, as the routes a Generate with short pieces calls, holding what it is given: the
    /// project's pages and pieces listed, the pages of some types deleted with what is under them, every
    /// piece of some types deleted, a run's pieces saved, and a page's one version read.
    /// </summary>
    private sealed class ProjectRepository : HttpMessageHandler
    {
        private readonly List<GccArtifactDto> held = [];
        private readonly Dictionary<Guid, string> bodies = [];

        public ByWhatIsWritten? Model { get; init; }
        public List<string> Calls { get; } = [];
        public List<(SaveGccGeneratedPiecesCommand Command, int PieceRequestsSoFar)> Saves { get; } = [];
        public string? PagesDeleteQuery { get; private set; }
        public string? PiecesDeleteQuery { get; private set; }
        public int? RequestsBeforeTheDeletes { get; private set; }

        public IReadOnlyList<GccArtifactDto> Everything => held;

        public string BodyOf(Guid id) => bodies[id];

        public GccArtifactDto Hold(string type, string name, string body, Guid? under = null, string status = "draft")
        {
            var artifact = new GccArtifactDto(
                Guid.NewGuid(), BriefFieldReachTests.CreateId, ProjectId, under, type, name, status,
                BriefFieldReachTests.At, BriefFieldReachTests.At, 1, BriefFieldReachTests.At);
            held.Add(artifact);
            bodies[artifact.Id] = body;
            return artifact;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var project = $"/repo/content-creator/projects/{ProjectId}";
            if (request.Method == HttpMethod.Get && path == $"{project}/artifacts")
            {
                Calls.Add("list");
                return Json(held);
            }

            if (request.Method == HttpMethod.Delete && path == $"{project}/pages")
            {
                Calls.Add("delete pages");
                PagesDeleteQuery = request.RequestUri.Query;
                RequestsBeforeTheDeletes ??= Model?.Requests ?? 0;
                var types = TypesIn(request.RequestUri.Query);
                var pages = held.Where(a => a.ParentArtifactId is null && types.Contains(a.Type)).Select(a => a.Id).ToHashSet();
                var under = held.RemoveAll(a => a.ParentArtifactId is Guid parent && pages.Contains(parent));
                held.RemoveAll(a => pages.Contains(a.Id));
                return Json(new GccPagesDeleteResult(false, pages.Count, under));
            }

            if (request.Method == HttpMethod.Delete && path == $"{project}/pieces")
            {
                Calls.Add("delete pieces");
                PiecesDeleteQuery = request.RequestUri.Query;
                RequestsBeforeTheDeletes ??= Model?.Requests ?? 0;
                var types = TypesIn(request.RequestUri.Query);
                return Json(new GccProjectPiecesDeleteResult(false, held.RemoveAll(a => types.Contains(a.Type)), 0));
            }

            if (request.Method == HttpMethod.Post && path == $"{project}/generated")
            {
                Calls.Add("save");
                var command = JsonSerializer.Deserialize<SaveGccGeneratedPiecesCommand>(
                    await request.Content!.ReadAsStringAsync(ct), Web)!;
                Saves.Add((command, Model?.PieceRequests.Count ?? 0));
                return Json(GccGeneratedPiecesSaveResult.Written([.. command.Pieces.Select(piece =>
                {
                    var saved = Hold(piece.Type, piece.Name, piece.BodyDocumentJson, piece.ParentPageId);
                    return new GccSavedPieceDto(
                        saved,
                        new GccArtifactVersionDto(
                            Guid.NewGuid(), saved.Id, 1, piece.BodyDocumentJson, piece.MetadataJson, 1, BriefFieldReachTests.At),
                        NewPage: true);
                })]));
            }

            if (request.Method == HttpMethod.Get && path == "/repo/content-creator/versions")
            {
                Calls.Add("read");
                var id = Guid.Parse(request.RequestUri.Query["?artifactId=".Length..]);
                return Json(new[] { new GccArtifactVersionDto(Guid.NewGuid(), id, 1, bodies[id], null, 1, BriefFieldReachTests.At) });
            }

            return new HttpResponseMessage(HttpStatusCode.NotImplemented);
        }

        private static HashSet<string> TypesIn(string query) =>
            query.TrimStart('?').Split('&')
                .Select(part => Uri.UnescapeDataString(part["type=".Length..]))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        private static HttpResponseMessage Json(object value) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(value, Web), Encoding.UTF8, "application/json"),
            };
    }
}
