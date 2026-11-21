using System.Security.Claims;
using System.Text.Json;
using GeekAPI.Controllers.Voice;
using GeekAPI.Services.Voice;
using GeekAPI.Services.Voice.Mcp;
using GeekAPI.Services.Voice.Onboarding;
using GeekAPI.Services.Voice.OwnerAuth;
using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace GeekBackend.Tests.Voice;

/// <summary>
/// The only writer of a whole receptionist config, and the two doors onto it. Jeff, 2026-11-20: <i>"Any and/or every setting needs to
/// be included in the onboarding multi-step form. The application may have preset defaults, but I am not comfortable making decisions
/// on behalf of others."</i>
///
/// <para>
/// Before this existed, fourteen of the sixteen settings had no write path at any status: the defaults were written once at signup and
/// could never be changed by the business or by anyone else. So the thing these tests pin hardest is that a step which answers one
/// question changes that one setting and leaves the other fourteen exactly as the business left them - a merge that dropped a field
/// would silently reconfigure a receptionist nobody asked it to touch, and it would look like a successful save.
/// </para>
///
/// <para>
/// The rest: null means "not asked" and empty means "none", and those are different answers; the signup door refuses a business that
/// is already live, because the caller there is a server key and not an owner; the owner's door refuses another business's id on both
/// verbs; validation is never duplicated, so a refusal from the store comes back with the store's own code; and no log line carries a
/// greeting, a script, a transfer number or a destination address.
/// </para>
/// </summary>
public sealed class ReceptionistConfigTests
{
    private static readonly Guid Tenant = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid OtherTenant = Guid.Parse("99999999-8888-7777-6666-555555555555");
    private static readonly Guid Knowledge = Guid.Parse("11111111-2222-3333-4444-555555555555");

    /// <summary>
    /// A real protector with a real key, because two of the sixteen settings are stored sealed and a fake that passed strings through
    /// would test a merge the product does not run. The key is this run's own; nothing here depends on its value.
    /// </summary>
    private static readonly string Key = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    private static readonly VoiceCredentialProtector Protector = new(() => Key);

    /// <summary>
    /// Sealed once, here, and reused. Every seal draws a fresh nonce, so sealing the same number twice gives two different strings:
    /// a fixture that sealed per call would compare one ciphertext against another and fail on every row for the wrong reason.
    /// </summary>
    private static readonly string TransferNumberSealed =
        Protector.Protect(TransferNumber, VoiceSecretPurposes.ConfigTransferTargetNumber).Value!;

    private static readonly string DestinationEmailSealed =
        Protector.Protect(DestinationEmail, VoiceSecretPurposes.ConfigMessageDestinationEmail).Value!;

    // Sentinels, so a lost field and a leaked field are both visible by name rather than by a value that could come from anywhere.
    private const string Greeting = "GREETING-SENTINEL: Thanks for calling Acme Plumbing.";
    private const string SystemPrompt = "PROMPT-SENTINEL: You are the receptionist for Acme Plumbing.";
    private const string TransferNumber = "+15615550137";
    private const string DestinationEmail = "owner@acme-plumbing.example";
    private const string BusinessHoursJson = """{"monday":"HOURS-SENTINEL 08:00-17:00"}""";
    private const string QualifyingQuestionsJson = """{"first":"QUESTION-SENTINEL What is the address of the leak?"}""";
    private const string EmergencyKeyword = "KEYWORD-SENTINEL flooding";

    // ----- The rig -----

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Lines { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception) + (exception?.ToString() ?? string.Empty));
    }

    private sealed class Store : IVoiceStore
    {
        public Result<VoiceReceptionistConfig> Config { get; set; } = Result<VoiceReceptionistConfig>.Success(Configured());

        public Result<VoiceReceptionistConfig>? SaveConfigResult { get; set; }

        public Result<VoiceTenant> Tenant { get; set; } = Result<VoiceTenant>.Success(TenantAt(VoiceTenantStatuses.PendingPayment));

        public List<Guid> ConfigReads { get; } = new();
        public List<Guid> TenantReads { get; } = new();
        public List<VoiceReceptionistConfig> SavedConfigs { get; } = new();

        public Task<Result<VoiceReceptionistConfig>> GetConfigAsync(Guid tenantId, CancellationToken ct = default)
        {
            ConfigReads.Add(tenantId);
            return Task.FromResult(Config);
        }

        public Task<Result<VoiceReceptionistConfig>> SaveConfigAsync(Guid tenantId, VoiceReceptionistConfig config, CancellationToken ct = default)
        {
            SavedConfigs.Add(config);
            return Task.FromResult(SaveConfigResult ?? Result<VoiceReceptionistConfig>.Success(config));
        }

        public Task<Result<VoiceTenant>> GetTenantAsync(Guid tenantId, CancellationToken ct = default)
        {
            TenantReads.Add(tenantId);
            return Task.FromResult(Tenant);
        }

        // Everything else this path has no business reaching for throws rather than quietly answering.
        public Task<Result<VoiceTenant>> CreateTenantAsync(CreateVoiceTenantCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceTenant>> ActivateTenantAsync(Guid tenantId, ActivateVoiceTenantCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceTelephony>> GetTelephonyAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        /// <summary>
        /// The business's calendar connection, which only booking asks about. No connection is the default, because that is what a
        /// business has until it connects one - and turning booking on is the one answer that is refused for it.
        /// </summary>
        public Result<VoiceMcpConnection> Connection { get; set; } =
            Result<VoiceMcpConnection>.Failure("No calendar is connected.", VoiceFailureCodes.NotFound);

        public Task<Result<VoiceMcpConnection>> GetMcpConnectionAsync(Guid tenantId, string provider, CancellationToken ct = default) =>
            Task.FromResult(Connection);
        public Task<Result<VoiceMcpConnection>> SaveMcpConnectionAsync(Guid tenantId, string provider, SaveVoiceMcpConnectionCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceStorageConnection>> GetStorageConnectionAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceStorageConnection>> SaveStorageConnectionAsync(Guid tenantId, SaveVoiceStorageConnectionCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceConnectionRemoved>> DisconnectMcpAsync(Guid tenantId, string provider, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceConnectionRemoved>> DisconnectStorageAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceCallStored>> UpsertCallAsync(Guid tenantId, UpsertVoiceCallCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceAppointmentCreated>> CreateAppointmentAsync(Guid tenantId, CreateVoiceAppointmentCommand command, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static VoiceTenant TenantAt(string status) => new(
        Tenant,
        "Acme Plumbing",
        "home_services",
        DateTimeOffset.UnixEpoch,
        "US",
        "FL",
        "contact@acme-plumbing.example",
        "+15615550199",
        status,
        DateTimeOffset.UnixEpoch);

    /// <summary>A business with an answer in every one of the sixteen settings, so a dropped field is a changed value and not a null.</summary>
    private static VoiceReceptionistConfig Configured()
    {
        using var hours = JsonDocument.Parse(BusinessHoursJson);
        using var questions = JsonDocument.Parse(QualifyingQuestionsJson);
        return new VoiceReceptionistConfig(
            Greeting,
            SystemPrompt,
            hours.RootElement.Clone(),
            questions.RootElement.Clone(),
            VoiceTransferModes.WarmTransfer,
            TransferNumberSealed,
            VoiceTransferCallerIdModes.OriginalCaller,
            [EmergencyKeyword, "fire"],
            RecordingDisclosureEnabled: true,
            MessageEmailEnabled: true,
            MessageDestinationEmailEncrypted: DestinationEmailSealed,
            IncludeTranscript: true,
            NotifyOnAbandonedCalls: true,
            BookingEnabled: true,
            BookingRequested: true,
            ConsentMode: VoiceConsentModes.StrictKeypress,
            KnowledgeRunId: Knowledge);
    }

    private static JsonElement Json(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }

    /// <summary>What a stored personal detail actually is, which is the only way to assert on one.</summary>
    private static string Opened(string? stored, string purpose) => Protector.Unprotect(stored!, purpose).Value!;

    private static string Serialized(object? value) => JsonSerializer.Serialize(value);

    /// <summary>
    /// The gateway behind the calendar check, which answers only when a test gives this business a connection. Booking turned on is
    /// the one answer refused for something outside the answer, so the check is run for real here rather than stubbed away: the
    /// product asks the business's calendar the same question a telephone call would.
    /// </summary>
    private sealed class GatewayHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(respond());
        }
    }

    private static readonly GoogleCalendarOptions CalendarOptions =
        new("client-id.apps.googleusercontent.com", "client-secret", "https://receptionist.geekatyourspot.example");

    /// <summary>A connected, working calendar for this business: a token that is not near expiring, and a gateway that answers.</summary>
    private static void GiveACalendar(Store store) => store.Connection = Result<VoiceMcpConnection>.Success(new VoiceMcpConnection(
        Tenant,
        GoogleCalendarOptions.Provider,
        Protector.Protect("google-access-token", VoiceSecretPurposes.McpAccessToken).Value!,
        Protector.Protect("google-refresh-token", VoiceSecretPurposes.McpRefreshToken).Value!,
        DateTimeOffset.UtcNow.AddHours(1),
        "primary",
        IsActive: true));

    private static ReceptionistConfigWriter Writer(Store store, GatewayHandler? gateway = null)
    {
        var handler = gateway ?? new GatewayHandler(() => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"tool":"list_calendars","content":[{"type":"text","text":"ok"}],"structuredContent":{"calendars":[{"id":"primary"}]}}""",
                System.Text.Encoding.UTF8,
                "application/json"),
        });

        var tokens = new GoogleAccessTokens(
            store,
            Protector,
            CalendarOptions,
            null!,
            TimeProvider.System,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<GoogleAccessTokens>.Instance);

        var calendar = new McpTools(tokens, new McpGatewayClient(
            new HttpClient(handler),
            new McpGatewayOptions("http://geek-mcp-gateway.railway.internal:3000", "gateway-key"),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<McpGatewayClient>.Instance));

        return new ReceptionistConfigWriter(store, Protector, calendar);
    }

    private static ReceptionistConfigController OwnerController(
        Store store, CapturingLogger<ReceptionistConfigController> log, Guid? signedInAs = null) =>
        new(store, Writer(store), log)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(
                        new ClaimsIdentity([new Claim(ReceptionistAuth.TenantClaim, (signedInAs ?? Tenant).ToString())], "test")),
                },
            },
        };

    // ----- One answer changes one setting -----

    /// <summary>
    /// The heart of it, one row per setting. Each row answers exactly one of the fifteen the form can ask and then asserts the other
    /// fourteen are byte-for-byte what the business had, plus the crawl id the form never touches. A merge that forgot a field would
    /// pass a narrower test and lose a business its greeting.
    /// </summary>
    public static TheoryData<ReceptionistConfigAnswers, string> OneAnswerEach()
    {
        var data = new TheoryData<ReceptionistConfigAnswers, string>
        {
            { new ReceptionistConfigAnswers(GreetingMessage: "Acme Plumbing, how can I help?"), nameof(VoiceReceptionistConfig.GreetingMessage) },
            { new ReceptionistConfigAnswers(SystemPromptTemplate: "You answer for a plumber."), nameof(VoiceReceptionistConfig.SystemPromptTemplate) },
            { new ReceptionistConfigAnswers(BusinessHours: Json("""{"tuesday":"09:00-18:00"}""")), nameof(VoiceReceptionistConfig.BusinessHours) },
            { new ReceptionistConfigAnswers(QualifyingQuestions: Json("""["What is the address?"]""")), nameof(VoiceReceptionistConfig.QualifyingQuestions) },
            { new ReceptionistConfigAnswers(TransferMode: VoiceTransferModes.ColdTransfer), nameof(VoiceReceptionistConfig.TransferMode) },
            { new ReceptionistConfigAnswers(TransferTargetNumber: "+15615550123"), nameof(VoiceReceptionistConfig.TransferTargetNumberEncrypted) },
            { new ReceptionistConfigAnswers(TransferCallerIdMode: VoiceTransferCallerIdModes.AssignedAiNumber), nameof(VoiceReceptionistConfig.TransferCallerIdMode) },
            { new ReceptionistConfigAnswers(EmergencyKeywords: ["burst pipe"]), nameof(VoiceReceptionistConfig.EmergencyKeywords) },
            { new ReceptionistConfigAnswers(RecordingDisclosureEnabled: false), nameof(VoiceReceptionistConfig.RecordingDisclosureEnabled) },
            { new ReceptionistConfigAnswers(MessageEmailEnabled: false), nameof(VoiceReceptionistConfig.MessageEmailEnabled) },
            { new ReceptionistConfigAnswers(MessageDestinationEmail: "dispatch@acme-plumbing.example"), nameof(VoiceReceptionistConfig.MessageDestinationEmailEncrypted) },
            { new ReceptionistConfigAnswers(IncludeTranscript: false), nameof(VoiceReceptionistConfig.IncludeTranscript) },
            { new ReceptionistConfigAnswers(NotifyOnAbandonedCalls: false), nameof(VoiceReceptionistConfig.NotifyOnAbandonedCalls) },
            { new ReceptionistConfigAnswers(BookingEnabled: false), nameof(VoiceReceptionistConfig.BookingEnabled) },
            { new ReceptionistConfigAnswers(BookingRequested: false), nameof(VoiceReceptionistConfig.BookingRequested) },
            { new ReceptionistConfigAnswers(ConsentMode: VoiceConsentModes.PassiveDisclosure), nameof(VoiceReceptionistConfig.ConsentMode) },
        };
        return data;
    }

    [Theory]
    [MemberData(nameof(OneAnswerEach))]
    public async Task One_setting_answered_changes_that_setting_and_leaves_the_other_fourteen_as_the_business_left_them(
        ReceptionistConfigAnswers answers, string changed)
    {
        var store = new Store();

        var saved = await Writer(store)
            .ApplyDuringSignupAsync(new ReceptionistConfigStepRequest(Tenant, answers), CancellationToken.None);

        Assert.True(saved.IsSuccess);
        var written = Assert.Single(store.SavedConfigs);

        // Exactly one setting was named. Its name on the answers record is the plaintext one the form sends
        // (TransferTargetNumber), and on the config it is the stored one (TransferTargetNumberEncrypted), so the two are compared by
        // which setting moved rather than by a shared string.
        Assert.Single(answers.AnsweredFields);

        var untouched = new Dictionary<string, Func<VoiceReceptionistConfig, object?>>(StringComparer.Ordinal)
        {
            [nameof(VoiceReceptionistConfig.GreetingMessage)] = c => c.GreetingMessage,
            [nameof(VoiceReceptionistConfig.SystemPromptTemplate)] = c => c.SystemPromptTemplate,
            [nameof(VoiceReceptionistConfig.BusinessHours)] = c => c.BusinessHours.GetRawText(),
            [nameof(VoiceReceptionistConfig.QualifyingQuestions)] = c => c.QualifyingQuestions.GetRawText(),
            [nameof(VoiceReceptionistConfig.TransferMode)] = c => c.TransferMode,
            [nameof(VoiceReceptionistConfig.TransferTargetNumberEncrypted)] = c => c.TransferTargetNumberEncrypted,
            [nameof(VoiceReceptionistConfig.TransferCallerIdMode)] = c => c.TransferCallerIdMode,
            [nameof(VoiceReceptionistConfig.EmergencyKeywords)] = c => string.Join("|", c.EmergencyKeywords),
            [nameof(VoiceReceptionistConfig.RecordingDisclosureEnabled)] = c => c.RecordingDisclosureEnabled,
            [nameof(VoiceReceptionistConfig.MessageEmailEnabled)] = c => c.MessageEmailEnabled,
            [nameof(VoiceReceptionistConfig.MessageDestinationEmailEncrypted)] = c => c.MessageDestinationEmailEncrypted,
            [nameof(VoiceReceptionistConfig.IncludeTranscript)] = c => c.IncludeTranscript,
            [nameof(VoiceReceptionistConfig.NotifyOnAbandonedCalls)] = c => c.NotifyOnAbandonedCalls,
            [nameof(VoiceReceptionistConfig.BookingEnabled)] = c => c.BookingEnabled,
            [nameof(VoiceReceptionistConfig.BookingRequested)] = c => c.BookingRequested,
            [nameof(VoiceReceptionistConfig.ConsentMode)] = c => c.ConsentMode,
        };

        var before = Configured();
        foreach (var (setting, read) in untouched)
        {
            if (string.Equals(setting, changed, StringComparison.Ordinal))
            {
                Assert.NotEqual(read(before), read(written));
                continue;
            }

            Assert.Equal(read(before), read(written));
        }

        // The crawl the form never asks about, carried through every save.
        Assert.Equal(Knowledge, written.KnowledgeRunId);
    }

    /// <summary>A step that groups several questions writes all of them at once, which is what makes grouping a form decision.</summary>
    [Fact]
    public async Task Several_settings_answered_on_one_step_are_all_written_together()
    {
        var store = new Store();

        await Writer(store).ApplyDuringSignupAsync(
            new ReceptionistConfigStepRequest(
                Tenant,
                new ReceptionistConfigAnswers(
                    TransferMode: VoiceTransferModes.VoicemailOnly,
                    MessageEmailEnabled: true,
                    MessageDestinationEmail: "dispatch@acme-plumbing.example",
                    IncludeTranscript: false)),
            CancellationToken.None);

        var written = Assert.Single(store.SavedConfigs);
        Assert.Equal(VoiceTransferModes.VoicemailOnly, written.TransferMode);
        Assert.True(written.MessageEmailEnabled);
        Assert.Equal("dispatch@acme-plumbing.example", Opened(written.MessageDestinationEmailEncrypted, VoiceSecretPurposes.ConfigMessageDestinationEmail));
        Assert.False(written.IncludeTranscript);

        Assert.Equal(Greeting, written.GreetingMessage);
        Assert.Equal(TransferNumber, Opened(written.TransferTargetNumberEncrypted, VoiceSecretPurposes.ConfigTransferTargetNumber));
        Assert.Equal(VoiceConsentModes.StrictKeypress, written.ConsentMode);
    }

    // ----- Null is not an answer; empty is -----

    /// <summary>
    /// The distinction the whole record is built on. An empty answer is the business saying "none" and clears the setting; null is the
    /// question not having been asked on this step, and leaves whatever the business said last time. A form that could not say "none"
    /// would have "none" chosen for it, and a form where null cleared things would wipe a business's answers one step at a time.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_answer_sent_empty_clears_the_setting(string answer)
    {
        var store = new Store();

        await Writer(store).ApplyDuringSignupAsync(
            new ReceptionistConfigStepRequest(
                Tenant, new ReceptionistConfigAnswers(TransferTargetNumber: answer, MessageDestinationEmail: answer)),
            CancellationToken.None);

        var written = Assert.Single(store.SavedConfigs);
        Assert.Null(written.TransferTargetNumberEncrypted);
        Assert.Null(written.MessageDestinationEmailEncrypted);
    }

    [Fact]
    public async Task An_answer_left_null_leaves_the_setting_exactly_as_it_was()
    {
        var store = new Store();

        await Writer(store).ApplyDuringSignupAsync(
            new ReceptionistConfigStepRequest(Tenant, new ReceptionistConfigAnswers(BookingEnabled: false)),
            CancellationToken.None);

        var written = Assert.Single(store.SavedConfigs);
        Assert.Equal(TransferNumber, Opened(written.TransferTargetNumberEncrypted, VoiceSecretPurposes.ConfigTransferTargetNumber));
        Assert.Equal(DestinationEmail, Opened(written.MessageDestinationEmailEncrypted, VoiceSecretPurposes.ConfigMessageDestinationEmail));
        Assert.Equal([EmergencyKeyword, "fire"], written.EmergencyKeywords);
    }

    /// <summary>An emergency-words list sent empty is a business saying no word makes a call an emergency, which is a valid answer.</summary>
    [Fact]
    public async Task An_empty_list_of_emergency_words_is_a_valid_answer_and_is_stored_as_one()
    {
        var store = new Store();

        await Writer(store).ApplyDuringSignupAsync(
            new ReceptionistConfigStepRequest(Tenant, new ReceptionistConfigAnswers(EmergencyKeywords: [])),
            CancellationToken.None);

        Assert.Empty(Assert.Single(store.SavedConfigs).EmergencyKeywords);
    }

    /// <summary>A step that answered nothing is a form posting an empty screen. It is refused rather than saved as a no-op.</summary>
    [Fact]
    public async Task A_step_that_answered_nothing_is_refused_and_nothing_is_read_or_saved()
    {
        var store = new Store();

        var refused = await Writer(store)
            .ApplyDuringSignupAsync(new ReceptionistConfigStepRequest(Tenant, new ReceptionistConfigAnswers()), CancellationToken.None);

        Assert.False(refused.IsSuccess);
        Assert.Equal(VoiceFailureCodes.InvalidRequest, refused.ErrorCode);
        Assert.Empty(store.ConfigReads);
        Assert.Empty(store.TenantReads);
        Assert.Empty(store.SavedConfigs);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task A_step_naming_no_tenant_is_refused_and_nothing_is_read_or_saved(string? tenantId)
    {
        var store = new Store();

        var refused = await Writer(store).ApplyDuringSignupAsync(
            new ReceptionistConfigStepRequest(
                tenantId is null ? null : Guid.Parse(tenantId), new ReceptionistConfigAnswers(BookingEnabled: true)),
            CancellationToken.None);

        Assert.False(refused.IsSuccess);
        Assert.Equal(VoiceFailureCodes.InvalidRequest, refused.ErrorCode);
        Assert.Empty(store.ConfigReads);
        Assert.Empty(store.SavedConfigs);
    }

    // ----- The signup door is closed once the business is live -----

    /// <summary>
    /// The caller here is the portal's server key, not a signed-in owner, so it must not be able to reconfigure a business that is
    /// already running. After payment the settings are the owner's, through their own token. Refused before the settings are read, so
    /// the refusal cannot be mistaken for a read that found nothing.
    /// </summary>
    [Theory]
    [InlineData(VoiceTenantStatuses.Active)]
    [InlineData(VoiceTenantStatuses.Suspended)]
    [InlineData(VoiceTenantStatuses.Cancelled)]
    public async Task A_business_past_pending_payment_is_refused_by_the_signup_door_and_nothing_is_saved(string status)
    {
        var store = new Store { Tenant = Result<VoiceTenant>.Success(TenantAt(status)) };

        var refused = await Writer(store).ApplyDuringSignupAsync(
            new ReceptionistConfigStepRequest(Tenant, new ReceptionistConfigAnswers(ConsentMode: VoiceConsentModes.PassiveDisclosure)),
            CancellationToken.None);

        Assert.False(refused.IsSuccess);
        Assert.Equal(ActivationFailureCodes.TenantNotPending, refused.ErrorCode);
        Assert.Equal(StatusCodes.Status409Conflict, OnboardingController.StatusFor(refused.ErrorCode));
        Assert.Empty(store.ConfigReads);
        Assert.Empty(store.SavedConfigs);
    }

    /// <summary>A tenant that cannot be read is not treated as pending: the status is unknown, so nothing is written.</summary>
    [Fact]
    public async Task A_tenant_that_cannot_be_read_stops_the_step_with_the_stores_own_code()
    {
        var store = new Store { Tenant = Result<VoiceTenant>.Failure("No such business.", VoiceFailureCodes.NotFound) };

        var refused = await Writer(store).ApplyDuringSignupAsync(
            new ReceptionistConfigStepRequest(Tenant, new ReceptionistConfigAnswers(BookingEnabled: true)), CancellationToken.None);

        Assert.False(refused.IsSuccess);
        Assert.Equal(VoiceFailureCodes.NotFound, refused.ErrorCode);
        Assert.Empty(store.SavedConfigs);
    }

    /// <summary>
    /// The owner's door has no status gate at all, on purpose: it is what stops every answer given at signup from being permanent. The
    /// owner's right to write was already proved by their token's claim, so the tenant row is not read a second time to decide it.
    /// </summary>
    [Theory]
    [InlineData(VoiceTenantStatuses.Active)]
    [InlineData(VoiceTenantStatuses.PendingPayment)]
    [InlineData(VoiceTenantStatuses.Suspended)]
    public async Task The_owners_door_writes_at_any_status_and_never_reads_the_tenant_row(string status)
    {
        var store = new Store { Tenant = Result<VoiceTenant>.Success(TenantAt(status)) };

        var saved = await Writer(store)
            .ApplyForOwnerAsync(Tenant, new ReceptionistConfigAnswers(GreetingMessage: "Acme Plumbing."), CancellationToken.None);

        Assert.True(saved.IsSuccess);
        Assert.Equal("Acme Plumbing.", Assert.Single(store.SavedConfigs).GreetingMessage);
        Assert.Empty(store.TenantReads);
    }

    // ----- Validation lives in one place -----

    /// <summary>
    /// A setting this product does not have is refused by SaveConfigAsync, which is where the rules live, and the refusal is handed
    /// back with the store's own code rather than re-worded or re-checked here. Two implementations of the same rule drift, and the one
    /// that drifts is the one nobody is looking at.
    /// </summary>
    [Fact]
    public async Task A_setting_the_product_does_not_have_comes_back_with_the_stores_own_refusal()
    {
        var store = new Store
        {
            SaveConfigResult = Result<VoiceReceptionistConfig>.Failure(
                "Choose a consent mode this product has.", VoiceFailureCodes.InvalidRequest),
        };

        var refused = await Writer(store).ApplyDuringSignupAsync(
            new ReceptionistConfigStepRequest(Tenant, new ReceptionistConfigAnswers(ConsentMode: "loud_disclosure")),
            CancellationToken.None);

        Assert.False(refused.IsSuccess);
        Assert.Equal(VoiceFailureCodes.InvalidRequest, refused.ErrorCode);

        // It reached the store rather than being refused here: that is what "not validated twice" means.
        Assert.Equal("loud_disclosure", Assert.Single(store.SavedConfigs).ConsentMode);
    }

    /// <summary>
    /// Settings that cannot be read abort the save. Merging answers onto a default would hand the business a config it never gave,
    /// under the fifteen fields it did not answer on this step.
    /// </summary>
    [Theory]
    [InlineData(VoiceFailureCodes.NotFound)]
    [InlineData(VoiceFailureCodes.StoreUnavailable)]
    public async Task Settings_that_cannot_be_read_abort_the_save_rather_than_merging_onto_a_default(string code)
    {
        var store = new Store { Config = Result<VoiceReceptionistConfig>.Failure("The settings could not be read.", code) };

        var refused = await Writer(store)
            .ApplyForOwnerAsync(Tenant, new ReceptionistConfigAnswers(NotifyOnAbandonedCalls: true), CancellationToken.None);

        Assert.False(refused.IsSuccess);
        Assert.Equal(code, refused.ErrorCode);
        Assert.Empty(store.SavedConfigs);
    }

    // ----- The owner's route -----

    [Fact]
    public async Task The_owner_is_shown_every_setting_and_every_choice_each_closed_question_has()
    {
        var store = new Store();

        var ok = Assert.IsType<OkObjectResult>(
            await OwnerController(store, new CapturingLogger<ReceptionistConfigController>()).Get(Tenant, CancellationToken.None));

        var json = Serialized(ok.Value);
        Assert.Contains(VoiceTransferModes.ColdTransfer, json, StringComparison.Ordinal);
        Assert.Contains(VoiceTransferCallerIdModes.AssignedAiNumber, json, StringComparison.Ordinal);
        Assert.Contains(VoiceConsentModes.PassiveDisclosure, json, StringComparison.Ordinal);
        Assert.Contains(Greeting, json, StringComparison.Ordinal);

        Assert.Equal(Tenant, Assert.Single(store.ConfigReads));
        Assert.Empty(store.SavedConfigs);
    }

    [Fact]
    public async Task The_owner_changing_one_setting_gets_the_saved_settings_back()
    {
        var store = new Store();

        var ok = Assert.IsType<OkObjectResult>(
            await OwnerController(store, new CapturingLogger<ReceptionistConfigController>())
                .Put(Tenant, new ReceptionistConfigAnswers(TransferMode: VoiceTransferModes.ColdTransfer), CancellationToken.None));

        Assert.Equal(VoiceTransferModes.ColdTransfer, Assert.Single(store.SavedConfigs).TransferMode);

        // The owner is shown the settings as saved, beside the choices the question has. The mode in force is read out of the
        // settings and not out of the choice list, which carries every mode by design.
        using var answered = JsonDocument.Parse(Serialized(ok.Value));
        var settings = answered.RootElement.GetProperty("settings");
        Assert.Equal(VoiceTransferModes.ColdTransfer, settings.GetProperty(nameof(VoiceReceptionistConfig.TransferMode)).GetString());
        Assert.Equal(Greeting, settings.GetProperty(nameof(VoiceReceptionistConfig.GreetingMessage)).GetString());
    }

    /// <summary>
    /// The tenant is the signed-in owner's own, from their token. Another business's id is refused on reading as firmly as on writing:
    /// being shown what another business tells its callers, what it pays attention to and where its messages go is a disclosure.
    /// </summary>
    [Fact]
    public async Task Another_businesss_id_is_refused_on_both_verbs_and_nothing_is_read_or_saved()
    {
        var store = new Store();
        var controller = OwnerController(store, new CapturingLogger<ReceptionistConfigController>(), signedInAs: OtherTenant);

        var read = Assert.IsType<ObjectResult>(await controller.Get(Tenant, CancellationToken.None));
        var write = Assert.IsType<ObjectResult>(
            await controller.Put(Tenant, new ReceptionistConfigAnswers(BookingEnabled: false), CancellationToken.None));

        Assert.Equal(StatusCodes.Status403Forbidden, read.StatusCode);
        Assert.Equal(StatusCodes.Status403Forbidden, write.StatusCode);
        Assert.Contains(McpFailureCodes.TenantForbidden, Serialized(read.Value), StringComparison.Ordinal);
        Assert.Contains(McpFailureCodes.TenantForbidden, Serialized(write.Value), StringComparison.Ordinal);
        Assert.Empty(store.ConfigReads);
        Assert.Empty(store.SavedConfigs);
    }

    [Fact]
    public async Task A_sign_in_carrying_no_tenant_is_refused_on_both_verbs()
    {
        var store = new Store();
        var controller = new ReceptionistConfigController(
            store, Writer(store), new CapturingLogger<ReceptionistConfigController>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([], "test")) },
            },
        };

        var read = Assert.IsType<ObjectResult>(await controller.Get(Tenant, CancellationToken.None));
        var write = Assert.IsType<ObjectResult>(
            await controller.Put(Tenant, new ReceptionistConfigAnswers(BookingEnabled: false), CancellationToken.None));

        Assert.Equal(StatusCodes.Status403Forbidden, read.StatusCode);
        Assert.Equal(StatusCodes.Status403Forbidden, write.StatusCode);
        Assert.Contains(McpFailureCodes.TenantUnverified, Serialized(read.Value), StringComparison.Ordinal);
        Assert.Contains(McpFailureCodes.TenantUnverified, Serialized(write.Value), StringComparison.Ordinal);
        Assert.Empty(store.ConfigReads);
        Assert.Empty(store.SavedConfigs);
    }

    [Fact]
    public async Task A_body_that_answered_nothing_is_refused_and_nothing_is_saved()
    {
        var store = new Store();

        var refused = Assert.IsType<ObjectResult>(
            await OwnerController(store, new CapturingLogger<ReceptionistConfigController>())
                .Put(Tenant, new ReceptionistConfigAnswers(), CancellationToken.None));

        Assert.Equal(StatusCodes.Status400BadRequest, refused.StatusCode);
        Assert.Contains(VoiceFailureCodes.InvalidRequest, Serialized(refused.Value), StringComparison.Ordinal);
        Assert.Empty(store.SavedConfigs);
    }

    [Theory]
    [InlineData(VoiceFailureCodes.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(VoiceFailureCodes.StoreUnavailable, StatusCodes.Status503ServiceUnavailable)]
    [InlineData(VoiceFailureCodes.InvalidRequest, StatusCodes.Status400BadRequest)]
    [InlineData(VoiceFailureCodes.Conflict, StatusCodes.Status409Conflict)]
    public async Task A_change_that_cannot_be_saved_is_answered_with_its_status_and_its_code(string code, int status)
    {
        var store = new Store
        {
            SaveConfigResult = Result<VoiceReceptionistConfig>.Failure("The settings could not be saved.", code),
        };
        var log = new CapturingLogger<ReceptionistConfigController>();

        var refused = Assert.IsType<ObjectResult>(
            await OwnerController(store, log).Put(Tenant, new ReceptionistConfigAnswers(NotifyOnAbandonedCalls: true), CancellationToken.None));

        Assert.Equal(status, refused.StatusCode);
        Assert.Contains($"\"code\":\"{code}\"", Serialized(refused.Value), StringComparison.Ordinal);

        // Loud, not swallowed: an operator can see which business and why.
        Assert.Contains(log.Lines, l => l.Contains(Tenant.ToString(), StringComparison.Ordinal) && l.Contains(code, StringComparison.Ordinal));
    }

    // ----- Zero Data Retention -----

    /// <summary>
    /// Every one of the sixteen settings passes through this route, and four of them are the business's own words or its people's
    /// contact details. What an operator gets is which business changed which settings, by name. Not a greeting, not a script, not a
    /// transfer number, not a destination address, and nothing a caller ever said.
    /// </summary>
    [Fact]
    public async Task No_log_line_carries_a_greeting_a_script_a_transfer_number_or_a_destination_address()
    {
        var log = new CapturingLogger<ReceptionistConfigController>();
        var store = new Store();
        var controller = OwnerController(store, log);

        await controller.Get(Tenant, CancellationToken.None);
        await controller.Put(
            Tenant,
            new ReceptionistConfigAnswers(
                GreetingMessage: Greeting,
                SystemPromptTemplate: SystemPrompt,
                TransferTargetNumber: TransferNumber,
                MessageDestinationEmail: DestinationEmail,
                BusinessHours: Json(BusinessHoursJson),
                QualifyingQuestions: Json(QualifyingQuestionsJson),
                EmergencyKeywords: [EmergencyKeyword]),
            CancellationToken.None);

        var failing = new Store
        {
            SaveConfigResult = Result<VoiceReceptionistConfig>.Failure(
                "The settings could not be saved.", VoiceFailureCodes.StoreUnavailable),
        };
        await OwnerController(failing, log).Put(
            Tenant, new ReceptionistConfigAnswers(GreetingMessage: Greeting, TransferTargetNumber: TransferNumber), CancellationToken.None);

        Assert.NotEmpty(log.Lines);
        foreach (var line in log.Lines)
        {
            Assert.DoesNotContain("GREETING-SENTINEL", line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("PROMPT-SENTINEL", line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("HOURS-SENTINEL", line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("QUESTION-SENTINEL", line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("KEYWORD-SENTINEL", line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(TransferNumber, line, StringComparison.Ordinal);
            Assert.DoesNotContain(DestinationEmail, line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Acme", line, StringComparison.OrdinalIgnoreCase);
        }

        // What it does carry: which business, and which settings it changed.
        Assert.Contains(
            log.Lines,
            l => l.Contains(Tenant.ToString(), StringComparison.Ordinal)
                && l.Contains(nameof(VoiceReceptionistConfig.GreetingMessage), StringComparison.Ordinal));
    }

    /// <summary>
    /// IncludeTranscript is a setting about one email the business asked for, and it is still only that. It carries what was said into
    /// the end-of-call message and persists nothing, so a route that can turn it on must not be read as a route that turns on
    /// retention. Nothing here writes a transcript anywhere, whichever way it is set.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Turning_the_transcript_setting_either_way_writes_one_setting_and_stores_no_call_content(bool include)
    {
        var store = new Store();

        await Writer(store).ApplyForOwnerAsync(
            Tenant, new ReceptionistConfigAnswers(IncludeTranscript: include), CancellationToken.None);

        var written = Assert.Single(store.SavedConfigs);
        Assert.Equal(include, written.IncludeTranscript);
        Assert.Equal(Configured().MessageEmailEnabled, written.MessageEmailEnabled);
        Assert.Equal(DestinationEmail, Opened(written.MessageDestinationEmailEncrypted, VoiceSecretPurposes.ConfigMessageDestinationEmail));
    }

    // ----- The prompt is assembled from what the business said about itself -----

    /// <summary>
    /// The prompt is a document and not a setting, so the form sends its parts and GeekAPI writes it. What it replaces is the starting
    /// script, which is one company's sales script with another company's name in it, and that is the whole point of this step: a
    /// business that answers these questions stops having a receptionist that offers free consultations in three Florida counties.
    /// </summary>
    [Fact]
    public async Task What_the_business_said_about_itself_becomes_its_prompt_and_nothing_else()
    {
        var store = new Store();

        var saved = await Writer(store).ApplyDuringSignupAsync(
            new ReceptionistConfigStepRequest(Tenant, new ReceptionistConfigAnswers(Knowledge: new ReceptionistKnowledgeAnswers(
                ReceptionistName: "Dana",
                WhatTheBusinessDoes: "Emergency plumbing and drain clearing.",
                WhoItServes: "Homeowners within thirty miles.",
                HowItPrices: "Ninety dollars to come out, quoted after that.",
                WhatToDoWithACaller: "Take the address and a number to call back."))),
            CancellationToken.None);

        Assert.True(saved.IsSuccess);
        var written = Assert.Single(store.SavedConfigs);

        // Every part the business gave, and the three facts taken from its tenant row rather than asked for twice.
        Assert.Contains("You are Dana, the receptionist for Acme Plumbing, a business in Florida that works in home_services", written.SystemPromptTemplate, StringComparison.Ordinal);
        Assert.Contains("Emergency plumbing and drain clearing.", written.SystemPromptTemplate, StringComparison.Ordinal);
        Assert.Contains("Homeowners within thirty miles.", written.SystemPromptTemplate, StringComparison.Ordinal);
        Assert.Contains("Ninety dollars to come out", written.SystemPromptTemplate, StringComparison.Ordinal);
        Assert.Contains("Take the address and a number to call back.", written.SystemPromptTemplate, StringComparison.Ordinal);

        // Not one word of the script it replaced.
        Assert.DoesNotContain("Lily", written.SystemPromptTemplate, StringComparison.Ordinal);
        Assert.DoesNotContain("PROMPT-SENTINEL", written.SystemPromptTemplate, StringComparison.Ordinal);
        Assert.DoesNotContain("free consultation", written.SystemPromptTemplate, StringComparison.OrdinalIgnoreCase);

        // The other fourteen are left as the business left them, including the crawl id no form touches.
        Assert.Equal(Greeting, written.GreetingMessage);
        Assert.Equal(Configured().ConsentMode, written.ConsentMode);
        Assert.Equal(Knowledge, written.KnowledgeRunId);
    }

    /// <summary>
    /// A part left unanswered is left out, not filled in. The receptionist is already told never to state a fact it was not given, so a
    /// heading with nothing under it would be text for the model to weigh and nothing for it to say.
    /// </summary>
    [Fact]
    public async Task A_part_the_business_left_blank_is_left_out_of_the_prompt()
    {
        var store = new Store();

        await Writer(store).ApplyDuringSignupAsync(
            new ReceptionistConfigStepRequest(Tenant, new ReceptionistConfigAnswers(
                Knowledge: new ReceptionistKnowledgeAnswers(WhatTheBusinessDoes: "Emergency plumbing.", HowItPrices: "   "))),
            CancellationToken.None);

        var written = Assert.Single(store.SavedConfigs);
        Assert.Contains("## What this business does", written.SystemPromptTemplate, StringComparison.Ordinal);
        Assert.DoesNotContain("## What it charges", written.SystemPromptTemplate, StringComparison.Ordinal);
        Assert.DoesNotContain("## Who it serves", written.SystemPromptTemplate, StringComparison.Ordinal);

        // No name given is the question's other answer, used as given rather than treated as missing.
        Assert.Contains("You are the AI receptionist for Acme Plumbing", written.SystemPromptTemplate, StringComparison.Ordinal);
    }

    /// <summary>A receptionist that cannot say what the business does can answer nothing, so that one part is refused when it is blank.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_business_that_says_nothing_about_itself_is_refused_rather_than_given_a_prompt_about_nothing(string? does)
    {
        var store = new Store();

        var saved = await Writer(store).ApplyDuringSignupAsync(
            new ReceptionistConfigStepRequest(Tenant, new ReceptionistConfigAnswers(
                Knowledge: new ReceptionistKnowledgeAnswers(WhatTheBusinessDoes: does))),
            CancellationToken.None);

        Assert.False(saved.IsSuccess);
        Assert.Equal(VoiceFailureCodes.InvalidRequest, saved.ErrorCode);
        Assert.Empty(store.SavedConfigs);
    }

    /// <summary>
    /// The parts and a finished prompt are two answers to one question. Resolving them in some order would make which one won depend on
    /// reading this code, so both together is refused and nothing is written.
    /// </summary>
    [Fact]
    public async Task The_prompt_answered_twice_is_refused_on_both_doors()
    {
        var answers = new ReceptionistConfigAnswers(
            SystemPromptTemplate: "You answer for a plumber.",
            Knowledge: new ReceptionistKnowledgeAnswers(WhatTheBusinessDoes: "Emergency plumbing."));

        var signup = new Store();
        var duringSignup = await Writer(signup)
            .ApplyDuringSignupAsync(new ReceptionistConfigStepRequest(Tenant, answers), CancellationToken.None);

        var owner = new Store();
        var asOwner = await Writer(owner)
            .ApplyForOwnerAsync(Tenant, answers, CancellationToken.None);

        Assert.False(duringSignup.IsSuccess);
        Assert.False(asOwner.IsSuccess);
        Assert.Equal(VoiceFailureCodes.InvalidRequest, duringSignup.ErrorCode);
        Assert.Equal(VoiceFailureCodes.InvalidRequest, asOwner.ErrorCode);
        Assert.Empty(signup.SavedConfigs);
        Assert.Empty(owner.SavedConfigs);
    }

    /// <summary>
    /// The signup door has already read the tenant for its status gate, so the prompt's assembly is handed that row. The owner's door
    /// has not, and reads it once - the business's own name is in the prompt and cannot be guessed from the token.
    /// </summary>
    [Fact]
    public async Task The_tenant_row_is_read_once_on_each_door()
    {
        var signup = new Store();
        await Writer(signup).ApplyDuringSignupAsync(
            new ReceptionistConfigStepRequest(Tenant, new ReceptionistConfigAnswers(
                Knowledge: new ReceptionistKnowledgeAnswers(WhatTheBusinessDoes: "Emergency plumbing."))),
            CancellationToken.None);

        var owner = new Store();
        await Writer(owner).ApplyForOwnerAsync(
            Tenant,
            new ReceptionistConfigAnswers(Knowledge: new ReceptionistKnowledgeAnswers(WhatTheBusinessDoes: "Emergency plumbing.")),
            CancellationToken.None);

        Assert.Equal([Tenant], signup.TenantReads);
        Assert.Equal([Tenant], owner.TenantReads);

        // And an answer that is not the prompt does not read the tenant on the owner's door at all.
        var unrelated = new Store();
        await Writer(unrelated)
            .ApplyForOwnerAsync(Tenant, new ReceptionistConfigAnswers(BookingEnabled: false), CancellationToken.None);
        Assert.Empty(unrelated.TenantReads);
    }

    /// <summary>
    /// The answer names the setting it changes, so a log line can say which setting a business changed without carrying what it said.
    /// The parts arrive as Knowledge and are the prompt, so that is the name they take.
    /// </summary>
    [Fact]
    public void The_parts_of_the_prompt_are_named_as_the_prompt()
    {
        var answers = new ReceptionistConfigAnswers(
            Knowledge: new ReceptionistKnowledgeAnswers(WhatTheBusinessDoes: "Emergency plumbing."));

        Assert.True(answers.AnswersAnything);
        Assert.False(answers.AnswersThePromptTwice);
        Assert.Equal([nameof(VoiceReceptionistConfig.SystemPromptTemplate)], answers.AnsweredFields);
    }

    // ----- Booking on needs a calendar behind it, on this door too -----

    /// <summary>
    /// The rule BookingSettingController states and enforces on the dashboard's own switch: <i>"A setting saved as on with nothing
    /// behind it would show the owner a receptionist that books while every caller is offered a message instead."</i> This route writes
    /// the same column, so until the check was here the rule held on one of the two paths to it.
    /// </summary>
    [Fact]
    public async Task Booking_turned_on_without_a_calendar_is_refused_and_nothing_is_saved()
    {
        var store = new Store();

        var refused = await Writer(store).ApplyForOwnerAsync(
            Tenant, new ReceptionistConfigAnswers(BookingEnabled: true), CancellationToken.None);

        Assert.False(refused.IsSuccess);
        Assert.Equal(ReceptionistConfigWriter.CalendarRequired, refused.ErrorCode);
        Assert.Equal(BookingSettingController.CalendarRequired, refused.ErrorCode);
        Assert.Empty(store.SavedConfigs);
    }

    /// <summary>The same refusal during signup, where a business has no calendar yet because its owner has no login yet.</summary>
    [Fact]
    public async Task Booking_turned_on_during_signup_is_refused_too()
    {
        var store = new Store();

        var refused = await Writer(store).ApplyDuringSignupAsync(
            new ReceptionistConfigStepRequest(Tenant, new ReceptionistConfigAnswers(BookingEnabled: true)), CancellationToken.None);

        Assert.False(refused.IsSuccess);
        Assert.Equal(ReceptionistConfigWriter.CalendarRequired, refused.ErrorCode);
        Assert.Empty(store.SavedConfigs);
    }

    /// <summary>With a calendar that answers, booking is turned on and nothing else moves.</summary>
    [Fact]
    public async Task Booking_turned_on_with_a_calendar_that_works_is_saved()
    {
        var store = new Store();
        store.Config = Result<VoiceReceptionistConfig>.Success(Configured() with { BookingEnabled = false });
        GiveACalendar(store);

        var saved = await Writer(store).ApplyForOwnerAsync(
            Tenant, new ReceptionistConfigAnswers(BookingEnabled: true), CancellationToken.None);

        Assert.True(saved.IsSuccess, saved.Error);
        Assert.True(Assert.Single(store.SavedConfigs).BookingEnabled);
    }

    /// <summary>
    /// Turning it off is the business withdrawing permission and is never refused, whatever the state of its calendar - and it does
    /// not ask the calendar at all, because there is nothing a calendar could say that should stand in the way of it.
    /// </summary>
    [Fact]
    public async Task Booking_turned_off_is_never_refused_and_asks_no_calendar()
    {
        var store = new Store();
        var gateway = new GatewayHandler(() => throw new InvalidOperationException("The calendar must not be asked."));

        var saved = await Writer(store, gateway).ApplyForOwnerAsync(
            Tenant, new ReceptionistConfigAnswers(BookingEnabled: false), CancellationToken.None);

        Assert.True(saved.IsSuccess, saved.Error);
        Assert.False(Assert.Single(store.SavedConfigs).BookingEnabled);
        Assert.Equal(0, gateway.Calls);
    }

    /// <summary>An answer that is not about booking asks no calendar either, so no other step pays for this check.</summary>
    [Fact]
    public async Task An_answer_that_is_not_about_booking_asks_no_calendar()
    {
        var store = new Store();
        var gateway = new GatewayHandler(() => throw new InvalidOperationException("The calendar must not be asked."));

        var saved = await Writer(store, gateway).ApplyForOwnerAsync(
            Tenant, new ReceptionistConfigAnswers(ConsentMode: VoiceConsentModes.PassiveDisclosure), CancellationToken.None);

        Assert.True(saved.IsSuccess, saved.Error);
        Assert.Equal(0, gateway.Calls);
    }

    /// <summary>
    /// The signup form's appointments answer, which is the whole reason this field is not <c>BookingEnabled</c>: it asks no calendar,
    /// because no business has one during signup. A business answering yes on the form must not be refused for it.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Asking_for_appointments_at_signup_is_saved_without_a_calendar_and_turns_booking_on_for_nobody(bool wants)
    {
        var store = new Store();
        store.Config = Result<VoiceReceptionistConfig>.Success(Configured() with { BookingEnabled = false, BookingRequested = false });
        var gateway = new GatewayHandler(() => throw new InvalidOperationException("The calendar must not be asked."));

        var saved = await Writer(store, gateway).ApplyDuringSignupAsync(
            new ReceptionistConfigStepRequest(Tenant, new ReceptionistConfigAnswers(BookingRequested: wants)), CancellationToken.None);

        Assert.True(saved.IsSuccess, saved.Error);
        var written = Assert.Single(store.SavedConfigs);
        Assert.Equal(wants, written.BookingRequested);

        // The answer is recorded; the capability is not granted here, because there is nothing yet to grant it against.
        Assert.False(written.BookingEnabled);
        Assert.Equal(0, gateway.Calls);
    }
}
