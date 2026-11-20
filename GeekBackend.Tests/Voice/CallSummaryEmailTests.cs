using GeekAPI.Services.Voice.Email;
using GeekApplication.Interfaces.Receptionist;
using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;
using Microsoft.Extensions.Logging;

namespace GeekBackend.Tests.Voice;

/// <summary>
/// The email a business gets when a call ends: it is the only record of that call, so it carries the caller's number, what they wanted
/// and what the receptionist did, says plainly when a summary could not be written rather than inventing one, includes the call itself
/// only when the business asked for it, cannot be made to run anything a caller said, and leaves nothing of the call in a log.
/// </summary>
public sealed class CallSummaryEmailTests
{
    private const string Destination = "owner@example.test";
    private static readonly DateTimeOffset Ended = new(2026, 11, 19, 15, 30, 0, TimeSpan.Zero);

    private static readonly ReceptionistCallSummary Summary = new(
        "Callback requested",
        "Dana has a leaking water heater and wants a call back this afternoon.",
        [new ReceptionistSummaryField("Name", "Dana"), new ReceptionistSummaryField("Callback number", "561-555-0199")],
        ["Promised a callback this afternoon"],
        ["Whether you work on tankless heaters"]);

    private sealed class FakeSender(Result<string>? result = null) : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = new();

        public Task<Result<string>> SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            Sent.Add(message);
            return Task.FromResult(result ?? Result<string>.Success("email-1"));
        }
    }

    private sealed class Recorder : ILogger<EmailCallSummaryDelivery>
    {
        public List<string> Lines { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception));
    }

    private static FinishedCall Call(
        FinishedCallKind kind = FinishedCallKind.Summarized,
        ReceptionistCallSummary? summary = null,
        IReadOnlyList<ReceptionistTurn>? transcript = null,
        string? caller = "+15615550199") =>
        new(kind, kind == FinishedCallKind.Summarized ? summary ?? Summary : summary, transcript, Ended, TimeSpan.FromSeconds(95),
            Destination, "Dag Plumbing", caller);

    private static async Task<(EmailMessage Message, FakeSender Sender, Recorder Log)> SendAsync(FinishedCall call, Result<string>? result = null)
    {
        var sender = new FakeSender(result);
        var log = new Recorder();
        var delivered = await new EmailCallSummaryDelivery(sender, log).DeliverAsync(call);
        Assert.Equal(result?.IsSuccess ?? true, delivered.IsSuccess);
        return (sender.Sent.Single(), sender, log);
    }

    [Fact]
    public async Task A_summarised_call_carries_the_outcome_the_number_and_everything_the_caller_gave()
    {
        var (message, _, log) = await SendAsync(Call());

        Assert.Equal(Destination, message.To);
        Assert.Equal("[Callback requested] +15615550199 — Nov 19, 3:30 PM", message.Subject);

        foreach (var body in new[] { message.TextBody, message.HtmlBody! })
        {
            Assert.Contains("Dana has a leaking water heater", body, StringComparison.Ordinal);
            Assert.Contains("561-555-0199", body, StringComparison.Ordinal);
            Assert.Contains("Promised a callback this afternoon", body, StringComparison.Ordinal);
            Assert.Contains("Whether you work on tankless heaters", body, StringComparison.Ordinal);
            Assert.Contains("Dag Plumbing", body, StringComparison.Ordinal);
            Assert.Contains("1 min 35 sec", body, StringComparison.Ordinal);
        }

        // The caller's number is one tap on a phone, because this email is what the owner calls back from.
        Assert.Contains("""<a href="tel:+15615550199""", message.HtmlBody!, StringComparison.Ordinal);
        Assert.DoesNotContain(log.Lines, l => l.Contains("Dana", StringComparison.Ordinal) || l.Contains("0199", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_call_whose_summary_could_not_be_written_says_so_and_invents_nothing()
    {
        var (message, _, _) = await SendAsync(Call(FinishedCallKind.SummaryUnavailable));

        Assert.Equal("[Call record — summary unavailable] +15615550199 — Nov 19, 3:30 PM", message.Subject);
        Assert.Contains("could not be written", message.TextBody, StringComparison.Ordinal);
        Assert.Contains("Nothing has been invented in its place", message.HtmlBody!, StringComparison.Ordinal);
        // The lead is still deliverable: the number is there to call back.
        Assert.Contains("+15615550199", message.HtmlBody!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_call_with_no_caller_speech_says_that_and_a_withheld_number_is_named_as_withheld()
    {
        var (message, _, _) = await SendAsync(Call(FinishedCallKind.NoCallerSpeech, caller: null));

        Assert.Equal("[Call with no caller speech] caller withheld — Nov 19, 3:30 PM", message.Subject);
        Assert.Contains("said nothing of substance", message.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("tel:", message.HtmlBody!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_call_itself_appears_only_when_the_business_asked_for_it()
    {
        var (without, _, _) = await SendAsync(Call());
        Assert.DoesNotContain("The call", without.HtmlBody!, StringComparison.Ordinal);

        var (with, _, _) = await SendAsync(Call(transcript:
        [
            new ReceptionistTurn(ReceptionistSpeaker.Caller, "My water heater is leaking"),
            new ReceptionistTurn(ReceptionistSpeaker.ToolCall, """{"day":"Thursday"}""", "c1", "list_events"),
            new ReceptionistTurn(ReceptionistSpeaker.ToolResult, """{"busy":false}""", "c1", "list_events"),
            new ReceptionistTurn(ReceptionistSpeaker.Receptionist, "Thursday is free."),
        ]));

        Assert.Contains("My water heater is leaking", with.TextBody, StringComparison.Ordinal);
        Assert.Contains("Receptionist used list_events", with.TextBody, StringComparison.Ordinal);
        Assert.Contains("list_events answered", with.HtmlBody!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nothing_a_caller_said_can_run_in_the_owners_mail_client()
    {
        var nasty = new ReceptionistCallSummary(
            "Message taken",
            "<script>alert('x')</script> wants a quote",
            [new ReceptionistSummaryField("Name", "<img src=x onerror=alert(1)>")],
            ["<b>bold</b>"],
            ["\"quoted\" & 'single'"]);

        var (message, _, _) = await SendAsync(Call(summary: nasty, transcript: [new ReceptionistTurn(ReceptionistSpeaker.Caller, "<script>bad()</script>")]));

        // What makes it safe is that no tag can start: the text is still there, encoded, so the owner sees what the caller said.
        Assert.DoesNotContain("<script", message.HtmlBody!, StringComparison.Ordinal);
        Assert.DoesNotContain("<img", message.HtmlBody!, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>bold</b>", message.HtmlBody!, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(&#39;x&#39;)&lt;/script&gt;", message.HtmlBody!, StringComparison.Ordinal);
        Assert.Contains("&lt;img src=x onerror=alert(1)&gt;", message.HtmlBody!, StringComparison.Ordinal);
        Assert.Contains("&lt;b&gt;bold&lt;/b&gt;", message.HtmlBody!, StringComparison.Ordinal);
        Assert.Contains("&amp;", message.HtmlBody!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_send_that_fails_is_returned_with_its_code_and_logs_no_call_content()
    {
        var (_, _, log) = await SendAsync(Call(), Result<string>.Failure("provider refused", EmailFailureCodes.SendFailed));

        Assert.Contains(log.Lines, l => l.Contains(EmailFailureCodes.SendFailed, StringComparison.Ordinal));
        Assert.DoesNotContain(log.Lines, l => l.Contains("Dana", StringComparison.Ordinal) || l.Contains(Destination, StringComparison.Ordinal));
    }
}
