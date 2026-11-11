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
