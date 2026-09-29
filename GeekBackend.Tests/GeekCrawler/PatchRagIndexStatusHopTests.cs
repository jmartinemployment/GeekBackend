using System.Reflection;
using System.Text.Json;
using SenderCommand = GeekAPI.HttpClients.PatchRagIndexStatusCommand;
using ReceiverCommand = GeekRepository.Controllers.GeekCrawler.GeekCrawlerRunsController.PatchRagIndexStatusCommand;

namespace GeekBackend.Tests.GeekCrawler;

/// <summary>
/// The second name-bound JSON hop, and the one where a drift is worse than a dropped field.
///
/// <c>PatchRagIndexStatusCommand</c> is declared TWICE, independently, with no compile-time
/// relationship: once in GeekAPI (<c>HttpClients/GeekCrawlerDtos.cs</c>) and once in GeekRepository
/// (nested in <c>GeekCrawlerRunsController</c>). <c>HttpGeekCrawlerRepository</c> serialises the
/// first and PATCHes <c>repo/geek-crawler/runs/{runId}/rag-index-status</c>, where the second binds
/// it by name over camelCase JSON. Until 2026-09-29 no test anywhere touched this hop.
///
/// Why a drift here is not merely a lost update: <c>MongoGeekCrawlerService.UpdateRagIndexStatusAsync</c>
/// issues an UNCONDITIONAL <c>$set</c> of all five members. A name that stops matching binds to
/// null, and null is then written over real data. Rename a member on one record only and the
/// webhook contract tests, the sender-side Python tests and the cross-repo byte-diff all stay
/// green while every accepted webhook nulls the reject count in Mongo — the 2026-09-29 incident
/// again, one hop downstream, with the new guard fully green.
///
/// These options are the ones the sender really uses (<c>HttpGeekCrawlerRepository.JsonOpts</c>:
/// CamelCase + case-insensitive), so the test exercises the real encoding rather than a guess at it.
/// </summary>
public sealed class PatchRagIndexStatusHopTests
{
    private static readonly JsonSerializerOptions Wire = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static IEnumerable<PropertyInfo> Members(Type t) =>
        t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name != "EqualityContract")
            .OrderBy(p => p.Name, StringComparer.Ordinal);

    [Fact]
    public void The_two_records_declare_the_same_members_with_the_same_types()
    {
        var sender = Members(typeof(SenderCommand))
            .Select(p => $"{p.Name}:{p.PropertyType.Name}").ToList();
        var receiver = Members(typeof(ReceiverCommand))
            .Select(p => $"{p.Name}:{p.PropertyType.Name}").ToList();

        Assert.Equal(sender, receiver);
    }

    [Fact]
    public void Every_member_survives_the_hop_with_its_value()
    {
        // Distinct values per member: a transposition binds without error and would pass an
        // all-same-value check.
        var sent = new SenderCommand(
            RagState: "complete",
            RagChunksUpserted: 1759,
            RagPagesEnglish: 32,
            RagPagesSkippedUnusable: 460,
            RagIndexedAtUtc: new DateTimeOffset(2026, 9, 29, 17, 58, 39, TimeSpan.Zero));

        var json = JsonSerializer.Serialize(sent, Wire);
        var received = JsonSerializer.Deserialize<ReceiverCommand>(json, Wire);

        Assert.NotNull(received);
        Assert.Equal("complete", received!.RagState);
        Assert.Equal(1759, received.RagChunksUpserted);
        Assert.Equal(32, received.RagPagesEnglish);
        Assert.Equal(460, received.RagPagesSkippedUnusable);
        Assert.Equal(sent.RagIndexedAtUtc, received.RagIndexedAtUtc);
    }

    [Fact]
    public void No_member_arrives_null_when_the_sender_set_it()
    {
        // The load-bearing assertion. Because the $set is unconditional, a member that binds to
        // null does not skip the field — it overwrites a real value with null. So "arrived null
        // while the sender set it" is the exact precondition for data loss, and it is checked here
        // by reflection so a member added later is covered without editing this test.
        var sent = new SenderCommand(
            RagState: "complete",
            RagChunksUpserted: 1,
            RagPagesEnglish: 2,
            RagPagesSkippedUnusable: 3,
            RagIndexedAtUtc: DateTimeOffset.UnixEpoch);

        var json = JsonSerializer.Serialize(sent, Wire);
        var received = JsonSerializer.Deserialize<ReceiverCommand>(json, Wire);
        Assert.NotNull(received);

        var nulled = Members(typeof(ReceiverCommand))
            .Where(p => p.GetValue(received) is null)
            .Select(p => p.Name)
            .ToList();

        Assert.True(nulled.Count == 0,
            "These members were set by the sender and arrived null, so an unconditional $set would "
            + "write null over real data in crawl_runs: " + string.Join(", ", nulled)
            + ". The two PatchRagIndexStatusCommand records have drifted — they are joined only by "
            + "camelCase JSON, with no compile-time relationship.");
    }

    [Fact]
    public void A_null_the_sender_really_sent_still_arrives_null()
    {
        // The other direction, so the check above cannot be satisfied by defaulting everything:
        // a genuinely absent value must stay absent rather than become 0.
        var sent = new SenderCommand(null, null, null, null, null);
        var received = JsonSerializer.Deserialize<ReceiverCommand>(
            JsonSerializer.Serialize(sent, Wire), Wire);

        Assert.NotNull(received);
        Assert.Null(received!.RagState);
        Assert.Null(received.RagChunksUpserted);
        Assert.Null(received.RagPagesEnglish);
        Assert.Null(received.RagPagesSkippedUnusable);
        Assert.Null(received.RagIndexedAtUtc);
    }
}
