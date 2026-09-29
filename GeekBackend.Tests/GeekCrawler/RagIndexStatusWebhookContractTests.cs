using System.Reflection;
using System.Text.Json;
using GeekAPI.Controllers.GeekCrawler;

namespace GeekBackend.Tests.GeekCrawler;

/// <summary>
/// Every field Geek-Crawler-Rag sends on the index-status webhook must be bound here.
///
/// The two repos cannot see each other's types, so binding is by name over JSON and a field
/// nobody declared is silently discarded by System.Text.Json -- no error, no log, no default
/// worth reading. On 2026-09-29 five fields were in exactly that state, sent on every webhook
/// and bound by nothing: pagesSkippedUnusable, attempt, trigger, embeddingRateLimitRetries and
/// embeddingWaitSeconds.
///
/// pagesSkippedUnusable was the costly one. It counts pages the Library refused as not citable
/// -- a 4xx/5xx body, robots-denied, a non-English locale path, a recorded fetch failure -- and
/// this DTO bound both of its benign siblings, pagesSkippedLang and pagesSkippedEmpty, while
/// dropping it. A run reporting pagesSeen 506 / pagesEnglish 46 with both bound skip counts at
/// zero read identically whether the site was small or 460 error-page bodies had been thrown
/// away. Nothing downstream could tell, including GccDeclaredUrlEvidence, which decides whether
/// evidence landed from RagPagesEnglish alone.
///
/// contracts/rag-index-status/webhook.v1.json is written by the sender and copied here
/// byte-identically; the cross-repo workflow diffs the two copies. This test reads the copy, so
/// a field added upstream fails here until it is bound.
/// </summary>
public sealed class RagIndexStatusWebhookContractTests
{
    private static JsonElement Contract()
    {
        var path = FindContract();
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Walks up from the test assembly to the repository root. The test binary runs from
    /// bin/Debug/net10.0, so a relative path from the working directory is not dependable.
    /// </summary>
    private static string FindContract()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(
                dir.FullName, "contracts", "rag-index-status", "webhook.v1.json");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            "contracts/rag-index-status/webhook.v1.json was not found above " +
            AppContext.BaseDirectory +
            ". It is the wire contract for the RAG index-status webhook and must be committed " +
            "in this repository, byte-identical to the copy in Geek-Crawler-Rag.");
    }

    private static HashSet<string> BoundPropertyNames() =>
        typeof(RagIndexStatusWebhookRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite)
            .Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name))
            .ToHashSet(StringComparer.Ordinal);

    private static HashSet<string> ContractFieldNames() =>
        Contract().GetProperty("fields").EnumerateObject()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void Every_contracted_field_is_bound_on_the_request_dto()
    {
        var unbound = ContractFieldNames().Except(BoundPropertyNames()).OrderBy(x => x).ToList();

        Assert.True(unbound.Count == 0,
            "These fields are sent by Geek-Crawler-Rag and bound by nothing, so they are " +
            "discarded on arrival: " + string.Join(", ", unbound) +
            ". Add a settable property to RagIndexStatusWebhookRequest for each, and decide " +
            "whether it also belongs in the SignalR payload and in crawl_runs.");
    }

    [Fact]
    public void The_dto_binds_nothing_the_sender_does_not_send()
    {
        var phantom = BoundPropertyNames().Except(ContractFieldNames()).OrderBy(x => x).ToList();

        Assert.True(phantom.Count == 0,
            "RagIndexStatusWebhookRequest binds fields absent from the contract: " +
            string.Join(", ", phantom) +
            ". Each will hold its default forever, which reads as real data.");
    }

    [Fact]
    public void The_reject_count_is_bound_and_is_an_int()
    {
        // Pinned by name and type: this is the field whose absence was the defect, and a
        // rename or a nullable-ing of it would quietly reopen the gap.
        var prop = typeof(RagIndexStatusWebhookRequest)
            .GetProperty(nameof(RagIndexStatusWebhookRequest.PagesSkippedUnusable));

        Assert.NotNull(prop);
        Assert.Equal(typeof(int), prop!.PropertyType);
        Assert.True(prop.CanWrite);
    }

    [Fact]
    public void A_real_payload_round_trips_the_reject_count_rather_than_dropping_it()
    {
        // The actual failure, reproduced: this is the body shape the RAG posts. Before the fix
        // it deserialised without error and PagesSkippedUnusable was 0.
        const string body = """
        {
          "runId": "11111111-1111-1111-1111-111111111111",
          "state": "complete",
          "crawlType": "partner",
          "mongoPageCount": 506,
          "pagesSeen": 506,
          "pagesEnglish": 46,
          "pagesSkippedLang": 0,
          "pagesSkippedEmpty": 0,
          "pagesSkippedUnusable": 460,
          "chunksUpserted": 214,
          "attempt": 2,
          "trigger": "scheduler",
          "embeddingRateLimitRetries": 3,
          "embeddingWaitSeconds": 12.5,
          "error": null,
          "startedAtUtc": "2026-09-29T10:00:00+00:00",
          "finishedAtUtc": "2026-09-29T10:04:00+00:00",
          "eventType": "rag_index"
        }
        """;

        var parsed = JsonSerializer.Deserialize<RagIndexStatusWebhookRequest>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(parsed);
        Assert.Equal(460, parsed!.PagesSkippedUnusable);
        Assert.Equal(46, parsed.PagesEnglish);
        Assert.Equal(506, parsed.PagesSeen);
        // 46 + 0 + 0 + 460 == 506: the skip counts now account for the whole gap. Without the
        // reject count they summed to 46 of 506 and the missing 460 had no home.
        Assert.Equal(
            parsed.PagesSeen,
            parsed.PagesEnglish + parsed.PagesSkippedLang
                + parsed.PagesSkippedEmpty + parsed.PagesSkippedUnusable);
        Assert.Equal(2, parsed.Attempt);
        Assert.Equal("scheduler", parsed.Trigger);
        Assert.Equal(3, parsed.EmbeddingRateLimitRetries);
        Assert.Equal(12.5, parsed.EmbeddingWaitSeconds);
    }
}
