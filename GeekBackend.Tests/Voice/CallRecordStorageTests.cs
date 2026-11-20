using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using GeekAPI.Services.Receptionist.Relay;
using GeekAPI.Services.Voice.Mcp;
using GeekAPI.Services.Voice.Storage;
using GeekApplication.Interfaces.Receptionist;
using GeekApplication.Interfaces.Voice;
using Microsoft.Extensions.Logging.Abstractions;
using GeekBackend.Tests.Visitors;
using GeekApplication.Models.Voice;
using GeekApplication.Results;
using GeekAPI.Services.Voice;
using Microsoft.Extensions.Time.Testing;

namespace GeekBackend.Tests.Voice;

/// <summary>
/// The plan's bring-your-own-storage: the object a call is written as, the path it goes to, and the dead-letter fallback that keeps a
/// failed write from losing the call.
/// </summary>
public sealed class CallRecordStorageTests
{
    private static readonly DateTimeOffset Ended = new(2026, 11, 19, 21, 4, 5, TimeSpan.Zero);

    private static readonly ReceptionistCallSummary Summary = new(
        "Booking request",
        "A caller wants a drain cleared on Friday.",
        [new ReceptionistSummaryField("Name", "Dana Reed"), new ReceptionistSummaryField("Callback", "+1 561 555 0133")],
        ["Took the details and promised a callback."],
        ["Whether Friday morning is free."]);

    private static FinishedCall Call(bool transcript = false, FinishedCallKind kind = FinishedCallKind.Summarized) =>
        new(kind,
            kind == FinishedCallKind.Summarized ? Summary : null,
            transcript ? [new ReceptionistTurn(ReceptionistSpeaker.Caller, "My drain is blocked.")] : null,
            Ended, TimeSpan.FromSeconds(95), "owner@example.test", "Dag Plumbing", "+15615550133");

    // ----- The path the plan sets out -----

    [Fact]
    public void The_path_is_the_plans_own_shape_in_utc()
    {
        Assert.Equal(
            $"2026/11/19/call-{Ended.ToUnixTimeSeconds()}-0133.json",
            CallRecordObjects.PathFor(Ended, "+15615550133", "json"));
    }

    [Fact]
    public void A_day_folder_means_one_thing_wherever_the_caller_was()
    {
        var sameInstantElsewhere = Ended.ToOffset(TimeSpan.FromHours(-11));

        Assert.Equal(
            CallRecordObjects.PathFor(Ended, "+15615550133", "json"),
            CallRecordObjects.PathFor(sameInstantElsewhere, "+15615550133", "json"));
    }

    [Theory]
    [InlineData(null, "unknown")]
    [InlineData("", "unknown")]
    [InlineData("anonymous", "unknown")]
    [InlineData("+1 (561) 555-0133", "0133")]
    [InlineData("133", "unknown")]
    public void A_caller_who_withheld_a_number_is_named_unknown_not_guessed(string? number, string expected) =>
        Assert.Equal(expected, CallRecordObjects.Last4(number));

    // ----- What goes in it -----

    [Fact]
    public void A_business_that_asked_for_transcripts_gets_json_with_every_turn()
    {
        var record = CallRecordObjects.Json(Call(transcript: true));

        Assert.Equal(CallRecordObjects.JsonContentType, record.ContentType);
        Assert.EndsWith(".json", record.Path, StringComparison.Ordinal);

        var json = JsonNode.Parse(record.Body)!;
        Assert.Equal("Booking request", json["outcome"]!.GetValue<string>());
        Assert.Equal(95, json["durationSeconds"]!.GetValue<int>());
        Assert.True(json["summaryAvailable"]!.GetValue<bool>());
        Assert.Equal("My drain is blocked.", json["transcript"]!.AsArray()[0]!["text"]!.GetValue<string>());
        Assert.Equal("Dana Reed", json["fields"]!.AsArray()[0]!["value"]!.GetValue<string>());
    }

    [Fact]
    public void A_business_that_asked_only_for_summaries_gets_the_short_report()
    {
        var record = CallRecordObjects.Markdown(Call());

        Assert.Equal(CallRecordObjects.MarkdownContentType, record.ContentType);
        Assert.EndsWith(".md", record.Path, StringComparison.Ordinal);
        Assert.Contains("**Outcome:** Booking request", record.Body, StringComparison.Ordinal);
        Assert.Contains("A caller wants a drain cleared on Friday.", record.Body, StringComparison.Ordinal);
        Assert.Contains("**Name:** Dana Reed", record.Body, StringComparison.Ordinal);
        Assert.Contains("Whether Friday morning is free.", record.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void A_call_whose_summary_could_not_be_written_says_so_and_invents_nothing()
    {
        var record = CallRecordObjects.Markdown(Call(kind: FinishedCallKind.SummaryUnavailable));

        Assert.Contains("**Outcome:** Summary unavailable", record.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("Booking request", record.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("## What the caller gave", record.Body, StringComparison.Ordinal);
    }

    // ----- The Drive writer -----

    private sealed class StubHandler(params (HttpStatusCode Status, string Body)[] replies) : HttpMessageHandler
    {
        private int _next;
        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct));
            var (status, body) = replies[Math.Min(_next++, replies.Length - 1)];
            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }

    private sealed class OneClient(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>A store that holds one connection, which is all a write needs, and refuses anything else a test has no business asking.</summary>
    private sealed class OneConnection(VoiceMcpConnection? connection) : IVoiceStore
    {
        public Task<Result<VoiceMcpConnection>> GetMcpConnectionAsync(Guid tenantId, string provider, CancellationToken ct = default) =>
            Task.FromResult(connection is null
                ? Result<VoiceMcpConnection>.Failure("No connection.", VoiceFailureCodes.NotFound)
                : Result<VoiceMcpConnection>.Success(connection));

        public Task<Result<VoiceMcpConnection>> SaveMcpConnectionAsync(Guid tenantId, string provider, SaveVoiceMcpConnectionCommand command, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<Result<VoiceTenant>> CreateTenantAsync(CreateVoiceTenantCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceTenant>> GetTenantAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceTenant>> ActivateTenantAsync(Guid tenantId, ActivateVoiceTenantCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceTelephony>> GetTelephonyAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceReceptionistConfig>> GetConfigAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceReceptionistConfig>> SaveConfigAsync(Guid tenantId, VoiceReceptionistConfig config, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceKnowledgeDocumentCreated>> CreateKnowledgeDocumentAsync(Guid tenantId, CreateVoiceKnowledgeDocumentCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<VoiceKnowledgeHit>>> SearchKnowledgeAsync(Guid tenantId, SearchVoiceKnowledgeCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceCallStored>> UpsertCallAsync(Guid tenantId, UpsertVoiceCallCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceAppointmentCreated>> CreateAppointmentAsync(Guid tenantId, CreateVoiceAppointmentCommand command, CancellationToken ct = default) => throw new NotSupportedException();
    }

    /// <summary>A base64 value of exactly the 32 bytes the protector requires.</summary>
    private const string Key = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";

    /// <summary>A Drive writer over a connection whose token is nowhere near expiring, so nothing is renewed and no OAuth call is made.</summary>
    private static (GoogleDriveCallRecordStorage Storage, StubHandler Handler) Drive(params (HttpStatusCode Status, string Body)[] replies)
    {
        var protector = new VoiceCredentialProtector(() => Key);
        var connection = new VoiceMcpConnection(
            Guid.NewGuid(),
            GoogleDriveOptions.Provider,
            protector.Protect("drive-access-token", VoiceSecretPurposes.McpAccessToken).Value!,
            protector.Protect("drive-refresh-token", VoiceSecretPurposes.McpRefreshToken).Value!,
            Ended.AddHours(1),
            null,
            IsActive: true);

        var tokens = new GoogleAccessTokens(
            new OneConnection(connection), protector, GoogleCalendarOptions.From(_ => null),
            null!, new FakeTimeProvider(Ended), NullLogger<GoogleAccessTokens>.Instance);

        var handler = new StubHandler(replies);
        return (new GoogleDriveCallRecordStorage(tokens, new OneClient(handler), NullLogger<GoogleDriveCallRecordStorage>.Instance), handler);
    }

    [Fact]
    public async Task A_record_goes_into_the_one_folder_at_the_plans_path()
    {
        var (storage, handler) = Drive(
            (HttpStatusCode.OK, """{"files":[{"id":"folder-1"}]}"""),
            (HttpStatusCode.OK, """{"id":"file-1"}"""));

        var written = await storage.WriteAsync(Guid.NewGuid(), CallRecordObjects.Markdown(Call()));

        Assert.True(written.IsSuccess, written.Error);
        Assert.Equal("file-1", written.Value);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("folder-1", handler.Bodies[1], StringComparison.Ordinal);
        Assert.Contains($"2026/11/19/call-{Ended.ToUnixTimeSeconds()}-0133.md", handler.Bodies[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_folder_is_made_the_first_time_and_not_again()
    {
        var (storage, handler) = Drive(
            (HttpStatusCode.OK, """{"files":[]}"""),
            (HttpStatusCode.OK, """{"id":"folder-new"}"""),
            (HttpStatusCode.OK, """{"id":"file-1"}"""));

        var written = await storage.WriteAsync(Guid.NewGuid(), CallRecordObjects.Markdown(Call()));

        Assert.True(written.IsSuccess, written.Error);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Contains(GoogleDriveOptions.FolderName, handler.Bodies[1], StringComparison.Ordinal);
        Assert.Contains(GoogleDriveOptions.FolderMimeType, handler.Bodies[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_revoked_connection_asks_for_authorization_again_rather_than_retrying()
    {
        var (storage, handler) = Drive((HttpStatusCode.Unauthorized, "{}"));

        var written = await storage.WriteAsync(Guid.NewGuid(), CallRecordObjects.Markdown(Call()));

        Assert.False(written.IsSuccess);
        Assert.Equal(McpFailureCodes.ReauthorizationRequired, written.ErrorCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_drive_that_refuses_the_write_fails_with_a_code_and_no_word_of_the_call()
    {
        var log = new CapturingLogger<GoogleDriveCallRecordStorage>();
        var protector = new VoiceCredentialProtector(() => Key);
        var connection = new VoiceMcpConnection(
            Guid.NewGuid(), GoogleDriveOptions.Provider,
            protector.Protect("drive-access-token", VoiceSecretPurposes.McpAccessToken).Value!,
            protector.Protect("drive-refresh-token", VoiceSecretPurposes.McpRefreshToken).Value!,
            Ended.AddHours(1), null, IsActive: true);
        var tokens = new GoogleAccessTokens(
            new OneConnection(connection), protector, GoogleCalendarOptions.From(_ => null),
            null!, new FakeTimeProvider(Ended), NullLogger<GoogleAccessTokens>.Instance);
        var handler = new StubHandler((HttpStatusCode.OK, """{"files":[{"id":"folder-1"}]}"""), (HttpStatusCode.InsufficientStorage, "{}"));
        var storage = new GoogleDriveCallRecordStorage(tokens, new OneClient(handler), log);

        var written = await storage.WriteAsync(Guid.NewGuid(), CallRecordObjects.Markdown(Call()));

        Assert.False(written.IsSuccess);
        Assert.Equal(McpFailureCodes.StorageWriteFailed, written.ErrorCode);
        Assert.DoesNotContain(log.Lines, l => l.Contains("Dana Reed", StringComparison.Ordinal) || l.Contains("drain", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_business_with_no_drive_connected_is_told_so_and_nothing_is_called()
    {
        var tokens = new GoogleAccessTokens(
            new OneConnection(null), new VoiceCredentialProtector(() => Key), GoogleCalendarOptions.From(_ => null),
            null!, new FakeTimeProvider(Ended), NullLogger<GoogleAccessTokens>.Instance);
        var handler = new StubHandler((HttpStatusCode.OK, "{}"));
        var storage = new GoogleDriveCallRecordStorage(tokens, new OneClient(handler), NullLogger<GoogleDriveCallRecordStorage>.Instance);

        var written = await storage.WriteAsync(Guid.NewGuid(), CallRecordObjects.Markdown(Call()));

        Assert.False(written.IsSuccess);
        Assert.Equal(McpFailureCodes.StorageNotConnected, written.ErrorCode);
        Assert.Empty(handler.Requests);
    }

    // ----- The dead-letter fallback -----

    private sealed class FakeStorage(Result<string> result) : ICallRecordStorage
    {
        public List<CallRecordObject> Written { get; } = new();
        public string Provider => GoogleDriveOptions.Provider;

        public Task<Result<string>> WriteAsync(Guid tenantId, CallRecordObject record, CancellationToken ct = default)
        {
            Written.Add(record);
            return Task.FromResult(result);
        }
    }

    private sealed class ThrowingStorage : ICallRecordStorage
    {
        public string Provider => GoogleDriveOptions.Provider;

        public Task<Result<string>> WriteAsync(Guid tenantId, CallRecordObject record, CancellationToken ct = default) =>
            throw new InvalidOperationException("the network went away");
    }

    [Fact]
    public void The_drive_scope_and_folder_are_the_ones_the_plan_names()
    {
        Assert.Equal("https://www.googleapis.com/auth/drive.file", GoogleDriveOptions.FileScope);
        Assert.Equal("Geek-Receptionist Logs", GoogleDriveOptions.FolderName);
    }

    [Fact]
    public void The_storage_port_can_only_write()
    {
        var methods = typeof(ICallRecordStorage).GetMethods().Select(m => m.Name).ToList();

        Assert.Equal(["WriteAsync"], methods.Where(m => !m.StartsWith("get_", StringComparison.Ordinal)));
    }
    // ----- Connecting a Drive, alongside the calendar -----

    [Fact]
    public void The_drive_connection_asks_for_one_scope_and_books_nothing()
    {
        var drive = GoogleConnections.For(GoogleDriveOptions.Provider);

        Assert.NotNull(drive);
        Assert.Equal([GoogleDriveOptions.FileScope], drive!.Scopes);
        Assert.Null(drive.ExternalId);
    }

    [Fact]
    public void The_calendar_connection_is_unchanged_by_drive_joining_it()
    {
        var calendar = GoogleConnections.For(GoogleCalendarOptions.Provider);

        Assert.NotNull(calendar);
        Assert.Equal(GoogleCalendarOptions.Scopes, calendar!.Scopes);
        Assert.Equal(GoogleCalendarOptions.PrimaryCalendarId, calendar.ExternalId);
    }

    [Fact]
    public void A_provider_that_is_not_a_google_connection_is_not_one()
    {
        Assert.Null(GoogleConnections.For("dropbox"));
        Assert.Null(GoogleConnections.For("GOOGLE_DRIVE"));
    }

    [Fact]
    public void Each_connection_is_sent_back_to_its_own_portal_page()
    {
        var options = GoogleCalendarOptions.From(name => name switch
        {
            "GOOGLE_OAUTH_CLIENT_ID" => "client",
            "GOOGLE_OAUTH_CLIENT_SECRET" => "secret",
            "PORTAL_BASE_URL" => "https://portal.example.test/",
            _ => null,
        });

        Assert.Equal("https://portal.example.test/oauth/mcp/google_calendar/callback", options.RedirectUriFor(GoogleCalendarOptions.Provider));
        Assert.Equal("https://portal.example.test/oauth/mcp/google_drive/callback", options.RedirectUriFor(GoogleDriveOptions.Provider));
        Assert.Equal(options.RedirectUri, options.RedirectUriFor(GoogleCalendarOptions.Provider));
    }

}
