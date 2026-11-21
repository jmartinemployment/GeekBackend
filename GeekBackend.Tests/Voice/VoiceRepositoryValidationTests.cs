using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using GeekApplication.Models.Voice;
using GeekRepository.Infrastructure;
using GeekRepository.Repositories.Voice;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeekBackend.Tests.Voice;

/// <summary>
/// Every refusal the store makes before it touches the database, proved with a connection factory that throws if it is asked for a
/// connection: a bad request comes back as invalid_request and never opens one. What the database itself enforces (row-level
/// security, the unique and check constraints, the transaction around an activation) needs a real Postgres and is in
/// GeekBackend.IntegrationTests/VoiceIntegrationTests.cs.
/// </summary>
public sealed class VoiceRepositoryValidationTests
{
    private sealed class NoDatabase : IDbConnectionFactory
    {
        public IDbConnection CreateConnection() => throw new InvalidOperationException("This test must be refused before it asks for a connection.");
    }

    private static readonly Guid Tenant = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonElement EmptyList = JsonDocument.Parse("[]").RootElement;
    private static readonly JsonElement EmptyObject = JsonDocument.Parse("{}").RootElement;

    private static VoiceRepository Store() => new(new NoDatabase(), NullLogger<VoiceRepository>.Instance);


    private static CreateVoiceTenantCommand GoodTenant() => new(
        "Boca Cool HVAC", "Plumbing, heating, & air-conditioning contractors", Now, "US", "FL", "dispatch@example.test", "+15615550101") { IndustryAnswers = IndustryAnswersFixture.AllNo(Now), Attestation = AttestationFixture.Good(Now) };

    private static ActivateVoiceTenantCommand GoodActivation() => new(
        VoiceSubscriptionTiers.Standard, 48m, 250, 0.50m, "I-TEST", "VAULT-TEST", Now, Now.AddMonths(1), "active",
        "ACtest", "ciphertext", "+15615550199", "PNtest", null, null, "not_started", false);

    private static VoiceReceptionistConfig GoodConfig() => new(
        "Thanks for calling.", "You answer for {business}.", EmptyObject, EmptyList,
        VoiceTransferModes.WarmTransfer, "+15615550102", VoiceTransferCallerIdModes.OriginalCaller, ["flood", "gas leak"], true);

    private static async Task AssertInvalid<T>(Task<GeekApplication.Results.Result<T>> call)
    {
        var result = await call;
        Assert.False(result.IsSuccess);
        Assert.Equal(VoiceFailureCodes.InvalidRequest, result.ErrorCode);
    }

    // ----- Tenants -----

    [Theory]
    [InlineData("", "HVAC", "US", "FL", "a@b.example", "+15615550101")]
    [InlineData("Acme", "", "US", "FL", "a@b.example", "+15615550101")]
    [InlineData("Acme", "HVAC", "CA", "FL", "a@b.example", "+15615550101")]
    [InlineData("Acme", "HVAC", "US", "Florida", "a@b.example", "+15615550101")]
    // Both are ciphertext here, so blank is the whole of what GeekRepository can refuse. "no-at-sign" and "561-555-0101" are refused
    // in GeekAPI before sealing (VoicePersonalDetailsTests), which is the last place that holds them in the clear.
    [InlineData("Acme", "HVAC", "US", "FL", "", "+15615550101")]
    [InlineData("Acme", "HVAC", "US", "FL", "a@b.example", "")]
    public async Task A_tenant_with_a_bad_field_is_refused(string name, string industry, string country, string state, string email, string phone) =>
        await AssertInvalid(Store().CreateTenantAsync(new(name, industry, Now, country, state, email, phone) { IndustryAnswers = IndustryAnswersFixture.AllNo(Now), Attestation = AttestationFixture.Good(Now) }));

    public static TheoryData<string, Func<VoiceSignupAttestation>> AttestationsThatCannotBeStored() => new()
    {
        { "no declarant name", () => AttestationFixture.Good(Now) with { AttestorNameEncrypted = " " } },
        { "no declarant title", () => AttestationFixture.Good(Now) with { AttestorTitleEncrypted = "" } },
        { "no business address", () => AttestationFixture.Good(Now) with { BusinessAddressEncrypted = "" } },
        { "no declarant address", () => AttestationFixture.Good(Now) with { DeclarantAddressEncrypted = " " } },
        { "no declarant telephone", () => AttestationFixture.Good(Now) with { DeclarantPhoneEncrypted = "" } },
        { "no declaration words", () => AttestationFixture.Good(Now) with { DeclarationStatement = "" } },
        { "no terms words", () => AttestationFixture.Good(Now) with { TermsStatement = " " } },
        { "no visitor address", () => AttestationFixture.Good(Now) with { IpAddress = "" } },
        { "no visitor country", () => AttestationFixture.Good(Now) with { VisitorCountry = "" } },
    };

    [Theory]
    [MemberData(nameof(AttestationsThatCannotBeStored))]
    public async Task A_tenant_whose_declaration_is_incomplete_is_refused(string reason, Func<VoiceSignupAttestation> attestation)
    {
        Assert.NotEmpty(reason);
        await AssertInvalid(Store().CreateTenantAsync(GoodTenant() with { Attestation = attestation() }));
    }

    public static TheoryData<string, Func<VoiceIndustryAnswerSet>> AnswerSetsThatCannotBeStored() => new()
    {
        { "no answers", () => IndustryAnswersFixture.AllNo(Now) with { Answers = [] } },
        { "a blank industry", () => IndustryAnswersFixture.AllNo(Now) with { Answers = [new VoiceIndustryAnswer(" ", "Is the business a healthcare business?", false)] } },
        { "a blank question", () => IndustryAnswersFixture.AllNo(Now) with { Answers = [new VoiceIndustryAnswer("healthcare", "", false)] } },
        { "an industry answered twice", () => IndustryAnswersFixture.AllNo(Now) with { Answers = [new VoiceIndustryAnswer("healthcare", "q", false), new VoiceIndustryAnswer("healthcare", "q", false)] } },
        { "no visitor address", () => IndustryAnswersFixture.AllNo(Now) with { IpAddress = "" } },
        { "an address that is not one", () => IndustryAnswersFixture.AllNo(Now) with { IpAddress = "not an address" } },
        { "no visitor country", () => IndustryAnswersFixture.AllNo(Now) with { VisitorCountry = " " } },
    };

    [Theory]
    [MemberData(nameof(AnswerSetsThatCannotBeStored))]
    public async Task A_tenant_whose_industry_answers_cannot_be_stored_is_refused(string why, Func<VoiceIndustryAnswerSet> answers)
    {
        var result = await Store().CreateTenantAsync(GoodTenant() with { IndustryAnswers = answers() });

        Assert.True(VoiceFailureCodes.InvalidRequest == result.ErrorCode, why);
    }

    [Fact]
    public async Task A_tenant_that_answered_yes_to_an_excluded_industry_is_refused()
    {
        var yes = IndustryAnswersFixture.AllNo(Now);
        yes = yes with { Answers = yes.Answers.Select((a, i) => i == 4 ? a with { Answer = true } : a).ToList() };

        var result = await Store().CreateTenantAsync(GoodTenant() with { IndustryAnswers = yes });

        Assert.Equal(VoiceFailureCodes.InvalidRequest, result.ErrorCode);
    }

    [Theory]
    [MemberData(nameof(AnswerSetsThatCannotBeStored))]
    public async Task A_refusal_whose_answers_cannot_be_stored_is_refused(string why, Func<VoiceIndustryAnswerSet> answers)
    {
        var result = await Store().RecordIndustryRefusalAsync(answers());

        Assert.True(VoiceFailureCodes.InvalidRequest == result.ErrorCode, why);
    }

    [Fact]
    public async Task A_refusal_with_no_yes_in_it_is_refused()
    {
        var result = await Store().RecordIndustryRefusalAsync(IndustryAnswersFixture.AllNo(Now));

        Assert.Equal(VoiceFailureCodes.InvalidRequest, result.ErrorCode);
    }

    // ----- Activation -----

    [Fact]
    public async Task An_unknown_tier_is_refused() =>
        await AssertInvalid(Store().ActivateTenantAsync(Tenant, GoodActivation() with { TierCode = "GOLD" }));

    [Fact]
    public async Task A_negative_price_is_refused() =>
        await AssertInvalid(Store().ActivateTenantAsync(Tenant, GoodActivation() with { MonthlyPriceUsd = -1m }));

    [Fact]
    public async Task A_billing_period_that_ends_before_it_starts_is_refused() =>
        await AssertInvalid(Store().ActivateTenantAsync(Tenant, GoodActivation() with { CurrentPeriodEnd = Now.AddDays(-1) }));

    [Fact]
    public async Task A_missing_vault_token_is_refused() =>
        await AssertInvalid(Store().ActivateTenantAsync(Tenant, GoodActivation() with { PaypalVaultTokenId = " " }));

    [Fact]
    public async Task An_assigned_number_that_is_not_e164_is_refused() =>
        await AssertInvalid(Store().ActivateTenantAsync(Tenant, GoodActivation() with { AssignedPhoneNumber = "5615550199" }));

    // ----- Settings -----

    [Fact]
    public async Task An_unknown_transfer_mode_is_refused() =>
        await AssertInvalid(Store().SaveConfigAsync(Tenant, GoodConfig() with { TransferMode = "teleport" }));

    [Fact]
    public async Task An_unknown_caller_id_mode_is_refused() =>
        await AssertInvalid(Store().SaveConfigAsync(Tenant, GoodConfig() with { TransferCallerIdMode = "anonymous" }));

    [Fact]
    public async Task A_blank_emergency_keyword_is_refused() =>
        await AssertInvalid(Store().SaveConfigAsync(Tenant, GoodConfig() with { EmergencyKeywords = ["flood", "  "] }));

    [Fact]
    public async Task Missing_business_hours_are_refused() =>
        await AssertInvalid(Store().SaveConfigAsync(Tenant, GoodConfig() with { BusinessHours = default }));

    [Fact]
    public async Task A_blank_greeting_is_refused() =>
        await AssertInvalid(Store().SaveConfigAsync(Tenant, GoodConfig() with { GreetingMessage = "" }));

    /// <summary>
    /// The destination is ciphertext here, so this is the whole of what GeekRepository can still check: that the business sent one.
    /// Whether it is an address at all is checked in GeekAPI before sealing (VoicePersonalDetails), which is the last place that holds
    /// it in the clear - "no-at-sign" and "Owner &lt;owner@b.example&gt;" are refused there, and tested there.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Message_email_enabled_without_a_destination_is_refused(string? destination) =>
        await AssertInvalid(Store().SaveConfigAsync(Tenant, GoodConfig() with { MessageEmailEnabled = true, MessageDestinationEmailEncrypted = destination }));

    [Fact]
    public async Task An_unknown_calendar_provider_is_refused() =>
        await AssertInvalid(Store().GetMcpConnectionAsync(Tenant, "icloud"));

    [Fact]
    public async Task Saving_a_connection_for_an_unknown_provider_is_refused() =>
        await AssertInvalid(Store().SaveMcpConnectionAsync(Tenant, "icloud", new("access", "refresh", null, null)));

    [Theory]
    [InlineData("", "refresh")]
    [InlineData("access", "")]
    [InlineData("  ", "refresh")]
    [InlineData("access", "  ")]
    public async Task Saving_a_connection_without_both_tokens_is_refused(string access, string refresh) =>
        await AssertInvalid(Store().SaveMcpConnectionAsync(Tenant, VoiceMcpProviders.GoogleCalendar, new(access, refresh, null, null)));

    /// <summary>
    /// Withdrawing a connection for a provider this platform has no such thing as is the caller's mistake, refused the same way saving
    /// one is. Answering "there was nothing to remove" instead would report a withdrawal that never happened, and the owner's real
    /// connection would still be there holding their tokens.
    /// </summary>
    [Fact]
    public async Task Disconnecting_an_unknown_provider_is_refused() =>
        await AssertInvalid(Store().DisconnectMcpAsync(Tenant, "icloud"));

    [Fact]
    public async Task Saving_a_missing_connection_is_refused() =>
        await AssertInvalid(Store().SaveMcpConnectionAsync(Tenant, VoiceMcpProviders.GoogleCalendar, null!));

    // ----- Calls and appointments -----

    private static UpsertVoiceCallCommand GoodCall() => new("CAtest", "completed", Now, Now.AddMinutes(3), 180, 3, null);

    [Fact]
    public async Task A_call_needs_its_twilio_sid_and_status()
    {
        await AssertInvalid(Store().UpsertCallAsync(Tenant, GoodCall() with { TwilioCallSid = " " }));
        await AssertInvalid(Store().UpsertCallAsync(Tenant, GoodCall() with { TwilioCallSid = "" }));
        await AssertInvalid(Store().UpsertCallAsync(Tenant, GoodCall() with { CallStatus = "" }));
    }

    [Fact]
    public async Task A_call_that_ends_before_it_starts_is_refused() =>
        await AssertInvalid(Store().UpsertCallAsync(Tenant, GoodCall() with { EndedAt = Now.AddMinutes(-1) }));

    [Fact]
    public async Task A_negative_duration_is_refused() =>
        await AssertInvalid(Store().UpsertCallAsync(Tenant, GoodCall() with { DurationSeconds = -1 }));

    private static CreateVoiceAppointmentCommand GoodAppointment() => new(
        null, Now.AddDays(1), Now.AddDays(1).AddHours(1), null, VoiceMcpProviders.GoogleCalendar);

    [Fact]
    public async Task An_appointment_that_ends_before_it_starts_is_refused() =>
        await AssertInvalid(Store().CreateAppointmentAsync(Tenant, GoodAppointment() with { ScheduledEnd = Now }));

    [Fact]
    public async Task An_appointment_with_an_unknown_calendar_provider_is_refused() =>
        await AssertInvalid(Store().CreateAppointmentAsync(Tenant, GoodAppointment() with { McpProvider = "icloud" }));

    [Fact]
    public void An_appointment_cannot_carry_a_caller_name_number_address_or_problem()
    {
        // Zero Data Retention is enforced by the record having nowhere to put call content, not by a rule that asks it not to.
        var fields = typeof(CreateVoiceAppointmentCommand).GetProperties().Select(p => p.Name).ToArray();

        Assert.Equal(["CallLogId", "ScheduledStart", "ScheduledEnd", "ExternalEventId", "McpProvider"], fields);
    }

    // ----- The script and the vocabularies agree -----

    private static readonly string SqlDirectory = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "GeekRepository", "Migrations", "Sql"));

    private static readonly string SqlPath = Path.Combine(SqlDirectory, "0060_geek_voice.sql");

    private static readonly string RepositoryDirectory = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "GeekRepository", "Repositories", "Voice"));

    private static string Sql => Regex.Replace(File.ReadAllText(SqlPath), @"--[^\n]*", string.Empty);

    /// <summary>One migration's statements, for a list the baseline no longer has the last word on.</summary>
    private static string SqlOf(string migration) =>
        Regex.Replace(File.ReadAllText(Path.Combine(SqlDirectory, migration)), @"--[^\n]*", string.Empty);

    /// <summary>
    /// The business's indexed site is a scope the receptionist retrieves by, so the column the migration adds and the column the
    /// repository reads have to be the same one. They were written in different files, and a knowledge tool that silently reads nothing
    /// is indistinguishable from a business that has published nothing.
    /// </summary>
    [Fact]
    public void The_knowledge_scope_column_the_migration_adds_is_the_one_the_repository_reads()
    {
        var migration = SqlOf("0081_geek_voice_knowledge_run.sql");

        Assert.Contains("geek_voice.receptionist_configs", migration, StringComparison.Ordinal);
        Assert.Contains("knowledge_run_id UUID", migration, StringComparison.Ordinal);

        var repository = File.ReadAllText(Path.Combine(RepositoryDirectory, "VoiceRepository.cs"));
        Assert.Contains("knowledge_run_id AS KnowledgeRunId", repository, StringComparison.Ordinal);
        Assert.Contains("knowledge_run_id = EXCLUDED.knowledge_run_id", repository, StringComparison.Ordinal);
    }

    /// <summary>
    /// A withdrawal deletes the row and names its tenant while doing it. Both halves are read off the statements themselves because
    /// both are what the owner was promised: the credential goes rather than being left behind under a flag, and the WHERE clause —
    /// not row-level security, which the owning login bypasses — is what keeps one owner's withdrawal off another owner's row.
    /// </summary>
    [Fact]
    public void A_withdrawal_deletes_the_row_and_names_its_tenant()
    {
        // Comment lines go first: what a doc comment says about a flag is not what the code does with one.
        var repository = Regex.Replace(File.ReadAllText(Path.Combine(RepositoryDirectory, "VoiceRepository.cs")), @"^\s*//.*$", string.Empty, RegexOptions.Multiline);

        var deletes = Regex.Matches(repository, @"DELETE FROM geek_voice\.(\w+)\s+WHERE ([^;]+);");
        Assert.Equal(2, deletes.Count);
        Assert.Equal(
            ["storage_connections", "tenant_mcp_connections"],
            deletes.Select(d => d.Groups[1].Value).OrderBy(t => t, StringComparer.Ordinal));
        Assert.All(deletes, d => Assert.Contains("tenant_id = @TenantId", d.Groups[2].Value, StringComparison.Ordinal));
        Assert.Contains("provider = @Provider", deletes.Single(d => d.Groups[1].Value == "tenant_mcp_connections").Groups[2].Value, StringComparison.Ordinal);

        // Nothing is deactivated in a withdrawal's place: a credential the owner withdrew is gone, not kept with a flag turned off.
        Assert.DoesNotContain("is_active = FALSE", repository, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_vocabulary_value_is_in_the_script_and_the_script_has_no_other_value()
    {
        AssertCheckList("status", VoiceTenantStatuses.All);
        AssertCheckList("tier_code", VoiceSubscriptionTiers.All);
        AssertCheckList("transfer_mode", VoiceTransferModes.All);
        AssertCheckList("transfer_caller_id_mode", VoiceTransferCallerIdModes.All);
        // Widened when Drive joined the providers a tenant can connect, so the baseline is no longer where this list lives.
        AssertCheckList("provider", VoiceMcpProviders.All, "0078_geek_voice_google_drive_provider.sql");
        // Narrowed when HubSpot stopped being filed as a calendar, so the baseline is no longer where this list lives either.
        AssertCheckList("mcp_provider", VoiceMcpProviders.Calendars, "0086_geek_voice_appointment_providers.sql");
    }


    [Fact]
    public void Every_table_has_row_level_security_forced_and_a_policy()
    {
        var dropped = Regex.Matches(SqlOf("0080_geek_voice_drop_knowledge.sql"), @"DROP TABLE IF EXISTS geek_voice\.(\w+)")
            .Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        var tables = Regex.Matches(Sql, @"CREATE TABLE IF NOT EXISTS geek_voice\.(\w+)")
            .Select(m => m.Groups[1].Value).Where(t => !dropped.Contains(t)).ToList();
        Assert.Equal(7, tables.Count);
        foreach (var table in tables)
        {
            Assert.Contains($"ALTER TABLE geek_voice.{table}", Sql, StringComparison.Ordinal);
            Assert.Matches(new Regex($@"ALTER TABLE geek_voice\.{table}\s+ENABLE ROW LEVEL SECURITY"), Sql);
            Assert.Matches(new Regex($@"ALTER TABLE geek_voice\.{table}\s+FORCE ROW LEVEL SECURITY"), Sql);
            Assert.Matches(new Regex($@"CREATE POLICY tenant_isolation ON geek_voice\.{table}\b"), Sql);
        }
    }

    [Fact]
    public void The_script_stands_alone_from_the_other_schemas()
    {
        Assert.DoesNotContain("geek_crm", Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("geek_social", Sql, StringComparison.Ordinal);
    }

    private static void AssertCheckList(string column, IReadOnlyList<string> values, string? migration = null)
    {
        // CHECK (column IN ('a', 'b')) -- the list in force for this column, in the baseline unless a later migration replaced it.
        var sql = migration is null ? Sql : SqlOf(migration);
        var match = Regex.Match(sql, $@"CHECK \({column} IN \(([^)]*)\)\)");
        Assert.True(match.Success, $"No CHECK list for {column}.");
        var inScript = Regex.Matches(match.Groups[1].Value, "'([^']+)'").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(values.OrderBy(v => v, StringComparer.Ordinal), inScript.OrderBy(v => v, StringComparer.Ordinal));
    }
}
