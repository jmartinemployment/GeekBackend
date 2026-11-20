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
        public Task<Result<VoiceMcpConnection>> GetMcpConnectionAsync(Guid tenantId, string provider, CancellationToken ct = default) => throw new NotSupportedException();
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

    private static ReceptionistConfigController OwnerController(
        Store store, CapturingLogger<ReceptionistConfigController> log, Guid? signedInAs = null) =>
        new(store, new ReceptionistConfigWriter(store, Protector), log)
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

        var saved = await new ReceptionistConfigWriter(store, Protector)
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

        await new ReceptionistConfigWriter(store, Protector).ApplyDuringSignupAsync(
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

        await new ReceptionistConfigWriter(store, Protector).ApplyDuringSignupAsync(
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

        await new ReceptionistConfigWriter(store, Protector).ApplyDuringSignupAsync(
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

        await new ReceptionistConfigWriter(store, Protector).ApplyDuringSignupAsync(
            new ReceptionistConfigStepRequest(Tenant, new ReceptionistConfigAnswers(EmergencyKeywords: [])),
            CancellationToken.None);

        Assert.Empty(Assert.Single(store.SavedConfigs).EmergencyKeywords);
    }

    /// <summary>A step that answered nothing is a form posting an empty screen. It is refused rather than saved as a no-op.</summary>
    [Fact]
    public async Task A_step_that_answered_nothing_is_refused_and_nothing_is_read_or_saved()
    {
        var store = new Store();

        var refused = await new ReceptionistConfigWriter(store, Protector)
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

        var refused = await new ReceptionistConfigWriter(store, Protector).ApplyDuringSignupAsync(
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

        var refused = await new ReceptionistConfigWriter(store, Protector).ApplyDuringSignupAsync(
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

        var refused = await new ReceptionistConfigWriter(store, Protector).ApplyDuringSignupAsync(
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

        var saved = await new ReceptionistConfigWriter(store, Protector)
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

        var refused = await new ReceptionistConfigWriter(store, Protector).ApplyDuringSignupAsync(
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

        var refused = await new ReceptionistConfigWriter(store, Protector)
            .ApplyForOwnerAsync(Tenant, new ReceptionistConfigAnswers(BookingEnabled: true), CancellationToken.None);

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
            store, new ReceptionistConfigWriter(store, Protector), new CapturingLogger<ReceptionistConfigController>())
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
            await OwnerController(store, log).Put(Tenant, new ReceptionistConfigAnswers(BookingEnabled: true), CancellationToken.None));

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

        await new ReceptionistConfigWriter(store, Protector).ApplyForOwnerAsync(
            Tenant, new ReceptionistConfigAnswers(IncludeTranscript: include), CancellationToken.None);

        var written = Assert.Single(store.SavedConfigs);
        Assert.Equal(include, written.IncludeTranscript);
        Assert.Equal(Configured().MessageEmailEnabled, written.MessageEmailEnabled);
        Assert.Equal(DestinationEmail, Opened(written.MessageDestinationEmailEncrypted, VoiceSecretPurposes.ConfigMessageDestinationEmail));
    }
}
