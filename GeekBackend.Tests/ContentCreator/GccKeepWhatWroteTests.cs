using System.Net;
using System.Text;
using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreator;
using GeekApplication.Interfaces.ContentWriterV3;
using GeekApplication.Models.ContentCreator;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// The multi-type run keeps what wrote when a type is refused, through the real save: the 2026-10-07 run
/// requested five types, three of them wrote, and the job reported nothing saved because the pillar and
/// the blog were refused (Jeff: "One failure should not kill the batch ... Failing jobs is not
/// acceptable").
/// </summary>
public sealed class GccKeepWhatWroteTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static GccGenerationCoordinator.TypeOutcome One(string type, string name) =>
        new([new GccGenerationCoordinator.GeneratedPiece(type, """{"warnings":[]}""", name)], []);

    private static IReadOnlyList<GccRunSettlement.TypeAttempt> TwoRefusedThreeWrote() =>
    [
        GccRunSettlement.TypeAttempt.Refused("pillar", "Refused: the pillar. a link leads nowhere"),
        GccRunSettlement.TypeAttempt.Refused("blog", "Refused: the blog. a figure is not in the evidence"),
        GccRunSettlement.TypeAttempt.Wrote("email-cold-outreach", One("email-cold-outreach", "Cold outreach")),
        GccRunSettlement.TypeAttempt.Wrote("social", One("social", "Social")),
        GccRunSettlement.TypeAttempt.Wrote(
            "tool",
            new GccGenerationCoordinator.TypeOutcome(
                [new GccGenerationCoordinator.GeneratedPiece("tool", """{"warnings":[]}""", "Ramp: Automated Approval Workflows")],
                ["Bill: Refused: money that is not in US dollars"])),
    ];

    private static Task<object> Settle(
        Handler handler,
        IReadOnlyList<GccRunSettlement.TypeAttempt> attempts,
        List<(string Type, bool Produced, string? Error, int SavesSoFar)> announced,
        out GccToolPageFanOutFixture fixtures)
    {
        fixtures = GccToolPageFanOutFixture.WithPartners();
        var local = fixtures;
        return GccGenerationCoordinator.SettleAndSaveAsync(
            Repo(handler), local.Service, local.Create, ["pillar", "blog", "email-cold-outreach", "social", "tool"],
            attempts, [], [], ContentGeneratorProvider.OpenAi, null,
            (type, produced, error) =>
            {
                announced.Add((type, produced is not null, error, handler.Saves));
                return Task.CompletedTask;
            },
            null, NullLogger.Instance, CancellationToken.None);
    }

    [Fact]
    public async Task The_pages_that_wrote_are_saved_in_one_call_and_the_types_that_did_not_are_named_not_thrown()
    {
        var handler = new Handler();
        var announced = new List<(string Type, bool Produced, string? Error, int SavesSoFar)>();

        var result = await Settle(handler, TwoRefusedThreeWrote(), announced, out _);

        var save = Assert.Single(handler.SaveBodies);
        var command = JsonSerializer.Deserialize<SaveGccGeneratedPiecesCommand>(save, Web)!;
        Assert.Equal(
            [("email-cold-outreach", "Cold outreach"), ("social", "Social"), ("tool", "Ramp: Automated Approval Workflows")],
            command.Pieces.Select(p => (p.Type, p.Name)));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result, Web));
        Assert.Equal(3, json.RootElement.GetProperty("created").GetArrayLength());
        Assert.Equal(
            [
                "pillar: Refused: the pillar. a link leads nowhere",
                "blog: Refused: the blog. a figure is not in the evidence",
                "Bill: Refused: money that is not in US dollars",
            ],
            json.RootElement.GetProperty("refusals").EnumerateArray().Select(r => r.GetString()));
    }

    [Fact]
    public async Task What_wrote_is_announced_after_it_is_kept_and_what_did_not_is_announced_without_a_page()
    {
        var handler = new Handler();
        var announced = new List<(string Type, bool Produced, string? Error, int SavesSoFar)>();

        await Settle(handler, TwoRefusedThreeWrote(), announced, out _);

        // Each written piece is announced once the save has happened (SavesSoFar is 1); a refused type has
        // no page, so its announcement carries the reason and no artifact.
        Assert.Equal(
            [
                ("email-cold-outreach", true, null, 1),
                ("social", true, null, 1),
                ("tool", true, null, 1),
                ("pillar", false, "Refused: the pillar. a link leads nowhere", 1),
                ("blog", false, "Refused: the blog. a figure is not in the evidence", 1),
                ("tool", false, "Bill: Refused: money that is not in US dollars", 1),
            ],
            announced);
    }

    [Fact]
    public async Task The_runs_record_says_what_was_saved_and_what_was_refused_and_why()
    {
        var written = new List<GccGenerateJobEventWrite>();
        GccRunLog.Begin(Guid.NewGuid(), (events, _) => { written.AddRange(events); return Task.CompletedTask; }, NullLogger.Instance);

        await Settle(new Handler(), TwoRefusedThreeWrote(), [], out _);

        var settled = Assert.Single(written, w => w.Kind == "settled");
        using var doc = JsonDocument.Parse(settled.PayloadJson);
        Assert.Equal(
            ["email-cold-outreach", "social", "tool"],
            doc.RootElement.GetProperty("saved").EnumerateArray().Select(s => s.GetProperty("type").GetString()));
        Assert.Equal(
            ["pillar", "blog", "tool"],
            doc.RootElement.GetProperty("refused").EnumerateArray().Select(s => s.GetProperty("type").GetString()));
        Assert.Equal(
            "Refused: the pillar. a link leads nowhere",
            doc.RootElement.GetProperty("refused")[0].GetProperty("reason").GetString());
    }

    [Fact]
    public async Task A_run_where_nothing_wrote_fails_saying_why_for_each_type_and_asks_the_repository_for_nothing()
    {
        var handler = new Handler();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => Settle(
            handler,
            [
                GccRunSettlement.TypeAttempt.Refused("pillar", "Refused: the pillar. a"),
                GccRunSettlement.TypeAttempt.Refused("blog", "Refused: the blog. b"),
            ],
            [], out _));

        Assert.Equal("pillar: Refused: the pillar. a | blog: Refused: the blog. b", failure.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_link_check_that_cannot_read_the_projects_pages_does_not_discard_what_was_written()
    {
        // The check reads the project's artifacts to add a warning to a pillar or blog that links a tool
        // page the project lacks. The read failing is a warning on the run, not the loss of the pages.
        var handler = new Handler { ArtifactsDown = true };
        var fixtures = GccToolPageFanOutFixture.WithPartners("https://ramp.com");
        var written = new List<GccGenerateJobEventWrite>();
        GccRunLog.Begin(Guid.NewGuid(), (events, _) => { written.AddRange(events); return Task.CompletedTask; }, NullLogger.Instance);

        var result = await GccGenerationCoordinator.SettleAndSaveAsync(
            Repo(handler), fixtures.Service, fixtures.Create, ["pillar", "social"],
            [
                GccRunSettlement.TypeAttempt.Wrote("pillar", One("pillar", "AP: Approvals")),
                GccRunSettlement.TypeAttempt.Refused("social", "Refused: the social post. x"),
            ],
            [], [], ContentGeneratorProvider.OpenAi, null, null, null, NullLogger.Instance, CancellationToken.None);

        Assert.Single(handler.SaveBodies);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result, Web));
        Assert.Equal(1, json.RootElement.GetProperty("created").GetArrayLength());
        Assert.Contains(
            json.RootElement.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()),
            w => w!.StartsWith("Links to tool pages were not checked", StringComparison.Ordinal));
        var warning = Assert.Single(written, w => w.Kind == "warning");
        using var doc = JsonDocument.Parse(warning.PayloadJson);
        Assert.Equal("tool-page link check", doc.RootElement.GetProperty("step").GetString());
        Assert.Equal("fault", doc.RootElement.GetProperty("fault").GetProperty("kind").GetString());
    }

    [Fact]
    public async Task A_save_the_repository_refuses_still_fails_the_run_because_nothing_was_kept()
    {
        var handler = new Handler { SaveRefused = true };

        await Assert.ThrowsAsync<InvalidOperationException>(() => Settle(handler, TwoRefusedThreeWrote(), [], out _));
    }

    private static HttpGccRepository Repo(Handler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://repo.test/") }, NullLogger<HttpGccRepository>.Instance);

    /// <summary>GeekRepository's routes for the save and the project's artifacts, recording what was asked.</summary>
    private sealed class Handler : HttpMessageHandler
    {
        private int _saves;

        public bool ArtifactsDown { get; init; }

        public bool SaveRefused { get; init; }

        public List<string> Requests { get; } = [];

        public List<string> SaveBodies { get; } = [];

        public int Saves => Volatile.Read(ref _saves);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add($"{request.Method} {path}");
            if (request.Method == HttpMethod.Get && path.EndsWith("/artifacts", StringComparison.Ordinal))
            {
                return ArtifactsDown
                    ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    : Json("[]");
            }

            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct);
            SaveBodies.Add(body);
            Interlocked.Increment(ref _saves);
            if (SaveRefused)
            {
                return Json(JsonSerializer.Serialize(GccGeneratedPiecesSaveResult.Refused("the database refused the write."), Web));
            }

            var command = JsonSerializer.Deserialize<SaveGccGeneratedPiecesCommand>(body, Web)!;
            var projectId = Guid.Parse(request.RequestUri.Segments[^2].TrimEnd('/'));
            var now = DateTime.UtcNow;
            var result = GccGeneratedPiecesSaveResult.Written([.. command.Pieces.Select(p =>
            {
                var page = new GccArtifactDto(Guid.NewGuid(), command.CreateId, projectId, null, p.Type, p.Name, "draft", now, now, 1, now);
                return new GccSavedPieceDto(
                    page, new GccArtifactVersionDto(Guid.NewGuid(), page.Id, 1, p.BodyDocumentJson, p.MetadataJson, 0, now), true);
            })]);
            return Json(JsonSerializer.Serialize(result, Web));
        }

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
