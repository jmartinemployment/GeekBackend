using System.Net;
using System.Text;
using GeekAPI.HttpClients;
using GeekApplication.Models.Visitors;
using GeekApplication.Results;

namespace GeekBackend.Tests.Visitors;

/// <summary>Status codes become Results; nothing throws; the log names the route and not the query or the body.</summary>
public sealed class HttpVisitorRepositoryTests
{
    [Fact]
    public async Task Two_hundred_parses_the_session_and_404_is_not_found_and_503_is_a_failure()
    {
        var handler = new ScriptedHandler();
        var log = new CapturingLogger<HttpVisitorRepository>();
        var repo = new HttpVisitorRepository(new HttpClient(handler) { BaseAddress = new Uri("http://repo.test/") }, log);
        var id = Guid.NewGuid();

        handler.Next = (HttpStatusCode.OK, $"{{\"id\":\"{id}\",\"visitorId\":\"{Guid.NewGuid()}\",\"ip\":\"203.0.113.5\",\"pages\":[],\"score\":2.5}}");
        var ok = await repo.GetSessionAsync(id, CancellationToken.None);
        Assert.True(ok.IsSuccess);
        Assert.Equal(id, ok.Value!.Id);
        Assert.Equal(2.5, ok.Value.Score);

        handler.Next = (HttpStatusCode.NotFound, "Session not found.");
        var missing = await repo.GetSessionAsync(id, CancellationToken.None);
        Assert.Equal(ResultStatus.NotFound, missing.Status);

        handler.Next = (HttpStatusCode.ServiceUnavailable, "Visitor store write failed.");
        var down = await repo.ScoreAsync(id, new VisitorScoreCommand { Score = 10 }, CancellationToken.None);
        Assert.Equal(ResultStatus.Failure, down.Status);
        Assert.Contains(log.Lines, l => l.Contains("503") && l.Contains("repo/visitors/sessions/"));

        handler.Next = (HttpStatusCode.OK, "{\"claimed\":true}");
        var marked = await repo.MarkIntentReachedAsync(id, new VisitorIntentReachedCommand { At = DateTime.UtcNow }, CancellationToken.None);
        Assert.True(marked.Value);
        handler.Next = (HttpStatusCode.NotFound, "Session not found.");
        Assert.Equal(ResultStatus.NotFound, (await repo.MarkIntentReachedAsync(id, new VisitorIntentReachedCommand { At = DateTime.UtcNow }, CancellationToken.None)).Status);
        handler.Next = (HttpStatusCode.OK, "{\"sessions\":4,\"visitors\":2}");
        var purged = await repo.PurgeAsync(new VisitorPurgeCommand { AsOf = DateTime.UtcNow }, CancellationToken.None);
        Assert.Equal(4, purged.Value!.Sessions);
        Assert.EndsWith("repo/visitors/retention/purge", handler.Requests[^1]);

        handler.Throw = true;
        var unreachable = await repo.CountDropAsync(new VisitorDropCommand { Day = "2026-10-17", Reason = "bot" }, CancellationToken.None);
        Assert.False(unreachable.IsSuccess);
    }

    [Fact]
    public async Task The_session_list_pages_until_a_short_page_and_logs_no_query_string()
    {
        var handler = new ScriptedHandler();
        var log = new CapturingLogger<HttpVisitorRepository>();
        var repo = new HttpVisitorRepository(new HttpClient(handler) { BaseAddress = new Uri("http://repo.test/") }, log);
        var full = string.Join(',', Enumerable.Range(0, HttpVisitorRepository.PageSize)
            .Select(i => $"{{\"id\":\"{Guid.NewGuid()}\",\"sequence\":{i + 1},\"visitorId\":\"{Guid.NewGuid()}\",\"startedAt\":\"2026-10-17T{(10 + i / 60) % 24:00}:{i % 60:00}:00Z\",\"pages\":[]}}"));
        handler.Queue.Enqueue((HttpStatusCode.OK, "[" + full + "]"));
        handler.Queue.Enqueue((HttpStatusCode.OK, $"[{{\"id\":\"{Guid.NewGuid()}\",\"visitorId\":\"{Guid.NewGuid()}\",\"startedAt\":\"2026-10-17T23:00:00Z\",\"pages\":[]}}]"));

        var all = await repo.ListSessionsAsync(new DateTime(2026, 10, 17, 4, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 18, 4, 0, 0, DateTimeKind.Utc), CancellationToken.None);
        Assert.True(all.IsSuccess);
        Assert.Equal(HttpVisitorRepository.PageSize + 1, all.Value!.Count);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("afterStartedAt=", handler.Requests[1]);
        Assert.Contains($"afterSequence={HttpVisitorRepository.PageSize}", handler.Requests[1]);
        Assert.Empty(log.Lines);
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public (HttpStatusCode, string)? Next { get; set; }
        public Queue<(HttpStatusCode, string)> Queue { get; } = new();
        public bool Throw { get; set; }
        public List<string> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.PathAndQuery);
            if (Throw) throw new HttpRequestException("connection refused");
            var (status, body) = Queue.Count > 0 ? Queue.Dequeue() : Next ?? (HttpStatusCode.InternalServerError, "unscripted");
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
