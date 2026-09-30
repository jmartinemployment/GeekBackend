using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace GeekBackend.Tests.GeekCrawler;

/// <summary>
/// The index-status receiver must not report success for a write it did not do.
///
/// <para>
/// It wrapped the persist in <c>catch (Exception) { LogWarning }</c> and returned
/// <c>202 Accepted</c> regardless — <c>plans/rules.md</c> §3a verbatim. The sender logs at Error on
/// 401/403 and 400, so a dead integration was visible, but a failed <b>persist</b> answered 202, so
/// the one failure mode that loses data was the one the sender could never learn about. It loses
/// <c>RagChunksUpserted</c>, <c>RagPagesEnglish</c> and <c>RagPagesSkippedUnusable</c>, which are the
/// three numbers <c>GccDeclaredUrlEvidence</c> reads to decide whether a partner has usable evidence.
/// </para>
///
/// <para>
/// The controller takes a sealed concrete repository and a SignalR notifier, so its branches are
/// read from source — the mechanism <c>PostgresIsOAuthOnlyTests</c> and
/// <c>UnindexableRunGuardTests</c> already use here. The behaviour underneath is covered against a
/// real Mongo in <c>MongoGeekCrawlerRagIndexStatusTests</c>.
/// </para>
/// </summary>
public sealed class RagIndexStatusPersistHonestyTests
{
    private static string SolutionRoot => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", ".."));

    private static string Receiver => File.ReadAllText(Path.Combine(
        SolutionRoot, "GeekAPI", "Controllers", "GeekCrawler", "GeekCrawlerRagWebhookController.cs"));

    private static string RepositoryRoute => File.ReadAllText(Path.Combine(
        SolutionRoot, "GeekRepository", "Controllers", "GeekCrawler", "GeekCrawlerRunsController.cs"));

    [Fact]
    public void AFailedPersistAnswersWithAStatusInsteadOfFallingThroughToAccepted()
    {
        var source = Receiver;

        // The catch must return, not merely log. Everything between `catch` and the end of that block
        // is what decides whether the sender is told the truth.
        var persistCatch = Regex.Match(
            source,
            @"catch \(Exception ex\) when \(ex is not OperationCanceledException\)\s*\{(?<body>.*?)\n            \}",
            RegexOptions.Singleline);
        Assert.True(persistCatch.Success, "the persist catch must exclude cancellation");

        var body = persistCatch.Groups["body"].Value;
        Assert.Contains("LogError", body);
        Assert.Contains("return", body);
        Assert.Contains("Status502BadGateway", body);
        Assert.Contains("NotFound", body);
        Assert.DoesNotContain("LogWarning", body);
    }

    [Fact]
    public void CancellationIsNotReportedAsAFailedWrite()
    {
        // A disconnected client is not a persist failure, and reporting it as 502 would put a false
        // failure in front of the sender's error branch. Matches the ResolveOwnershipAsync idiom.
        Assert.Contains("ex is not OperationCanceledException", Receiver);
    }

    [Fact]
    public void AFrameForAnUnknownRunIsRefusedRatherThanAccepted()
    {
        // The hole the exception fix alone would have missed: UpdateOneAsync has no upsert, so a
        // runId with no document matches nothing, raises nothing, and was answered 202.
        var source = Receiver;

        var nullRun = source.IndexOf("if (run is null)", StringComparison.Ordinal);
        var persist = source.IndexOf("UpdateRagIndexStatusAsync", StringComparison.Ordinal);
        Assert.True(nullRun >= 0, "an absent run must be handled explicitly");
        Assert.True(
            nullRun < persist,
            "the absent-run check must precede the write it would otherwise discard");

        // And it must not be folded back into the replay guard's condition, where `run is null` used
        // to mean "go ahead and write".
        Assert.DoesNotContain("if (run is null || run.RagIndexedAtUtc is null", source);
    }

    [Fact]
    public void TheRepositoryRouteRefusesAWriteThatMatchedNothing()
    {
        var source = RepositoryRoute;
        Assert.Contains("var written = await _mongo.UpdateRagIndexStatusAsync", source);
        Assert.Contains("if (!written) return NotFound();", source);
    }

    [Fact]
    public void TheGuardIsNotDescribedAsOutOfOrderProtection()
    {
        // It compares FinishedAtUtc, which is null until a run is terminal, so for a mid-run frame it
        // reduces to "RagIndexedAtUtc is null" and no comparison is made. Calling that an
        // out-of-order guard is a safety property no check enforces — CLAUDE.md §2 names exactly
        // that as the defect. It is sound only because the sender emits frames strictly serially,
        // which the comment now has to say.
        var source = Receiver;

        Assert.DoesNotContain("A stale delivery must never overwrite a newer", source);
        Assert.Contains("Duplicate/replay guard, and only that.", source);
        Assert.Contains("nothing sends these concurrently", source);
    }
}
