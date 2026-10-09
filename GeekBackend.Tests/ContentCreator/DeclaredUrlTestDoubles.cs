using System.Net;
using System.Text;
using GeekAPI.Services.GeekCrawler;
using GeekApplication.Models.ContentCreator;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// The crawler-repository and index doubles the declared-URL tests share: a run good enough to write
/// from, and a search that does or does not find it. One implementation for the suite, so "usable"
/// means one thing in every test that says it.
/// </summary>
internal static class DeclaredUrlTestDoubles
{
    /// <summary>
    /// A crawl run whose counters all pass, answered for whichever run id is asked for -- so each
    /// declared URL keeps its own run and a search can be answered per run.
    /// </summary>
    internal sealed class UsableRunHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    UsableRunJson.Replace("RUN_ID", request.RequestUri!.Segments[^1].TrimEnd('/')),
                    Encoding.UTF8, "application/json"),
            });

        private const string UsableRunJson =
            """
            {"id":"RUN_ID","ownerUserId":"operator-1",
             "crawlType":"partner","status":"complete","seedUrlsJson":"[]","seedKey":null,
             "hostProgressJson":null,"errorSummary":null,"createdAtUtc":"2026-09-01T00:00:00Z",
             "startedAtUtc":"2026-09-01T00:00:00Z","completedAtUtc":"2026-09-01T01:00:00Z",
             "contentReadyAt":"2026-09-01T01:00:00Z","crawlReportJson":null,"ragState":"complete",
             "ragChunksUpserted":400,"ragPagesEnglish":40,
             "ragIndexedAtUtc":"2026-09-01T02:00:00Z"}
            """;
    }

    /// <summary>A search that returns a page: the run holds chunks.</summary>
    internal static GeekCrawlerRagQueryResult Holds(Guid runId) =>
        new() { RunId = runId, Pages = [new GccQuoteablePage("https://found.test/page", "Found", [], ["Text."])] };

    /// <summary>A search that returns nothing: "No chunks for runId".</summary>
    internal static GeekCrawlerRagQueryResult HoldsNothing(Guid runId) =>
        new() { RunId = runId, Pages = [], Warning = $"No chunks for runId={runId}; notify-and-skip research", Retrieval = "empty" };
}
