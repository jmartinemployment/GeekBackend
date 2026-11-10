using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using GeekAPI.Services.Receptionist;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GeekBackend.Tests.Receptionist;

public sealed class FakeSms : ISmsSender
{
    public ConcurrentQueue<(string To, string Body)> Sent { get; } = new();
    public bool Fail { get; set; }

    public Task<SmsResult> SendAsync(string to, string body, CancellationToken ct)
    {
        if (Fail && to != Settings.Owner)
            return Task.FromResult(new SmsResult(false, null, "Twilio 30034: unregistered number"));
        Sent.Enqueue((to, body));
        return Task.FromResult(new SmsResult(true, "SM" + Guid.NewGuid().ToString("N"), null));
    }

    public List<(string To, string Body)> To(string number) => Sent.Where(m => m.To == number).ToList();
}

public sealed class FakeMediaDeleter : IMediaDeleter
{
    public ConcurrentQueue<(string MessageSid, string Url)> Deleted { get; } = new();

    public Task DeleteAsync(string messageSid, string mediaUrl, CancellationToken ct)
    {
        Deleted.Enqueue((messageSid, mediaUrl));
        return Task.CompletedTask;
    }
}

public static class Settings
{
    public const string Token = "test-auth-token";
    public const string Base = "https://receptionist.example.com";
    public const string Owner = "+15615550100";
    public const string TwilioNumber = "+15615550199";
    public const string Caller = "+15615551234";
}

/// <summary>
/// GeekAPI's receptionist endpoints on a TestServer: only AddReceptionist/MapReceptionist, so the
/// tests need none of GeekAPI's other configuration (REPO_API_KEY, GEEK_OAUTH_AUTHORITY, ...).
/// </summary>
public sealed class App : IDisposable
{
    private readonly WebApplication _app;
    public FakeSms Sms { get; } = new();
    public FakeMediaDeleter Media { get; } = new();

    public App(Dictionary<string, string?>? overrides = null, bool configured = true)
    {
        var settings = configured
            ? new Dictionary<string, string?>
            {
                ["TWILIO_ACCOUNT_SID"] = "ACtest",
                ["TWILIO_AUTH_TOKEN"] = Settings.Token,
                ["TWILIO_API_KEY_SID"] = "SKtest",
                ["TWILIO_API_KEY_SECRET"] = "test-api-key-secret",
                ["TWILIO_NUMBER"] = Settings.TwilioNumber,
                ["OWNER_CELL"] = Settings.Owner,
                ["COMPANY_NAME"] = "Dag Plumbing",
                ["PUBLIC_BASE_URL"] = Settings.Base,
                ["CALL_MODE"] = "Dial",
                ["SCREEN_CALLS"] = "true",
            }
            : new Dictionary<string, string?>();
        foreach (var (k, v) in overrides ?? new()) settings[k] = v;

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddReceptionist(builder.Configuration);
        builder.Services.Replace(ServiceDescriptor.Singleton<ISmsSender>(Sms));
        builder.Services.Replace(ServiceDescriptor.Singleton<IMediaDeleter>(Media));

        _app = builder.Build();
        _app.MapReceptionist();
        _app.StartAsync().GetAwaiter().GetResult();
    }

    public ReceptionistStatus Status => _app.Services.GetRequiredService<ReceptionistStatus>();

    public HttpClient CreateClient() => _app.GetTestClient();

    /// <summary>POSTs a form signed exactly the way Twilio signs webhooks.</summary>
    public async Task<(HttpStatusCode Status, string Body)> PostTwilio(string pathAndQuery, Dictionary<string, string> form, bool sign = true)
    {
        pathAndQuery = ReceptionistEndpoints.RoutePrefix + pathAndQuery;
        using var request = new HttpRequestMessage(HttpMethod.Post, pathAndQuery) { Content = new FormUrlEncodedContent(form) };
        if (sign) request.Headers.Add("X-Twilio-Signature", Sign(Settings.Base + pathAndQuery, form));
        var response = await CreateClient().SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static string Sign(string url, Dictionary<string, string> form)
    {
        var data = new StringBuilder(url);
        foreach (var key in form.Keys.OrderBy(k => k, StringComparer.Ordinal)) data.Append(key).Append(form[key]);
        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(Settings.Token));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(data.ToString())));
    }

    public void Dispose() => _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
}

public class VoiceWebhookTests
{
    private static Dictionary<string, string> Call(string sid = "CA1", string from = Settings.Caller, string? dialStatus = null)
    {
        var form = new Dictionary<string, string> { ["CallSid"] = sid, ["From"] = from, ["To"] = Settings.TwilioNumber };
        if (dialStatus is not null) form["DialCallStatus"] = dialStatus;
        return form;
    }

    [Fact]
    public async Task Incoming_call_rings_owner_with_screening_and_timeout()
    {
        using var app = new App();
        var (status, xml) = await app.PostTwilio("/voice/incoming", Call());

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("<Dial", xml);
        Assert.Contains("timeout=\"18\"", xml);
        Assert.Contains($"{Settings.Base}/api/receptionist/twilio/voice/dial-complete", xml);
        Assert.Contains(Settings.Owner, xml);
        Assert.Contains("/api/receptionist/twilio/voice/screen?parent=CA1", xml);
        Assert.Empty(app.Sms.Sent);
    }

    [Fact]
    public async Task Unanswered_call_texts_caller_notifies_owner_and_offers_voicemail()
    {
        using var app = new App();
        var (_, xml) = await app.PostTwilio("/voice/dial-complete", Call(dialStatus: "no-answer"));

        var callerText = Assert.Single(app.Sms.To(Settings.Caller));
        Assert.StartsWith("Hi, this is Dag Plumbing.", callerText.Body);
        Assert.Contains("Auto-text sent", Assert.Single(app.Sms.To(Settings.Owner)).Body);
        Assert.Contains("<Record", xml);
        Assert.Contains("/voice/voicemail-complete", xml);
    }

    [Fact]
    public async Task Owner_voicemail_picking_up_still_counts_as_missed_when_screening()
    {
        // Carrier voicemail "answers" → DialCallStatus completed, but nobody pressed a key.
        using var app = new App();
        await app.PostTwilio("/voice/dial-complete", Call(dialStatus: "completed"));

        Assert.Single(app.Sms.To(Settings.Caller));
    }

    [Fact]
    public async Task Owner_accepting_the_call_sends_no_text()
    {
        using var app = new App();
        var (screenStatus, screenXml) = await app.PostTwilio("/voice/screen?parent=CA1", Call("CAchild"));
        Assert.Equal(HttpStatusCode.OK, screenStatus);
        Assert.Contains("<Gather", screenXml);
        Assert.Contains("Call for Dag Plumbing", screenXml);

        await app.PostTwilio("/voice/screen-accepted?parent=CA1", new() { ["CallSid"] = "CAchild", ["Digits"] = "1" });
        var (_, xml) = await app.PostTwilio("/voice/dial-complete", Call(dialStatus: "completed"));

        Assert.Empty(app.Sms.Sent);
        Assert.Contains("<Hangup", xml);
    }

    [Fact]
    public async Task Without_screening_completed_dial_counts_as_answered()
    {
        using var app = new App(new() { ["SCREEN_CALLS"] = "false" });
        var (_, incoming) = await app.PostTwilio("/voice/incoming", Call());
        Assert.DoesNotContain("/screen", incoming);

        await app.PostTwilio("/voice/dial-complete", Call(dialStatus: "completed"));
        Assert.Empty(app.Sms.Sent);

        await app.PostTwilio("/voice/dial-complete", Call("CA2", dialStatus: "busy"));
        Assert.Single(app.Sms.To(Settings.Caller));
    }

    [Fact]
    public async Task Repeat_caller_is_texted_only_once_within_window()
    {
        using var app = new App();
        await app.PostTwilio("/voice/dial-complete", Call("CA1", dialStatus: "no-answer"));
        await app.PostTwilio("/voice/dial-complete", Call("CA2", dialStatus: "no-answer"));

        Assert.Single(app.Sms.To(Settings.Caller));
        Assert.Contains(app.Sms.To(Settings.Owner), m => m.Body.Contains("called again"));
    }

    [Fact]
    public async Task Forwarded_mode_texts_immediately_without_ringing_owner()
    {
        using var app = new App(new() { ["CALL_MODE"] = "Forwarded" });
        var (_, xml) = await app.PostTwilio("/voice/incoming", Call());

        Assert.DoesNotContain("<Dial", xml);
        Assert.Contains("<Record", xml);
        Assert.Single(app.Sms.To(Settings.Caller));
    }

    [Fact]
    public async Task Texted_caller_hears_the_registered_consent_disclosure()
    {
        using var app = new App(new() { ["CALL_MODE"] = "Forwarded" });
        var (_, xml) = await app.PostTwilio("/voice/incoming", Call());

        Assert.Contains("Dag Plumbing is texting you at this number about your call.", xml);
        Assert.Contains("Message and data rates may apply. Reply STOP to opt out.", xml);
    }

    [Fact]
    public async Task Caller_not_texted_hears_no_texting_claim()
    {
        using var app = new App(new() { ["CALL_MODE"] = "Forwarded" });
        await app.PostTwilio("/voice/incoming", Call("CA1"));
        var (_, xml) = await app.PostTwilio("/voice/incoming", Call("CA2"));

        Assert.DoesNotContain("texting you", xml);
        Assert.Contains("Please leave a message after the tone.", xml);
        Assert.Contains("<Record", xml);
    }

    [Fact]
    public async Task Blocked_caller_id_notifies_owner_only()
    {
        using var app = new App();
        await app.PostTwilio("/voice/dial-complete", Call(from: "Anonymous", dialStatus: "no-answer"));

        Assert.Empty(app.Sms.To("Anonymous"));
        Assert.Contains("blocked number", Assert.Single(app.Sms.To(Settings.Owner)).Body);
    }

    [Fact]
    public async Task International_caller_is_not_texted()
    {
        using var app = new App();
        await app.PostTwilio("/voice/dial-complete", Call(from: "+447700900123", dialStatus: "no-answer"));

        Assert.Empty(app.Sms.To("+447700900123"));
        Assert.Contains("international", Assert.Single(app.Sms.To(Settings.Owner)).Body);
    }

    [Fact]
    public async Task Failed_text_is_reported_to_owner_not_hidden()
    {
        using var app = new App();
        app.Sms.Fail = true;
        await app.PostTwilio("/voice/dial-complete", Call(dialStatus: "no-answer"));

        var owner = Assert.Single(app.Sms.To(Settings.Owner));
        Assert.Contains("FAILED", owner.Body);
        Assert.Contains("30034", owner.Body);
    }

    [Fact]
    public async Task Voicemail_link_is_sent_to_owner()
    {
        using var app = new App();
        var form = Call();
        form["RecordingUrl"] = "https://api.twilio.com/recordings/RE1";
        form["RecordingDuration"] = "14";
        await app.PostTwilio("/voice/voicemail-complete", form);

        var owner = Assert.Single(app.Sms.To(Settings.Owner));
        Assert.Contains("RE1", owner.Body);
        Assert.Contains("14s", owner.Body);
    }

    [Fact]
    public async Task Unsigned_or_forged_requests_are_rejected()
    {
        using var app = new App();
        var (unsigned, _) = await app.PostTwilio("/voice/dial-complete", Call(dialStatus: "no-answer"), sign: false);
        Assert.Equal(HttpStatusCode.Forbidden, unsigned);

        var client = app.CreateClient();
        using var forged = new HttpRequestMessage(HttpMethod.Post, ReceptionistEndpoints.RoutePrefix + "/voice/dial-complete")
        { Content = new FormUrlEncodedContent(Call(dialStatus: "no-answer")) };
        forged.Headers.Add("X-Twilio-Signature", "bm90LWEtcmVhbC1zaWduYXR1cmU=");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(forged)).StatusCode);

        Assert.Empty(app.Sms.Sent);
    }

    [Fact]
    public async Task Without_twilio_settings_the_receptionist_is_off_and_answers_503()
    {
        using var app = new App(configured: false);
        Assert.False(app.Status.Enabled);
        var (status, _) = await app.PostTwilio("/voice/incoming", Call());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Empty(app.Sms.Sent);
    }

    [Fact]
    public async Task Invalid_configuration_lists_every_problem_and_answers_503_without_stopping_the_host()
    {
        using var app = new App(new()
        {
            ["OWNER_CELL"] = "561-555-0100", ["COMPANY_NAME"] = "", ["CALL_MODE"] = "Sometimes",
            ["TWILIO_API_KEY_SID"] = "ACwrong", ["TWILIO_API_KEY_SECRET"] = "",
        });

        Assert.False(app.Status.Enabled);
        var message = app.Status.Reason;
        Assert.Contains("OWNER_CELL must be E.164", message);
        Assert.Contains("COMPANY_NAME is required", message);
        Assert.Contains("CALL_MODE must be one of", message);
        Assert.Contains("TWILIO_API_KEY_SID must start with 'SK'", message);
        Assert.Contains("TWILIO_API_KEY_SECRET is required", message);

        var (status, _) = await app.PostTwilio("/voice/incoming", Call());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
    }
}
