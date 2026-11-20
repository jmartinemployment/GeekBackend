using System.Security.Claims;
using System.Text.Json;
using GeekAPI.Controllers.Voice;
using GeekAPI.Services.Voice.Mcp;
using GeekAPI.Services.Voice.OwnerAuth;
using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace GeekBackend.Tests.Voice;

/// <summary>
/// The owner's own choice of what their callers are told and asked before the AI engages. Jeff, 2026-11-20: <i>"Client
/// configurable. I don't feel comfortable making that decision for others."</i>
///
/// <para>
/// So the first thing these tests pin is that the route has no opinion: <b>both</b> modes save, neither is refused, neither has a
/// precondition, and neither is harder to ask for than the other. Strict keypress asks every caller to press a key and gets
/// affirmative consent and an autodialer that cannot answer - which is money, because this product bills by the minute. Passive
/// disclosure asks nothing and deters nothing. That trade belongs to the business, and a test that let one direction fail would be
/// this route quietly making the choice for them.
/// </para>
///
/// <para>
/// The rest: a mode this product does not have is refused before anything is read or written, another tenant's id is refused on both
/// verbs, a sign-in with no tenant touches nothing, every store failure comes back as its own status and code rather than a
/// clean-looking answer, and no log line carries the business's greeting, its script, its transfer number or anything a caller said.
/// </para>
/// </summary>
public sealed class ConsentSettingControllerTests
{
    private static readonly Guid Tenant = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid OtherTenant = Guid.Parse("99999999-8888-7777-6666-555555555555");

    // Sentinels. Nothing here is this controller's business, so every one of them is something the Zero Data Retention sweep looks
    // for in the log lines, and something the save test proves survived a `with { ConsentMode = ... }`.
    private const string Greeting = "GREETING-SENTINEL: Thanks for calling Acme Plumbing.";
    private const string SystemPrompt = "PROMPT-SENTINEL: You are the receptionist for Acme Plumbing.";
    private const string TransferNumber = "+15615550137";
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
        public Result<VoiceReceptionistConfig> Config { get; set; } =
            Result<VoiceReceptionistConfig>.Success(ConfigWith(VoiceConsentModes.PassiveDisclosure));

        public Result<VoiceReceptionistConfig>? SaveConfigResult { get; set; }

        /// <summary>Every read, so "nothing is read" can be asserted and not assumed.</summary>
        public List<Guid> ConfigReads { get; } = new();

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

        // This route reads and writes one business setting and nothing else. Anything else it reached for would throw here rather
        // than quietly succeed, which is the point of leaving them unsupported.
        public Task<Result<VoiceTenant>> CreateTenantAsync(CreateVoiceTenantCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceTenant>> GetTenantAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
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

    /// <summary>A business with something to lose in every field this route does not touch.</summary>
    private static VoiceReceptionistConfig ConfigWith(string consentMode)
    {
        using var hours = JsonDocument.Parse(BusinessHoursJson);
        using var questions = JsonDocument.Parse(QualifyingQuestionsJson);
        return new VoiceReceptionistConfig(
            Greeting,
            SystemPrompt,
            hours.RootElement.Clone(),
            questions.RootElement.Clone(),
            VoiceTransferModes.WarmTransfer,
            TransferNumber,
            VoiceTransferCallerIdModes.OriginalCaller,
            [EmergencyKeyword, "fire"],
            RecordingDisclosureEnabled: true,
            MessageEmailEnabled: true,
            MessageDestinationEmailEncrypted: "owner@acme-plumbing.example",
            IncludeTranscript: true,
            NotifyOnAbandonedCalls: true,
            BookingEnabled: true,
            ConsentMode: consentMode,
            KnowledgeRunId: Guid.Parse("11111111-2222-3333-4444-555555555555"));
    }

    private static ConsentSettingController ControllerFor(Store store, CapturingLogger<ConsentSettingController> log, Guid? signedInAs = null) =>
        new(store, log)
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

    private static string Json(object? value) => JsonSerializer.Serialize(value);

    // ----- What the owner is shown -----

    [Fact]
    public async Task The_mode_in_force_and_every_mode_the_business_may_choose_between_both_come_back()
    {
        var store = new Store { Config = Result<VoiceReceptionistConfig>.Success(ConfigWith(VoiceConsentModes.StrictKeypress)) };

        var ok = Assert.IsType<OkObjectResult>(
            await ControllerFor(store, new CapturingLogger<ConsentSettingController>()).Get(Tenant, CancellationToken.None));

        var json = Json(ok.Value);
        Assert.Contains($"\"mode\":\"{VoiceConsentModes.StrictKeypress}\"", json, StringComparison.Ordinal);

        // Both choices, always, because a page that only lists the one in force cannot offer the other.
        Assert.Contains($"\"{VoiceConsentModes.StrictKeypress}\"", json, StringComparison.Ordinal);
        Assert.Contains($"\"{VoiceConsentModes.PassiveDisclosure}\"", json, StringComparison.Ordinal);

        Assert.Equal(Tenant, Assert.Single(store.ConfigReads));
        Assert.Empty(store.SavedConfigs);
    }

    // ----- Both directions, and no preference between them -----

    /// <summary>
    /// The heart of it. Either mode is saved on being asked for, including being asked for again when it is already in force, and
    /// neither direction is refused, delayed or made conditional on anything. There is no recommended answer here.
    /// </summary>
    [Theory]
    [InlineData(VoiceConsentModes.PassiveDisclosure, VoiceConsentModes.StrictKeypress)]
    [InlineData(VoiceConsentModes.StrictKeypress, VoiceConsentModes.PassiveDisclosure)]
    [InlineData(VoiceConsentModes.StrictKeypress, VoiceConsentModes.StrictKeypress)]
    [InlineData(VoiceConsentModes.PassiveDisclosure, VoiceConsentModes.PassiveDisclosure)]
    public async Task Either_mode_the_owner_asks_for_is_saved_and_neither_direction_is_refused(string inForce, string asked)
    {
        var store = new Store { Config = Result<VoiceReceptionistConfig>.Success(ConfigWith(inForce)) };

        var ok = Assert.IsType<OkObjectResult>(
            await ControllerFor(store, new CapturingLogger<ConsentSettingController>())
                .Put(Tenant, new ConsentSettingRequest(asked), CancellationToken.None));

        Assert.Equal(asked, Assert.Single(store.SavedConfigs).ConsentMode);

        var json = Json(ok.Value);
        Assert.Contains($"\"mode\":\"{asked}\"", json, StringComparison.Ordinal);
        Assert.Contains($"\"{VoiceConsentModes.StrictKeypress}\"", json, StringComparison.Ordinal);
        Assert.Contains($"\"{VoiceConsentModes.PassiveDisclosure}\"", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// The mode changes and the business does not. The whole record is written back, so a save that dropped the greeting, the script,
    /// the transfer arrangements or the booking switch would be this route silently reconfiguring a receptionist nobody asked it to
    /// touch. Every field is named here on purpose: a field left out of this list is a field nothing would notice losing.
    /// </summary>
    [Fact]
    public async Task Saving_the_mode_changes_the_mode_and_nothing_else_about_the_business()
    {
        var before = ConfigWith(VoiceConsentModes.PassiveDisclosure);
        var store = new Store { Config = Result<VoiceReceptionistConfig>.Success(before) };

        await ControllerFor(store, new CapturingLogger<ConsentSettingController>())
            .Put(Tenant, new ConsentSettingRequest(VoiceConsentModes.StrictKeypress), CancellationToken.None);

        var saved = Assert.Single(store.SavedConfigs);

        Assert.Equal(VoiceConsentModes.StrictKeypress, saved.ConsentMode);

        Assert.Equal(Greeting, saved.GreetingMessage);
        Assert.Equal(SystemPrompt, saved.SystemPromptTemplate);
        Assert.Equal(BusinessHoursJson, saved.BusinessHours.GetRawText());
        Assert.Equal(QualifyingQuestionsJson, saved.QualifyingQuestions.GetRawText());
        Assert.Equal(VoiceTransferModes.WarmTransfer, saved.TransferMode);
        Assert.Equal(TransferNumber, saved.TransferTargetNumberEncrypted);
        Assert.Equal(VoiceTransferCallerIdModes.OriginalCaller, saved.TransferCallerIdMode);
        Assert.Equal(new[] { EmergencyKeyword, "fire" }, saved.EmergencyKeywords);
        Assert.True(saved.RecordingDisclosureEnabled);
        Assert.True(saved.MessageEmailEnabled);
        Assert.Equal("owner@acme-plumbing.example", saved.MessageDestinationEmailEncrypted);
        Assert.True(saved.IncludeTranscript);
        Assert.True(saved.NotifyOnAbandonedCalls);
        Assert.True(saved.BookingEnabled);
        Assert.Equal(before.KnowledgeRunId, saved.KnowledgeRunId);
    }

    // ----- A mode this product does not have -----

    /// <summary>
    /// A mode nothing in the product answers to would be stored and then fall through to passive on the next call: a business given a
    /// setting it did not ask for by a route that said yes. So it is refused, and refused before the store is touched at all. The
    /// wrong-case row pins the comparison as ordinal - "Strict_Keypress" is not a mode, and a case-insensitive check would store it.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("loud_disclosure")]
    [InlineData("Strict_Keypress")]
    public async Task A_mode_this_product_does_not_have_is_refused_and_nothing_is_read_or_saved(string mode)
    {
        var store = new Store();

        var bad = Assert.IsType<BadRequestObjectResult>(
            await ControllerFor(store, new CapturingLogger<ConsentSettingController>())
                .Put(Tenant, new ConsentSettingRequest(mode), CancellationToken.None));

        Assert.Contains($"\"code\":\"{VoiceFailureCodes.InvalidRequest}\"", Json(bad.Value), StringComparison.Ordinal);
        Assert.Empty(store.ConfigReads);
        Assert.Empty(store.SavedConfigs);
    }

    /// <summary>No body is not an instruction to do nothing quietly; it is a request that cannot be honoured, and it says so.</summary>
    [Fact]
    public async Task A_request_carrying_no_body_at_all_is_refused_and_nothing_is_read_or_saved()
    {
        var store = new Store();

        var bad = Assert.IsType<BadRequestObjectResult>(
            await ControllerFor(store, new CapturingLogger<ConsentSettingController>()).Put(Tenant, null, CancellationToken.None));

        Assert.Contains($"\"code\":\"{VoiceFailureCodes.InvalidRequest}\"", Json(bad.Value), StringComparison.Ordinal);
        Assert.Empty(store.ConfigReads);
        Assert.Empty(store.SavedConfigs);
    }

    // ----- Whose tenant it is -----

    /// <summary>
    /// The tenant is the signed-in owner's own, from their token. A URL naming somebody else is refused on reading as firmly as on
    /// writing: being told what another business tells its callers is a disclosure, not a harmless GET.
    /// </summary>
    [Fact]
    public async Task Another_tenants_id_is_refused_on_both_verbs_and_nothing_is_read_or_saved()
    {
        var store = new Store();
        var controller = ControllerFor(store, new CapturingLogger<ConsentSettingController>(), signedInAs: OtherTenant);

        var read = Assert.IsType<ObjectResult>(await controller.Get(Tenant, CancellationToken.None));
        var write = Assert.IsType<ObjectResult>(
            await controller.Put(Tenant, new ConsentSettingRequest(VoiceConsentModes.StrictKeypress), CancellationToken.None));

        Assert.Equal(StatusCodes.Status403Forbidden, read.StatusCode);
        Assert.Equal(StatusCodes.Status403Forbidden, write.StatusCode);
        Assert.Contains(McpFailureCodes.TenantForbidden, Json(read.Value), StringComparison.Ordinal);
        Assert.Contains(McpFailureCodes.TenantForbidden, Json(write.Value), StringComparison.Ordinal);
        Assert.Empty(store.ConfigReads);
        Assert.Empty(store.SavedConfigs);
    }

    /// <summary>A sign-in that was never given a tenant can touch no tenant's settings, on either verb.</summary>
    [Fact]
    public async Task A_sign_in_carrying_no_tenant_is_refused_on_both_verbs()
    {
        var store = new Store();
        var controller = new ConsentSettingController(store, new CapturingLogger<ConsentSettingController>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([], "test")) },
            },
        };

        var read = Assert.IsType<ObjectResult>(await controller.Get(Tenant, CancellationToken.None));
        var write = Assert.IsType<ObjectResult>(
            await controller.Put(Tenant, new ConsentSettingRequest(VoiceConsentModes.PassiveDisclosure), CancellationToken.None));

        Assert.Equal(StatusCodes.Status403Forbidden, read.StatusCode);
        Assert.Equal(StatusCodes.Status403Forbidden, write.StatusCode);
        Assert.Contains(McpFailureCodes.TenantUnverified, Json(read.Value), StringComparison.Ordinal);
        Assert.Contains(McpFailureCodes.TenantUnverified, Json(write.Value), StringComparison.Ordinal);
        Assert.Empty(store.ConfigReads);
        Assert.Empty(store.SavedConfigs);
    }

    // ----- Store failures -----

    /// <summary>
    /// Settings that cannot be read are not a blank page and not a default mode: the owner is told, with the store's own code and the
    /// status that code maps to. On the write it aborts before anything is saved, because a record built on settings nobody could read
    /// is a record that would overwrite them.
    /// </summary>
    [Theory]
    [InlineData(VoiceFailureCodes.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(VoiceFailureCodes.StoreUnavailable, StatusCodes.Status503ServiceUnavailable)]
    [InlineData(VoiceFailureCodes.InvalidRequest, StatusCodes.Status400BadRequest)]
    [InlineData(VoiceFailureCodes.Conflict, StatusCodes.Status409Conflict)]
    public async Task Settings_that_cannot_be_read_are_answered_with_their_status_and_their_code_on_both_verbs(string code, int status)
    {
        var store = new Store { Config = Result<VoiceReceptionistConfig>.Failure("The settings could not be read.", code) };
        var controller = ControllerFor(store, new CapturingLogger<ConsentSettingController>());

        var read = Assert.IsType<ObjectResult>(await controller.Get(Tenant, CancellationToken.None));
        var write = Assert.IsType<ObjectResult>(
            await controller.Put(Tenant, new ConsentSettingRequest(VoiceConsentModes.StrictKeypress), CancellationToken.None));

        Assert.Equal(status, read.StatusCode);
        Assert.Equal(status, write.StatusCode);
        Assert.Contains($"\"code\":\"{code}\"", Json(read.Value), StringComparison.Ordinal);
        Assert.Contains($"\"code\":\"{code}\"", Json(write.Value), StringComparison.Ordinal);
        Assert.Empty(store.SavedConfigs);
    }

    /// <summary>
    /// A mode that would not save comes back as the failure it is, with the store's own code. The owner is never shown the mode they
    /// asked for as though it had taken: their callers would still be hearing the old one.
    /// </summary>
    [Theory]
    [InlineData(VoiceFailureCodes.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(VoiceFailureCodes.StoreUnavailable, StatusCodes.Status503ServiceUnavailable)]
    public async Task A_mode_that_cannot_be_saved_is_answered_with_its_status_and_its_code(string code, int status)
    {
        var store = new Store
        {
            Config = Result<VoiceReceptionistConfig>.Success(ConfigWith(VoiceConsentModes.PassiveDisclosure)),
            SaveConfigResult = Result<VoiceReceptionistConfig>.Failure("The setting could not be saved.", code),
        };
        var log = new CapturingLogger<ConsentSettingController>();

        var refused = Assert.IsType<ObjectResult>(
            await ControllerFor(store, log).Put(Tenant, new ConsentSettingRequest(VoiceConsentModes.StrictKeypress), CancellationToken.None));

        Assert.Equal(status, refused.StatusCode);
        Assert.Contains($"\"code\":\"{code}\"", Json(refused.Value), StringComparison.Ordinal);

        // Loud, not swallowed: an operator can see which tenant and why.
        Assert.Contains(log.Lines, l => l.Contains(Tenant.ToString(), StringComparison.Ordinal) && l.Contains(code, StringComparison.Ordinal));
    }

    // ----- Zero Data Retention -----

    /// <summary>
    /// Zero Data Retention. This route reads and writes one business setting, and the record it passes through carries the business's
    /// greeting, its receptionist's script, its qualifying questions, its hours, its emergency words and the number it transfers
    /// callers to. None of that belongs in a log line, and neither does anything a caller said. What an operator does get is which
    /// tenant, and which mode it chose.
    /// </summary>
    [Fact]
    public async Task No_log_line_carries_the_businesss_greeting_its_script_or_its_transfer_number()
    {
        var store = new Store { Config = Result<VoiceReceptionistConfig>.Success(ConfigWith(VoiceConsentModes.PassiveDisclosure)) };
        var log = new CapturingLogger<ConsentSettingController>();
        var controller = ControllerFor(store, log);

        await controller.Get(Tenant, CancellationToken.None);
        await controller.Put(Tenant, new ConsentSettingRequest(VoiceConsentModes.StrictKeypress), CancellationToken.None);
        await controller.Put(Tenant, new ConsentSettingRequest(VoiceConsentModes.PassiveDisclosure), CancellationToken.None);
        await controller.Put(Tenant, new ConsentSettingRequest("loud_disclosure"), CancellationToken.None);

        var failing = new Store
        {
            Config = Result<VoiceReceptionistConfig>.Success(ConfigWith(VoiceConsentModes.PassiveDisclosure)),
            SaveConfigResult = Result<VoiceReceptionistConfig>.Failure("The setting could not be saved.", VoiceFailureCodes.StoreUnavailable),
        };
        await ControllerFor(failing, log).Put(Tenant, new ConsentSettingRequest(VoiceConsentModes.StrictKeypress), CancellationToken.None);

        Assert.NotEmpty(log.Lines);
        foreach (var line in log.Lines)
        {
            Assert.DoesNotContain("GREETING-SENTINEL", line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("PROMPT-SENTINEL", line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("HOURS-SENTINEL", line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("QUESTION-SENTINEL", line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("KEYWORD-SENTINEL", line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(TransferNumber, line, StringComparison.Ordinal);
            Assert.DoesNotContain("owner@acme-plumbing.example", line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Acme", line, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains(
            log.Lines,
            l => l.Contains(Tenant.ToString(), StringComparison.Ordinal) && l.Contains(VoiceConsentModes.StrictKeypress, StringComparison.Ordinal));
    }
}
