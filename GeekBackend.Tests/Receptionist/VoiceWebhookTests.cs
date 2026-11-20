using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using GeekAPI.Services.Receptionist;
using GeekAPI.Services.Receptionist.Relay;
using GeekApplication.Interfaces.Receptionist;
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

    public App(Dictionary<string, string?>? overrides = null, bool configured = true, IReceptionistModel? model = null)
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
        builder.Services.AddReceptionistRelay(_ => null);
        if (model is not null) builder.Services.Replace(ServiceDescriptor.Singleton(model));
        builder.Services.Replace(ServiceDescriptor.Singleton<ISmsSender>(Sms));
        builder.Services.Replace(ServiceDescriptor.Singleton<IMediaDeleter>(Media));

        _app = builder.Build();
        _app.UseWebSockets();
        _app.MapReceptionist();
        _app.StartAsync().GetAwaiter().GetResult();
    }

    public ReceptionistStatus Status => _app.Services.GetRequiredService<ReceptionistStatus>();

    public HttpClient CreateClient() => _app.GetTestClient();

    public TestServer Server => _app.GetTestServer();

    /// <summary>Signs an address with no parameters, as a WebSocket handshake is checked.</summary>
    public static string SignUrl(string url) => Sign(url, new Dictionary<string, string>());

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
    public async Task Unanswered_call_hears_the_disclosure_and_is_handed_to_the_ai_with_no_recording_and_no_text()
    {
        using var app = new App(model: new FakeReceptionistModel());
        var (_, xml) = await app.PostTwilio("/voice/dial-complete", Call(dialStatus: "no-answer"));

        Assert.Contains("Calls are processed in real time and not recorded.", xml, StringComparison.Ordinal);
        Assert.Contains("<ConversationRelay", xml);
        Assert.DoesNotContain("<Gather", xml);
        Assert.DoesNotContain("<Record", xml);
        Assert.Empty(app.Sms.Sent);
    }

    [Fact]
    public async Task Owner_voicemail_picking_up_still_counts_as_missed_when_screening()
    {
        // Carrier voicemail "answers" -> DialCallStatus completed, but nobody pressed a key.
        using var app = new App();
        var (_, xml) = await app.PostTwilio("/voice/dial-complete", Call(dialStatus: "completed"));

        Assert.Contains("We can", xml);
        Assert.Empty(app.Sms.Sent);
    }

    [Fact]
    public async Task Owner_accepting_the_call_hears_no_closing_message()
    {
        using var app = new App();
        var (screenStatus, screenXml) = await app.PostTwilio("/voice/screen?parent=CA1", Call("CAchild"));
        Assert.Equal(HttpStatusCode.OK, screenStatus);
        Assert.Contains("<Gather", screenXml);
        Assert.Contains("Call for Dag Plumbing", screenXml);

        await app.PostTwilio("/voice/screen-accepted?parent=CA1", new() { ["CallSid"] = "CAchild", ["Digits"] = "1" });
        var (_, xml) = await app.PostTwilio("/voice/dial-complete", Call(dialStatus: "completed"));

        Assert.DoesNotContain("We can", xml);
        Assert.Contains("<Hangup", xml);
    }

    [Fact]
    public async Task Without_screening_completed_dial_counts_as_answered()
    {
        using var app = new App(new() { ["SCREEN_CALLS"] = "false" });
        var (_, incoming) = await app.PostTwilio("/voice/incoming", Call());
        Assert.DoesNotContain("/screen", incoming);

        var (_, answered) = await app.PostTwilio("/voice/dial-complete", Call(dialStatus: "completed"));
        Assert.DoesNotContain("We can", answered);

        var (_, busy) = await app.PostTwilio("/voice/dial-complete", Call("CA2", dialStatus: "busy"));
        Assert.Contains("We can", busy);
    }

    [Fact]
    public async Task Forwarded_mode_ends_the_call_without_ringing_the_owner_or_recording()
    {
        using var app = new App(new() { ["CALL_MODE"] = "Forwarded" });
        var (_, xml) = await app.PostTwilio("/voice/incoming", Call("CA9"));

        Assert.DoesNotContain("<Dial", xml);
        Assert.DoesNotContain("<Record", xml);
        Assert.Contains("<Hangup", xml);
        Assert.Empty(app.Sms.Sent);
    }

    [Fact]
    public async Task There_is_no_voicemail_route()
    {
        using var app = new App();
        var (status, _) = await app.PostTwilio("/voice/voicemail-complete", Call());
        Assert.Equal(HttpStatusCode.NotFound, status);
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
