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
/// A Generate saves everything it wrote in one call, to the project's pages, and says a piece exists
/// only after it has been kept.
/// </summary>
public sealed class GccGenerateSaveTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static readonly Guid ProjectId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid CreateId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly GccGenerationCoordinator.GeneratedPiece[] Pieces =
    [
        new("pillar", """{"body":"pillar"}""", "AP: Approvals"),
        new("tool", """{"body":"ramp"}""", "Ramp"),
        new("tool", """{"body":"bill"}""", "Bill"),
    ];

    [Fact]
    public async Task Every_piece_goes_to_the_projects_pages_in_one_call_and_is_announced_after_it()
    {
        var repository = new Repository(saved: true);
        var announced = new List<(string Type, bool SavedFirst)>();

        var produced = await GccGenerationCoordinator.PersistAllAsync(
            Repo(repository), Create(ProjectId), Pieces, ContentGeneratorProvider.Anthropic,
            new GccBriefRevisionStamp(Guid.NewGuid(), new DateTime(2026, 10, 5, 18, 11, 25, DateTimeKind.Utc)),
            (type, _, _) =>
            {
                announced.Add((type, repository.Calls.Count == 1));
                return Task.CompletedTask;
            },
            CancellationToken.None);

        var call = Assert.Single(repository.Calls);
        Assert.Equal($"/repo/content-creator/projects/{ProjectId}/generated", call.Path);
        var command = JsonSerializer.Deserialize<SaveGccGeneratedPiecesCommand>(call.Body, Web)!;
        Assert.Equal(CreateId, command.CreateId);
        Assert.Equal(
            [("pillar", "AP: Approvals"), ("tool", "Ramp"), ("tool", "Bill")],
            command.Pieces.Select(p => (p.Type, p.Name)));
        Assert.Equal("""{"body":"ramp"}""", command.Pieces[1].BodyDocumentJson);
        // Each version is stamped with who wrote it and the brief it was written from.
        Assert.All(command.Pieces, p => Assert.Contains("Anthropic", p.MetadataJson!, StringComparison.Ordinal));
        Assert.All(command.Pieces, p => Assert.Contains("briefRevisionSavedAtUtc", p.MetadataJson!, StringComparison.Ordinal));

        Assert.Equal(3, produced.Count);
        Assert.Equal([("pillar", true), ("tool", true), ("tool", true)], announced);
    }

    [Fact]
    public async Task A_refused_save_fails_the_run_with_the_reason_and_announces_nothing()
    {
        var repository = new Repository(saved: false);
        var announced = 0;

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            GccGenerationCoordinator.PersistAllAsync(
                Repo(repository), Create(ProjectId), Pieces, ContentGeneratorProvider.OpenAi, null,
                (_, _, _) =>
                {
                    announced++;
                    return Task.CompletedTask;
                },
                CancellationToken.None));

        Assert.Equal("None of the 3 piece(s) was saved: the database refused the write.", failure.Message);
        Assert.Equal(0, announced);
    }

    [Fact]
    public async Task A_project_that_is_gone_fails_the_run()
    {
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            GccGenerationCoordinator.PersistAllAsync(
                Repo(new Repository(saved: true, projectExists: false)), Create(ProjectId), Pieces,
                ContentGeneratorProvider.OpenAi, null, null, CancellationToken.None));

        Assert.Contains("the project no longer exists", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_run_that_wrote_nothing_saves_nothing_and_asks_for_nothing()
    {
        var repository = new Repository(saved: true);

        var produced = await GccGenerationCoordinator.PersistAllAsync(
            Repo(repository), Create(ProjectId), [], ContentGeneratorProvider.OpenAi, null, null, CancellationToken.None);

        Assert.Empty(produced);
        Assert.Empty(repository.Calls);
    }

    [Fact]
    public async Task A_create_on_no_project_has_nowhere_to_save_and_is_refused_by_name()
    {
        var repository = new Repository(saved: true);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            GccGenerationCoordinator.PersistAllAsync(
                Repo(repository), Create(projectId: null), Pieces, ContentGeneratorProvider.OpenAi, null, null,
                CancellationToken.None));

        Assert.Equal(GccGenerationCoordinator.NoProjectRefusal, failure.Message);
        Assert.StartsWith("Refused:", failure.Message, StringComparison.Ordinal);
        Assert.Empty(repository.Calls);
    }

    private static HttpGccRepository Repo(Repository handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://repo.test/") }, NullLogger<HttpGccRepository>.Instance);

    private static GccCreateDto Create(Guid? projectId) => new(
        CreateId, Guid.NewGuid(), Guid.NewGuid(), "pillar", "AP: Approvals", null, null, null, null, null,
        "draft", DateTime.UtcNow, DateTime.UtcNow, "marketing", projectId);

    /// <summary>GeekRepository's page-save route: saves what it is sent, refuses it, or has no such project.</summary>
    private sealed class Repository(bool saved, bool projectExists = true) : HttpMessageHandler
    {
        public List<(string Path, string Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct);
            Calls.Add((request.RequestUri!.AbsolutePath, body));
            if (!projectExists) return new HttpResponseMessage(HttpStatusCode.NotFound);

            GccGeneratedPiecesSaveResult result;
            if (saved)
            {
                var command = JsonSerializer.Deserialize<SaveGccGeneratedPiecesCommand>(body, Web)!;
                var now = DateTime.UtcNow;
                result = GccGeneratedPiecesSaveResult.Written([.. command.Pieces.Select(p =>
                {
                    var page = new GccArtifactDto(Guid.NewGuid(), command.CreateId, null, p.Type, p.Name, "draft", now, now, 1, now);
                    return new GccSavedPieceDto(
                        page, new GccArtifactVersionDto(Guid.NewGuid(), page.Id, 1, p.BodyDocumentJson, p.MetadataJson, 0, now), true);
                })]);
            }
            else
            {
                result = GccGeneratedPiecesSaveResult.Refused("None of the 3 piece(s) was saved: the database refused the write.");
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(result, Web), Encoding.UTF8, "application/json"),
            };
        }
    }
}
