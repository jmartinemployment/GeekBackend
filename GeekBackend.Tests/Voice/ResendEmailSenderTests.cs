using System.Net;
using System.Text;
using System.Text.Json;
using GeekAPI.Services.Voice.Email;
using GeekApplication.Models.Voice;
using Microsoft.Extensions.Logging;

namespace GeekBackend.Tests.Voice;

/// <summary>
/// Sending one transactional email through Resend: the call is made once with a Bearer key and a JSON body, a success is the
/// provider's message id, every failure is an <c>email_send_failed</c> result, an unset provider fails closed without a call,
/// and no log line carries the recipient, the subject or the body — call content this platform keeps none of.
/// </summary>
public sealed class ResendEmailSenderTests
{
    private const string Key = "re_test_secret_key";
    private const string Recipient = "owner@example.test";
    private const string Subject = "A caller left a message";
    private const string TextBody = "Jane Doe called about a leaking water heater. Call back 561-555-0199.";

    private sealed record Seen(HttpMethod Method, string Url, string? Authorization, string Body);

    private sealed class StubHandler(Func<Seen, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Seen> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            var seen = new Seen(request.Method, request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(), body);
            Requests.Add(seen);
            return respond(seen);
        }
    }

    private sealed class ThrowingHandler(Exception toThrow) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw toThrow;
    }

    private sealed class CapturingLogger : ILogger<ResendEmailSender>
    {
        public List<(LogLevel Level, string Line)> Lines { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add((logLevel, formatter(state, exception) + (exception?.ToString() ?? string.Empty)));
    }

    private static EmailMessage Message() =>
        new(Recipient, Subject, TextBody, HtmlBody: "<p>" + TextBody + "</p>", Purpose: "owner-message");

    private static (ResendEmailSender Sender, CapturingLogger Log) Make(HttpMessageHandler handler, EmailOptions? options = null)
    {
        var log = new CapturingLogger();
        var opts = options ?? new EmailOptions(Key, "receptionist@example.test", "Geek Receptionist");
        return (new ResendEmailSender(new HttpClient(handler), opts, log), log);
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Sends_a_bearer_authorized_json_message_and_returns_the_provider_id()
    {
        var handler = new StubHandler(_ => Json("""{"id":"resend-message-id-1"}"""));
        var (sender, _) = Make(handler);

        var result = await sender.SendAsync(Message());

        Assert.True(result.IsSuccess);
        Assert.Equal("resend-message-id-1", result.Value);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.resend.com/emails", request.Url);
        Assert.Equal("Bearer " + Key, request.Authorization);

        using var sent = JsonDocument.Parse(request.Body);
        Assert.Equal("Geek Receptionist <receptionist@example.test>", sent.RootElement.GetProperty("from").GetString());
        Assert.Equal(Recipient, sent.RootElement.GetProperty("to")[0].GetString());
        Assert.Equal(Subject, sent.RootElement.GetProperty("subject").GetString());
        Assert.Equal(TextBody, sent.RootElement.GetProperty("text").GetString());
        Assert.Equal("<p>" + TextBody + "</p>", sent.RootElement.GetProperty("html").GetString());
    }

    [Fact]
    public async Task A_message_without_html_omits_the_html_field()
    {
        var handler = new StubHandler(_ => Json("""{"id":"resend-message-id-2"}"""));
        var (sender, _) = Make(handler);

        var result = await sender.SendAsync(new EmailMessage(Recipient, Subject, TextBody, HtmlBody: null, Purpose: "per-call-summary"));

        Assert.True(result.IsSuccess);
        using var sent = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        Assert.False(sent.RootElement.TryGetProperty("html", out _));
    }

    [Fact]
    public async Task An_unset_provider_fails_closed_without_calling_resend()
    {
        var handler = new StubHandler(_ => Json("""{"id":"should-not-happen"}"""));
        var (sender, log) = Make(handler, new EmailOptions(ApiKey: null, FromAddress: null, FromName: null));

        var result = await sender.SendAsync(Message());

        Assert.False(result.IsSuccess);
        Assert.Equal(EmailFailureCodes.EmailNotConfigured, result.ErrorCode);
        Assert.Empty(handler.Requests);
        Assert.Contains(log.Lines, line => line.Level == LogLevel.Error && line.Line.Contains("RESEND_API_KEY"));
    }

    [Fact]
    public async Task A_provider_refusal_is_a_send_failed_result()
    {
        var handler = new StubHandler(_ => Json("""{"message":"invalid"}""", HttpStatusCode.UnprocessableEntity));
        var (sender, _) = Make(handler);

        var result = await sender.SendAsync(Message());

        Assert.False(result.IsSuccess);
        Assert.Equal(EmailFailureCodes.SendFailed, result.ErrorCode);
    }

    [Fact]
    public async Task An_accepted_message_without_an_id_is_a_send_failed_result()
    {
        var handler = new StubHandler(_ => Json("""{"accepted":true}"""));
        var (sender, _) = Make(handler);

        var result = await sender.SendAsync(Message());

        Assert.False(result.IsSuccess);
        Assert.Equal(EmailFailureCodes.SendFailed, result.ErrorCode);
    }

    [Fact]
    public async Task An_unreachable_provider_is_a_send_failed_result()
    {
        var (sender, _) = Make(new ThrowingHandler(new HttpRequestException("no route to host")));

        var result = await sender.SendAsync(Message());

        Assert.False(result.IsSuccess);
        Assert.Equal(EmailFailureCodes.SendFailed, result.ErrorCode);
    }

    [Fact]
    public async Task No_log_line_carries_the_recipient_subject_body_or_key()
    {
        var handler = new StubHandler(_ => Json("""{"message":"invalid"}""", HttpStatusCode.UnprocessableEntity));
        var (sender, log) = Make(handler);

        await sender.SendAsync(Message());

        Assert.NotEmpty(log.Lines);
        foreach (var (_, line) in log.Lines)
        {
            Assert.DoesNotContain(Recipient, line);
            Assert.DoesNotContain(Subject, line);
            Assert.DoesNotContain(TextBody, line);
            Assert.DoesNotContain(Key, line);
        }
    }
}
