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

    private static IReadOnlyList<float> Embedding(int length = VoiceEmbedding.Dimensions) => Enumerable.Repeat(0.5f, length).ToList();

    private static CreateVoiceTenantCommand GoodTenant() => new(
        "Boca Cool HVAC", "Plumbing, heating, & air-conditioning contractors", Now, "US", "FL", "dispatch@example.test", "+15615550101") { IndustryAnswers = IndustryAnswersFixture.AllNo(Now) };

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
    [InlineData("Acme", "HVAC", "US", "FL", "no-at-sign", "+15615550101")]
    [InlineData("Acme", "HVAC", "US", "FL", "a@b.example", "561-555-0101")]
    public async Task A_tenant_with_a_bad_field_is_refused(string name, string industry, string country, string state, string email, string phone) =>
        await AssertInvalid(Store().CreateTenantAsync(new(name, industry, Now, country, state, email, phone) { IndustryAnswers = IndustryAnswersFixture.AllNo(Now) }));

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
    public async Task A_transfer_number_that_is_not_e164_is_refused() =>
        await AssertInvalid(Store().SaveConfigAsync(Tenant, GoodConfig() with { TransferTargetNumber = "call me" }));

    [Fact]
    public async Task A_blank_emergency_keyword_is_refused() =>
        await AssertInvalid(Store().SaveConfigAsync(Tenant, GoodConfig() with { EmergencyKeywords = ["flood", "  "] }));

    [Fact]
    public async Task Missing_business_hours_are_refused() =>
        await AssertInvalid(Store().SaveConfigAsync(Tenant, GoodConfig() with { BusinessHours = default }));

    [Fact]
    public async Task A_blank_greeting_is_refused() =>
        await AssertInvalid(Store().SaveConfigAsync(Tenant, GoodConfig() with { GreetingMessage = "" }));

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

    [Fact]
    public async Task Saving_a_missing_connection_is_refused() =>
        await AssertInvalid(Store().SaveMcpConnectionAsync(Tenant, VoiceMcpProviders.GoogleCalendar, null!));

    // ----- Knowledge -----

    private static CreateVoiceKnowledgeDocumentCommand GoodDocument(params VoiceKnowledgeChunk[] chunks) =>
        new(VoiceKnowledgeSourceTypes.GuidedTradeForm, "Hours and prices", null, "We open at eight.", chunks);

    [Fact]
    public async Task A_document_with_no_chunks_is_refused() =>
        await AssertInvalid(Store().CreateKnowledgeDocumentAsync(Tenant, GoodDocument()));

    [Fact]
    public async Task An_embedding_of_the_wrong_length_is_refused() =>
        await AssertInvalid(Store().CreateKnowledgeDocumentAsync(Tenant, GoodDocument(new VoiceKnowledgeChunk(0, "text", Embedding(383)))));

    [Fact]
    public async Task An_embedding_with_a_value_that_is_not_a_number_is_refused()
    {
        var bad = Embedding().ToArray();
        bad[10] = float.NaN;
        await AssertInvalid(Store().CreateKnowledgeDocumentAsync(Tenant, GoodDocument(new VoiceKnowledgeChunk(0, "text", bad))));
    }

    [Fact]
    public async Task Two_chunks_with_the_same_number_are_refused() =>
        await AssertInvalid(Store().CreateKnowledgeDocumentAsync(Tenant, GoodDocument(new VoiceKnowledgeChunk(0, "a", Embedding()), new VoiceKnowledgeChunk(0, "b", Embedding()))));

    [Fact]
    public async Task An_unknown_source_type_is_refused() =>
        await AssertInvalid(Store().CreateKnowledgeDocumentAsync(Tenant, GoodDocument(new VoiceKnowledgeChunk(0, "a", Embedding())) with { SourceType = "markdown_blob" }));

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public async Task A_search_limit_outside_the_range_is_refused(int limit) =>
        await AssertInvalid(Store().SearchKnowledgeAsync(Tenant, new(Embedding(), limit)));

    [Fact]
    public async Task A_search_embedding_of_the_wrong_length_is_refused() =>
        await AssertInvalid(Store().SearchKnowledgeAsync(Tenant, new(Embedding(10), 4)));

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
        null, "Pat Jones", "+15615550112", null, "12 Ocean Ave", "No cooling", Now.AddDays(1), Now.AddDays(1).AddHours(1), null, VoiceMcpProviders.GoogleCalendar);

    [Fact]
    public async Task An_appointment_that_ends_before_it_starts_is_refused() =>
        await AssertInvalid(Store().CreateAppointmentAsync(Tenant, GoodAppointment() with { ScheduledEnd = Now }));

    [Fact]
    public async Task An_appointment_with_an_unknown_calendar_provider_is_refused() =>
        await AssertInvalid(Store().CreateAppointmentAsync(Tenant, GoodAppointment() with { McpProvider = "icloud" }));

    [Fact]
    public async Task An_appointment_with_no_address_is_refused() =>
        await AssertInvalid(Store().CreateAppointmentAsync(Tenant, GoodAppointment() with { ServiceAddress = "" }));

    // ----- The script and the vocabularies agree -----

    private static readonly string SqlPath = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "GeekRepository", "Migrations", "Sql", "0060_geek_voice.sql"));

    private static string Sql => Regex.Replace(File.ReadAllText(SqlPath), @"--[^\n]*", string.Empty);

    [Fact]
    public void Every_vocabulary_value_is_in_the_script_and_the_script_has_no_other_value()
    {
        AssertCheckList("status", VoiceTenantStatuses.All);
        AssertCheckList("tier_code", VoiceSubscriptionTiers.All);
        AssertCheckList("transfer_mode", VoiceTransferModes.All);
        AssertCheckList("transfer_caller_id_mode", VoiceTransferCallerIdModes.All);
        AssertCheckList("provider", VoiceMcpProviders.All);
        AssertCheckList("mcp_provider", VoiceMcpProviders.All);
        AssertCheckList("source_type", VoiceKnowledgeSourceTypes.All);
    }

    [Fact]
    public void The_embedding_column_has_the_dimensions_the_code_checks()
    {
        Assert.Contains($"embedding     vector({VoiceEmbedding.Dimensions})", Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_table_has_row_level_security_forced_and_a_policy()
    {
        var tables = Regex.Matches(Sql, @"CREATE TABLE IF NOT EXISTS geek_voice\.(\w+)").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(9, tables.Count);
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

    private static void AssertCheckList(string column, IReadOnlyList<string> values)
    {
        // CHECK (column IN ('a', 'b')) -- the list in the script for this column.
        var match = Regex.Match(Sql, $@"CHECK \({column} IN \(([^)]*)\)\)");
        Assert.True(match.Success, $"No CHECK list for {column}.");
        var inScript = Regex.Matches(match.Groups[1].Value, "'([^']+)'").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(values.OrderBy(v => v, StringComparer.Ordinal), inScript.OrderBy(v => v, StringComparer.Ordinal));
    }
}
