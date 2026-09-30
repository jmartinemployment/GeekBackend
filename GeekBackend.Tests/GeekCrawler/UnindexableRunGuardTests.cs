using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using GeekAPI.HttpClients;
using GeekAPI.Services.GeekCrawler;
using Xunit;

namespace GeekBackend.Tests.GeekCrawler;

/// <summary>
/// GeekAPI's in-process crawler produces runs nothing can index: <c>SameOriginBfsCrawler</c> runs no
/// extractor, so <c>GeekCrawlerPageBatchWriter</c> leaves <c>ContentHtml</c> and <c>Blocks</c> null,
/// and nothing on that path stamps <c>ContentReadyAt</c> — which
/// <c>mongo.find_smallest_content_ready_run</c> filters on. Such a run read <c>complete</c>, was
/// never indexed, and was never reported as unindexed.
///
/// <para>
/// The workers are configured to zero, which makes the path unreachable rather than correct. These
/// tests cover the parts that a config flag cannot: <c>GEEK_CRAWLER_WORKER_COUNT</c> is one variable
/// away from being back.
/// </para>
/// </summary>
public class UnindexableRunGuardTests
{
    private static string SolutionRoot => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", ".."));

    private static string ReadSource(string relativePath) =>
        File.ReadAllText(Path.Combine(SolutionRoot, relativePath));

    private static GeekCrawlerRunDto Run(
        string status,
        DateTimeOffset? contentReadyAt) => new(
            Id: Guid.Parse("11111111-2222-4333-8444-555555555555"),
            OwnerUserId: "owner-1",
            CrawlType: "partner",
            Status: status,
            SeedUrlsJson: """["https://example.com"]""",
            SeedKey: "seed-key",
            HostProgressJson: "[]",
            ErrorSummary: null,
            CreatedAtUtc: DateTimeOffset.Parse("2026-09-30T00:00:00Z"),
            StartedAtUtc: null,
            CompletedAtUtc: null,
            ContentReadyAt: contentReadyAt);

    [Fact]
    public void TheLiveFrameCarriesContentReadyAt()
    {
        // The frame omitted the field entirely, so the UI could not show "content ready: no" beside
        // "status: complete" — it could not show it even as null. ToSnapshot always had it; the
        // live frame was the only hole.
        var json = JsonSerializer.Serialize(GeekCrawlerEventMapper.MapRun(Run("complete", null)));
        using var doc = JsonDocument.Parse(json);

        Assert.True(
            doc.RootElement.TryGetProperty("contentReadyAt", out var value),
            "the SignalR frame must carry contentReadyAt");
        Assert.Equal(JsonValueKind.Null, value.ValueKind);
    }

    [Fact]
    public void TheLiveFrameCarriesTheStampWhenThereIsOne()
    {
        var stamped = DateTimeOffset.Parse("2026-09-30T12:34:56Z");
        var json = JsonSerializer.Serialize(GeekCrawlerEventMapper.MapRun(Run("complete", stamped)));
        using var doc = JsonDocument.Parse(json);

        Assert.Equal(stamped, doc.RootElement.GetProperty("contentReadyAt").GetDateTimeOffset());
    }

    [Fact]
    public void RestartingAnExternalRunDoesNotConvertItToPending()
    {
        // The regression this pins is silent and one-way: `external` exists so GeekCrawlerWorker
        // ignores a Crawlee-owned run (GeekCrawlerRunStatuses.cs:7), but the requeue path cancelled
        // the external crawl and forced the run to `pending` so the in-process worker could claim
        // it — turning a good, indexable run into one the Library can never see.
        //
        // Read from source because the branch is inside a private method on a service with a dozen
        // injected dependencies; the same mechanism PostgresIsOAuthOnlyTests uses. What matters is
        // that the external branch refuses rather than patches.
        var source = ReadSource(Path.Combine(
            "GeekAPI", "Services", "GeekCrawler", "GeekCrawlerService.cs"));

        var externalBranch = Regex.Match(
            source,
            @"GeekCrawlerRunStatuses\.External, StringComparison\.OrdinalIgnoreCase\)\)\s*\{(?<body>.*?)\n        \}",
            RegexOptions.Singleline);
        Assert.True(externalBranch.Success, "the external branch of the requeue path must still exist");

        var body = externalBranch.Groups["body"].Value;
        Assert.DoesNotContain("PatchGeekCrawlerRunCommand", body);
        Assert.DoesNotContain("_wake.Wake", body);
        Assert.DoesNotContain("_coordinator.Cancel", body);
        Assert.Contains("throw new InvalidOperationException", body);
    }

    [Fact]
    public void TheInProcessPathFailsRatherThanCompletes()
    {
        // It marked `complete` and never stamped ContentReadyAt, so the run looked finished and was
        // invisible forever. It now fails with the reason, which is the truth about what it produced.
        var source = ReadSource(Path.Combine(
            "GeekAPI", "Services", "GeekCrawler", "GeekCrawlerService.cs"));

        Assert.DoesNotContain("Status: \"complete\"", source);
        Assert.Contains("in-process crawler, which runs no extractor", source);
    }

    [Fact]
    public void IngestRefusesCompleteWithoutContentReadyAt()
    {
        // The route enforced "contentReadyAt requires status=complete" and never the converse, so a
        // run with pages stored could claim completion while denying it was content-ready.
        var source = ReadSource(Path.Combine(
            "GeekAPI", "Controllers", "GeekCrawler", "GeekCrawlerIngestController.cs"));

        Assert.Contains("contentReadyAt requires status=complete", source);
        Assert.Contains("status=complete requires contentReadyAt once pages are stored", source);
    }

    [Fact]
    public void TheEnqueueWarningNoLongerClaimsContentReady()
    {
        // The line read "the run is crawled and content-ready but unindexed" for runs that were
        // never content-ready. A surviving claim is read as evidence.
        var source = ReadSource(Path.Combine(
            "GeekAPI", "Services", "GeekCrawler", "GeekCrawlerService.cs"));

        Assert.DoesNotContain("crawled and content-ready but unindexed", source);
    }
}
