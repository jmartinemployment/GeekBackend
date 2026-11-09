using System.Net;
using System.Text;
using System.Text.Json;
using GeekAPI.HttpClients;
using GeekApplication.Models.Social;
using Microsoft.Extensions.Logging;

namespace GeekBackend.Tests.Social;

/// <summary>
/// repo/social/* over HTTP: each status GeekRepository can answer becomes the failure code the
/// callers branch on, a store that is down is never an empty list, and no log line carries a body.
/// </summary>
public class HttpSocialRepositoryTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string PathAndQuery, string? Body)> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method, request.RequestUri!.PathAndQuery, body));
            return respond(request);
        }
    }

    private sealed class CapturingLogger : ILogger<HttpSocialRepository>
    {
        public List<string> Lines { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception) + (exception?.ToString() ?? string.Empty));
    }

    private static (HttpSocialRepository Repo, StubHandler Handler, CapturingLogger Log) Build(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        var log = new CapturingLogger();
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://geek-repository.test/") };
        return (new HttpSocialRepository(client, log), handler, log);
    }

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private static readonly string[] QuestionOnly = { ReplyCategories.Question };

    private static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(value, WebJson), Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage Text(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body, Encoding.UTF8, "text/plain") };

    private static readonly Guid AccountId = Guid.Parse("7d6c1c5e-7c0a-4d6e-9a0c-2f0f7a3a9b11");

    [Fact]
    public async Task A_success_is_read_as_the_model()
    {
        var (repo, handler, _) = Build(_ => Json(new { id = AccountId, platform = "facebook", platformAccountId = "123", displayName = "Geek Page", status = "connected", autoSendCategories = QuestionOnly, sendsDisabled = false }));

        var result = await repo.GetAccountAsync(AccountId);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("Geek Page", result.Value!.DisplayName);
        Assert.Equal(QuestionOnly, result.Value.AutoSendCategories);
        Assert.Equal((HttpMethod.Get, $"/repo/social/accounts/{AccountId}", null), handler.Requests.Single());
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "No such account.", SocialFailureCodes.NotFound)]
    [InlineData(HttpStatusCode.BadRequest, "displayName is required.", SocialFailureCodes.InvalidRequest)]
    [InlineData(HttpStatusCode.Conflict, "The draft is sent, not pending.", SocialFailureCodes.Conflict)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "The social store failed.", SocialFailureCodes.StoreUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError, "boom", SocialFailureCodes.StoreUnavailable)]
    [InlineData(HttpStatusCode.Unauthorized, "", SocialFailureCodes.StoreUnavailable)]
    public async Task Each_status_becomes_the_code_callers_branch_on(HttpStatusCode status, string body, string expected)
    {
        var (repo, _, _) = Build(_ => Text(status, body));

        var result = await repo.GetAccountAsync(AccountId);

        Assert.False(result.IsSuccess);
        Assert.Equal(expected, result.ErrorCode);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public async Task A_404_with_no_message_is_a_missing_route_not_a_missing_record()
    {
        var (repo, _, log) = Build(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await repo.GetAccountAsync(AccountId);

        Assert.Equal(SocialFailureCodes.StoreUnavailable, result.ErrorCode);
        Assert.Single(log.Lines);
    }

    [Fact]
    public async Task An_unreachable_repository_is_store_unavailable()
    {
        var (repo, _, log) = Build(_ => throw new HttpRequestException("connection refused"));

        var result = await repo.ListAccountsAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(SocialFailureCodes.StoreUnavailable, result.ErrorCode);
        Assert.Null(result.Value);
        Assert.Single(log.Lines);
    }

    [Fact]
    public async Task A_down_store_is_never_an_empty_list()
    {
        var (repo, _, _) = Build(_ => Text(HttpStatusCode.ServiceUnavailable, "The social store failed."));

        Assert.False((await repo.ListAccountsAsync()).IsSuccess);
        Assert.False((await repo.ListPostsAsync(AccountId, 10)).IsSuccess);
        Assert.False((await repo.ListDraftsAsync(null, null, 10)).IsSuccess);
        Assert.False((await repo.ListAuditAsync(null, 10)).IsSuccess);
    }

    [Fact]
    public async Task An_unparseable_or_empty_body_is_a_failure_and_the_log_has_no_body()
    {
        var (repo, _, log) = Build(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{ the comment: 'secret comment text' ", Encoding.UTF8, "application/json") });

        var result = await repo.GetEventAsync(Guid.NewGuid());

        Assert.Equal(SocialFailureCodes.StoreUnavailable, result.ErrorCode);
        Assert.DoesNotContain(log.Lines, l => l.Contains("secret comment text", StringComparison.Ordinal));

        var (nullBody, _, _) = Build(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("null", Encoding.UTF8, "application/json") });
        Assert.Equal(SocialFailureCodes.StoreUnavailable, (await nullBody.GetEventAsync(Guid.NewGuid())).ErrorCode);
    }

    [Fact]
    public async Task A_refusal_logs_the_status_and_the_route_and_never_the_body_or_the_query()
    {
        var (repo, _, log) = Build(_ => Text(HttpStatusCode.ServiceUnavailable, "secret comment text"));

        await repo.ListDraftsAsync("pending", AccountId, 25);

        var line = Assert.Single(log.Lines);
        Assert.Contains("503", line, StringComparison.Ordinal);
        Assert.Contains("repo/social/drafts", line, StringComparison.Ordinal);
        Assert.DoesNotContain("secret comment text", line, StringComparison.Ordinal);
        Assert.DoesNotContain(AccountId.ToString(), line, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Recording_an_event_says_whether_this_call_stored_it(bool inserted)
    {
        var (repo, handler, _) = Build(_ => Json(new { inserted }));

        var result = await repo.RecordEventAsync(new EngagementEvent
        {
            Platform = SocialPlatforms.Facebook,
            PlatformEventId = "e1",
            PlatformAccountId = "123",
            Kind = EngagementKinds.CommentCreated,
            PlatformCommentId = "c1",
            OccurredAt = DateTime.UtcNow,
            ReceivedAt = DateTime.UtcNow,
        });

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(inserted, result.Value);
        Assert.Equal("/repo/social/events", handler.Requests.Single().PathAndQuery);
    }

    [Fact]
    public async Task Routes_and_queries_are_the_ones_GeekRepository_serves()
    {
        var (repo, handler, _) = Build(_ => Json(new List<object>()));
        var draft = Guid.NewGuid();

        await repo.ListDraftsAsync("pending", AccountId, 10);
        await repo.ListPostsAsync(AccountId, 5);
        await repo.ListAuditAsync(AccountId, 7);
        await repo.ListAuditAsync(null, 7);

        Assert.Equal(
            new[]
            {
                $"/repo/social/drafts?limit=10&status=pending&accountId={AccountId}",
                $"/repo/social/posts?accountId={AccountId}&limit=5",
                $"/repo/social/audit?limit=7&accountId={AccountId}",
                "/repo/social/audit?limit=7",
            },
            handler.Requests.Select(r => r.PathAndQuery).ToArray());

        var (post, postHandler, _) = Build(_ => Json(new { id = draft, status = "approved" }));
        await post.TransitionDraftAsync(draft, new TransitionReplyDraftCommand { From = ReplyDraftStatuses.Pending, To = ReplyDraftStatuses.Approved, At = new DateTime(2026, 11, 9, 15, 0, 0, DateTimeKind.Utc) });
        var sent = postHandler.Requests.Single();
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal($"/repo/social/drafts/{draft}/transition", sent.PathAndQuery);
        using var json = JsonDocument.Parse(sent.Body!);
        Assert.Equal("pending", json.RootElement.GetProperty("from").GetString());
        Assert.Equal("approved", json.RootElement.GetProperty("to").GetString());
        Assert.EndsWith("Z", json.RootElement.GetProperty("at").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_stored_credential_travels_as_bytes_and_comes_back_as_bytes()
    {
        var cipher = new byte[] { 9, 8, 7 };
        var (repo, _, _) = Build(_ => Json(new { accountId = AccountId, token = new { cipher = Convert.ToBase64String(cipher), iv = Convert.ToBase64String(new byte[12]), tag = Convert.ToBase64String(new byte[16]), expiresAt = (DateTime?)null } }));

        var result = await repo.GetCredentialAsync(AccountId);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(cipher, result.Value!.Token.Cipher);
        Assert.Equal(12, result.Value.Token.Iv.Length);
        Assert.Equal(16, result.Value.Token.Tag.Length);
    }

    [Fact]
    public async Task A_cancelled_call_is_a_cancellation_not_a_store_failure()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var (repo, _, _) = Build(_ => Json(new { }));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repo.GetAccountAsync(AccountId, cts.Token));
    }
}
