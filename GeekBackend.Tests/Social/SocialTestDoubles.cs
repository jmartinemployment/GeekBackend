using System.Net;
using System.Text;
using System.Text.Json;
using GeekApplication.Interfaces.Social;
using GeekApplication.Models.Social;
using GeekApplication.Results;
using Microsoft.Extensions.Logging;

namespace GeekBackend.Tests.Social;

/// <summary>A logger that keeps every line it is given, so a test can read what was logged and check what was not.</summary>
internal sealed class RecordingLog<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entries.Add((logLevel, formatter(state, exception), exception));

    public IEnumerable<string> Messages => Entries.Select(e => e.Message);
}

/// <summary>An HTTP handler that answers each request with what the test scripted and counts the calls.</summary>
internal sealed class ScriptedModelHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> answer) : HttpMessageHandler
{
    public int Calls { get; private set; }
    public string? LastBody { get; private set; }
    public HttpRequestMessage? LastRequest { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        LastRequest = request;
        LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return answer(request, LastBody);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}

/// <summary>A model client a test scripts; it records every request it was handed.</summary>
internal sealed class FakeModelClient(Func<SocialModelRequest, Result<JsonElement>> answer) : ISocialModelClient
{
    public List<SocialModelRequest> Requests { get; } = new();

    public Task<Result<JsonElement>> CallToolAsync(SocialModelRequest request, CancellationToken ct = default)
    {
        Requests.Add(request);
        return Task.FromResult(answer(request));
    }

    public static Result<JsonElement> Answer(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return Result<JsonElement>.Success(doc.RootElement.Clone());
    }

    public static FakeModelClient Never() =>
        new(_ => throw new InvalidOperationException("The model must not be called in this test."));
}

/// <summary>Records every live-update message, so a test can check what the screen was told (ids and states only).</summary>
internal sealed class FakeRealtime : GeekAPI.Services.Social.Realtime.ISocialRealtime
{
    public List<(string Message, Guid Id, Guid AccountId, string State)> Sent { get; } = new();

    public Task DraftChangedAsync(Guid draftId, Guid accountId, string status, CancellationToken ct = default)
    {
        Sent.Add(("draftChanged", draftId, accountId, status));
        return Task.CompletedTask;
    }

    public Task CommentChangedAsync(Guid eventId, Guid accountId, string outcome, CancellationToken ct = default)
    {
        Sent.Add(("commentChanged", eventId, accountId, outcome));
        return Task.CompletedTask;
    }

    public Task AccountChangedAsync(Guid accountId, string status, CancellationToken ct = default)
    {
        Sent.Add(("accountChanged", accountId, accountId, status));
        return Task.CompletedTask;
    }
}

/// <summary>A send step a test scripts; by default nothing is waiting and nothing is auto-sent.</summary>
internal sealed class FakeSendStep : GeekAPI.Services.Social.Orchestration.ISocialSendStep
{
    public List<Guid> AutoSent { get; } = new();
    public List<(Guid DraftId, string? Actor)> Sends { get; } = new();
    public int WaitingPasses { get; private set; }

    public Func<ReplyDraft, Result<GeekAPI.Services.Social.Orchestration.SocialSendOutcome>?> OnAutoSend { get; set; } =
        _ => Result<GeekAPI.Services.Social.Orchestration.SocialSendOutcome>.Success(new GeekAPI.Services.Social.Orchestration.SocialSendOutcome { Sent = true });

    public Func<Guid, Result<GeekAPI.Services.Social.Orchestration.SocialSendOutcome>> OnSend { get; set; } =
        _ => Result<GeekAPI.Services.Social.Orchestration.SocialSendOutcome>.Success(new GeekAPI.Services.Social.Orchestration.SocialSendOutcome { Sent = true });

    public Func<EngagementEvent, Result<List<string>>> OnApprovedLinks { get; set; } =
        _ => Result<List<string>>.Success(new List<string>());

    public Task<Result<GeekAPI.Services.Social.Orchestration.SocialSendOutcome>?> AutoSendAsync(ReplyDraft draft, SocialAccount account, CancellationToken ct)
    {
        AutoSent.Add(draft.Id);
        return Task.FromResult(OnAutoSend(draft));
    }

    public Task<Result<GeekAPI.Services.Social.Orchestration.SocialSendOutcome>> SendAsync(Guid draftId, string? actorUserId, CancellationToken ct)
    {
        Sends.Add((draftId, actorUserId));
        return Task.FromResult(OnSend(draftId));
    }

    public Task<int> SendWaitingAsync(CancellationToken ct)
    {
        WaitingPasses++;
        return Task.FromResult(0);
    }

    public Task<Result<List<string>>> ApprovedLinksAsync(EngagementEvent ev, CancellationToken ct) =>
        Task.FromResult(OnApprovedLinks(ev));
}
