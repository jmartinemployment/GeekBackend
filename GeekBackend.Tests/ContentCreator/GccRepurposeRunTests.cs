using System.Net;
using System.Text;
using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.Providers;
using GeekApplication.Interfaces.ContentWriterV3;
using GeekApplication.Models.ContentCreator;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// A Repurpose, run: each short piece is written from the page through its real writer, the page's
/// old pieces of the asked types go first, what wrote is saved under the page in one write, and a
/// piece that states a figure the page does not is refused while the others are kept.
/// </summary>
public sealed class GccRepurposeRunTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static readonly Guid ProjectId = Guid.NewGuid();

    /// <summary>The approved page the pieces are written from: a pillar that states one figure, 60%.</summary>
    private static readonly GccRepurposeSource Page = new(
        Guid.NewGuid(), "pillar", "Automated approval workflows", "Automated Approval Workflows: A Guide",
        "Why approvals stall\n\nAn invoice waits four days for a signature. Teams that route by rule cut approval time by 60%.");

    private const string EmailFromThePage =
        """{"subject":"Cut approval time by 60%","body":"Rule-based routing cut approval time by 60%.","ctaLabel":"Read the guide"}""";

    private const string PostFromThePage = """{"text":"Teams that route by rule cut approval time by 60%."}""";
    private const string PostWithItsOwnFigure = """{"text":"Approvals run 75% faster."}""";

    [Fact]
    public async Task Each_piece_is_written_from_the_page_and_saved_under_it_after_the_old_ones_are_deleted()
    {
        var provider = new ByPiece(email: EmailFromThePage, post: PostFromThePage);
        var repository = new PagesRepository();

        var result = await RunAsync(provider, repository, ["email-cold-outreach", "social", "ads"]);

        // The page's old pieces of the asked types went before a word was written, and nothing else did.
        Assert.Equal("delete", repository.Calls[0]);
        Assert.Equal(["delete", "save"], repository.Calls);
        Assert.Equal($"/repo/content-creator/projects/{ProjectId}/pages/{Page.PageId}/pieces", repository.DeletePath);
        Assert.Equal("?type=email-cold-outreach&type=social&type=ads", repository.DeleteQuery);
        Assert.True(repository.RequestsBeforeTheDelete == 0, "a request was sent to the model before the old pieces were deleted");

        // One write, every piece under the page, each carrying the page's name.
        var save = Assert.Single(repository.Saves);
        Assert.Equal(["email-cold-outreach", "social", "ads"], save.Pieces.Select(p => p.Type));
        Assert.All(save.Pieces, piece =>
        {
            Assert.Equal(Page.PageId, piece.ParentPageId);
            Assert.Equal(Page.Name, piece.Name);
        });

        // Each piece is what this code wrote from the model's fields, with its image prompt.
        using var email = JsonDocument.Parse(save.Pieces[0].BodyDocumentJson);
        Assert.Equal("Cut approval time by 60%", email.RootElement.GetProperty("subject").GetString());
        Assert.Equal("a ledger on a desk", email.RootElement.GetProperty("imagePrompt").GetProperty("prompt").GetString());
        using var post = JsonDocument.Parse(save.Pieces[1].BodyDocumentJson);
        Assert.Equal("Teams that route by rule cut approval time by 60%.", post.RootElement.GetProperty("text").GetString());

        // Every writer was handed the page, and each piece was asked for once.
        Assert.Equal(3, provider.Writers.Count);
        Assert.All(provider.Writers, sent => Assert.Contains(Page.Text, sent, StringComparison.Ordinal));

        using var recorded = JsonDocument.Parse(JsonSerializer.Serialize(result, Web));
        Assert.Equal(3, recorded.RootElement.GetProperty("created").GetArrayLength());
        Assert.Equal(0, recorded.RootElement.GetProperty("refusals").GetArrayLength());
        Assert.Equal(0, recorded.RootElement.GetProperty("preflight").GetArrayLength());
    }

    [Fact]
    public async Task A_piece_that_states_a_figure_the_page_does_not_is_refused_and_the_others_are_kept()
    {
        var provider = new ByPiece(email: EmailFromThePage, post: PostWithItsOwnFigure);
        var repository = new PagesRepository();
        var outcomes = new List<(string Type, bool Wrote, string? Why)>();

        var result = await RunAsync(
            provider, repository, ["email-cold-outreach", "social"],
            onTypeOutcome: (type, written, why) =>
            {
                lock (outcomes) outcomes.Add((type, written is not null, why));
                return Task.CompletedTask;
            });

        // The post was asked for once and not again; the email is the one piece saved.
        Assert.Equal(2, provider.Writers.Count);
        var save = Assert.Single(repository.Saves);
        Assert.Equal("email-cold-outreach", Assert.Single(save.Pieces).Type);

        const string why = "Refused: the social post states a figure the page it was written from does not: 75%. Nothing was saved for it.";
        using var recorded = JsonDocument.Parse(JsonSerializer.Serialize(result, Web));
        Assert.Equal(1, recorded.RootElement.GetProperty("created").GetArrayLength());
        Assert.Equal($"social: {why}", recorded.RootElement.GetProperty("refusals").EnumerateArray().Single().GetString());
        Assert.Contains(("email-cold-outreach", true, null), outcomes);
        Assert.Contains(("social", false, why), outcomes);

        // The refused piece's old one is not left behind to be read as this run's: it went with the delete.
        Assert.Equal("?type=email-cold-outreach&type=social", repository.DeleteQuery);
    }

    [Fact]
    public async Task When_every_piece_is_refused_the_run_fails_saying_why_and_nothing_is_saved()
    {
        var provider = new ByPiece(email: EmailFromThePage, post: PostWithItsOwnFigure);
        var repository = new PagesRepository();

        var failed = await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(provider, repository, ["social", "ads"]));

        Assert.Contains("75%", failed.Message, StringComparison.Ordinal);
        Assert.Equal(["delete"], repository.Calls);
        Assert.Empty(repository.Saves);
    }

    /// <summary>A second Repurpose of the same page with the same types: the first's pieces are deleted
    /// and the second's saved, the same names under the same page, so the page still has one of each.</summary>
    [Fact]
    public async Task A_second_repurpose_deletes_the_firsts_pieces_and_saves_its_own_in_their_place()
    {
        var repository = new PagesRepository();

        await RunAsync(new ByPiece(email: EmailFromThePage, post: PostFromThePage), repository, ["social"]);
        await RunAsync(new ByPiece(email: EmailFromThePage, post: PostFromThePage), repository, ["social"]);

        Assert.Equal(["delete", "save", "delete", "save"], repository.Calls);
        Assert.Equal(2, repository.Saves.Count);
        Assert.All(repository.Saves, save =>
        {
            var piece = Assert.Single(save.Pieces);
            Assert.Equal(("social", Page.Name, (Guid?)Page.PageId), (piece.Type, piece.Name, piece.ParentPageId));
        });
    }

    [Fact]
    public async Task A_page_deleted_before_the_run_reaches_it_fails_the_run_before_anything_is_written()
    {
        var provider = new ByPiece(email: EmailFromThePage, post: PostFromThePage);
        var repository = new PagesRepository { PageIsGone = true };

        var failed = await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(provider, repository, ["social"]));

        Assert.Equal(
            "Refused: the pillar page 'Automated approval workflows' is no longer one of this project's pages, "
                + "so there is nothing to write from and nowhere to save to. Nothing was written.",
            failed.Message);
        Assert.Empty(provider.Writers);
        Assert.Empty(repository.Saves);
    }

    [Fact]
    public async Task A_run_asked_for_a_page_type_is_refused_before_anything_is_deleted_or_written()
    {
        var provider = new ByPiece(email: EmailFromThePage, post: PostFromThePage);
        var repository = new PagesRepository();

        var failed = await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(provider, repository, ["social", "blog"]));

        Assert.Equal(
            "Refused: Repurpose writes an email, a social post or an ad from a page; 'blog' is not one of those. Nothing was written.",
            failed.Message);
        Assert.Empty(repository.Calls);
        Assert.Empty(provider.Writers);
    }

    /// <summary>
    /// What a Repurpose sends, counted as OpenAI counts: the page, whole, and the Brief's reader, voice
    /// and problem. No research and nothing a search returned, so a piece from a 3,000-word pillar is
    /// a request of a few thousand tokens, where a body request of that pillar is about 157,000.
    /// </summary>
    [Fact]
    public async Task A_piece_from_a_3000_word_page_is_a_request_of_a_few_thousand_tokens()
    {
        var sentence = "Finance teams that route each invoice by rule clear approvals before the payment run closes. ";
        var words = sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        // The page's one figure stays in it: the pieces this test's model writes state it.
        var page = Page with { Text = Page.Text + "\n\n" + string.Concat(Enumerable.Repeat(sentence, 3000 / words)) };
        var provider = new ByPiece(email: EmailFromThePage, post: PostFromThePage);

        await RunAsync(
            provider, new PagesRepository(), ["email-cold-outreach", "social", "ads"],
            page: page, briefJson: BriefFieldReachTests.CompleteBrief);

        var sizes = new List<int>();
        Assert.Equal(3, provider.WriterRequests.Count);
        Assert.All(provider.WriterRequests, request =>
        {
            Assert.Contains(page.Text, request.Messages[^1].Content, StringComparison.Ordinal);
            var tokens = GccRequestSize.TokensOf(request);
            Assert.True(tokens is > 3500 and < 6000, $"a Repurpose request was {tokens} tokens");
            sizes.Add(tokens);
        });
        output.WriteLine($"Repurpose requests from a 3,000-word page: {string.Join(", ", sizes)} tokens");
    }

    // ---- fixtures ----------------------------------------------------------------------------------

    private static Task<object> RunAsync(
        ByPiece provider, PagesRepository repository, IReadOnlyList<string> types,
        Func<string, object?, string?, Task>? onTypeOutcome = null,
        GccRepurposeSource? page = null, string? briefJson = null)
    {
        repository.Provider = provider;
        var coordinator = new GccGenerationCoordinator(grounding: null!, NullLogger<GccGenerationCoordinator>.Instance);
        return coordinator.RunRepurposeAsync(
            new HttpGccRepository(
                new HttpClient(repository) { BaseAddress = new Uri("http://repo.test/") }, NullLogger<HttpGccRepository>.Instance),
            GccGenerateServiceImagePromptTests.Build(provider),
            new GccCreateDto(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "pillar", "Automated approval workflows", null,
                null, null, briefJson, null, "draft", DateTime.UtcNow, DateTime.UtcNow, "marketing", ProjectId),
            section: null,
            ContentGeneratorProvider.OpenAi,
            types,
            page ?? Page,
            CancellationToken.None,
            onTypeOutcome);
    }

    /// <summary>The model, answering each request as what it was asked for: the email, a post or an ad,
    /// or the image prompt that follows a piece.</summary>
    private sealed class ByPiece(string email, string post) : IContentGenerationProvider
    {
        public LlmProviderType ProviderType => LlmProviderType.OpenAi;
        public string ModelFor(ChatCompletionRequest request) => "gpt-4.1";

        /// <summary>The user prompt of every request that asked for a piece, not for an image prompt.</summary>
        public List<string> Writers { get; } = [];

        /// <summary>Those requests, whole.</summary>
        public List<ChatCompletionRequest> WriterRequests { get; } = [];

        public Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
        {
            var system = request.Messages[0].Content;
            var user = request.Messages[^1].Content;
            string reply;
            if (system.Contains("image-generation prompt", StringComparison.Ordinal))
            {
                reply = """{"prompt":"a ledger on a desk"}""";
            }
            else
            {
                lock (Writers)
                {
                    Writers.Add(user);
                    WriterRequests.Add(request);
                }

                reply = system.Contains("cold outreach", StringComparison.Ordinal) ? email : post;
            }

            return Task.FromResult(new ChatCompletionResult(reply, "test-model", null, null));
        }
    }

    /// <summary>GeekRepository, as the two routes a Repurpose run calls: delete the page's pieces, save the run's.</summary>
    private sealed class PagesRepository : HttpMessageHandler
    {
        public ByPiece? Provider { get; set; }
        public bool PageIsGone { get; init; }
        public List<string> Calls { get; } = [];
        public List<SaveGccGeneratedPiecesCommand> Saves { get; } = [];
        public string? DeletePath { get; private set; }
        public string? DeleteQuery { get; private set; }
        public int? RequestsBeforeTheDelete { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Delete && path.EndsWith("/pieces", StringComparison.Ordinal))
            {
                Calls.Add("delete");
                DeletePath = path;
                DeleteQuery = request.RequestUri.Query;
                RequestsBeforeTheDelete ??= Provider?.Writers.Count ?? 0;
                return PageIsGone ? new HttpResponseMessage(HttpStatusCode.NotFound) : Json(new GccPiecesDeleteResult(false, 1));
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/generated", StringComparison.Ordinal))
            {
                Calls.Add("save");
                var command = JsonSerializer.Deserialize<SaveGccGeneratedPiecesCommand>(
                    await request.Content!.ReadAsStringAsync(ct), Web)!;
                Saves.Add(command);
                return Json(GccGeneratedPiecesSaveResult.Written([.. command.Pieces.Select(piece =>
                {
                    var pieceId = Guid.NewGuid();
                    return new GccSavedPieceDto(
                        new GccArtifactDto(
                            pieceId, command.CreateId, ProjectId, piece.ParentPageId, piece.Type, piece.Name, "draft",
                            DateTime.UtcNow, DateTime.UtcNow, 1, DateTime.UtcNow),
                        new GccArtifactVersionDto(
                            Guid.NewGuid(), pieceId, 1, piece.BodyDocumentJson, piece.MetadataJson, 1, DateTime.UtcNow),
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
