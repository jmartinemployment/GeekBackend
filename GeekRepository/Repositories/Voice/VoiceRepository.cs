using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;
using GeekRepository.Infrastructure;
using Npgsql;

using static GeekRepository.Repositories.Voice.VoiceResults;

namespace GeekRepository.Repositories.Voice;

/// <summary>
/// geek_voice over Dapper (docs/Geek-Receptionist.md sections 1 and 3). Every connection it opens for a tenant first runs
/// <c>set_config('app.current_tenant_id', tenant, false)</c> so row-level security applies, and every statement also names its
/// tenant in a WHERE clause. Each call runs once: a database fault that is not a refusal below is raised, not retried, not
/// defaulted. Logs carry ids and codes, never a name, number, address, transcript or token.
/// </summary>
public sealed partial class VoiceRepository(IDbConnectionFactory db, ILogger<VoiceRepository> logger) : IVoiceStore, IVoiceSignupAnswerStore, IVoiceBillingExemption
{
    private readonly ILogger<VoiceRepository> _logger = logger;

    private const string TenantColumns = """
        id AS Id, business_name AS BusinessName, industry_vertical AS IndustryVertical,
        compliance_attested_at AS ComplianceAttestedAt, country_code AS CountryCode, state_code AS StateCode,
        contact_email AS ContactEmail, contact_phone AS ContactPhone, status AS Status, created_at AS CreatedAt
        """;
    private const string ConfigColumns = """
        greeting_message AS GreetingMessage, system_prompt_template AS SystemPromptTemplate,
        business_hours_json::text AS BusinessHours, qualifying_questions_json::text AS QualifyingQuestions,
        transfer_mode AS TransferMode, transfer_target_number AS TransferTargetNumber,
        transfer_caller_id_mode AS TransferCallerIdMode, emergency_keywords AS EmergencyKeywords,
        recording_disclosure_enabled AS RecordingDisclosureEnabled,
        message_email_enabled AS MessageEmailEnabled, message_destination_email AS MessageDestinationEmail,
        include_transcript AS IncludeTranscript, notify_on_abandoned_calls AS NotifyOnAbandonedCalls,
        booking_enabled AS BookingEnabled, consent_mode AS ConsentMode,
        knowledge_run_id AS KnowledgeRunId
        """;

    [GeneratedRegex(@"^\+[1-9]\d{6,14}$")]
    private static partial Regex E164();

    // ----- Tenants -----

    public async Task<Result<VoiceTenant>> CreateTenantAsync(CreateVoiceTenantCommand command, CancellationToken ct = default)
    {
        var problem = ValidateCreateTenant(command);
        if (problem is not null) return Invalid<VoiceTenant>(problem);

        // The id is chosen here, before the insert, because the policy on tenants matches the row's own id against the session
        // setting: the connection has to name the new tenant before the row exists.
        var tenantId = Guid.NewGuid();
        try
        {
            await using var conn = await OpenForTenantAsync(tenantId, ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            var row = await conn.QuerySingleAsync<TenantRow>(new CommandDefinition($"""
                INSERT INTO geek_voice.tenants (
                    id, business_name, industry_vertical,
                    compliance_attested_at, country_code, state_code, contact_email, contact_phone)
                VALUES (
                    @TenantId, @BusinessName, @IndustryVertical,
                    @ComplianceAttestedAt, @CountryCode, @StateCode, @ContactEmail, @ContactPhone)
                RETURNING {TenantColumns};
                """,
                new
                {
                    TenantId = tenantId,
                    command.BusinessName,
                    command.IndustryVertical,
                    ComplianceAttestedAt = command.ComplianceAttestedAt.UtcDateTime,
                    command.CountryCode,
                    command.StateCode,
                    command.ContactEmail,
                    command.ContactPhone,
                },
                tx,
                cancellationToken: ct));
            // The tenant and its answers are one write: a tenant never exists without the record of what it answered.
            await InsertIndustryAnswersAsync(conn, tx, tenantId, command.IndustryAnswers, ct);
            await tx.CommitAsync(ct);
            return Result<VoiceTenant>.Success(row.ToModel());
        }
        catch (PostgresException ex) when (IsRefusal(ex))
        {
            return Refused<VoiceTenant>(ex, "The tenant was refused by the database.");
        }
    }

    // ----- Excluded-industry answers (kept forever: rows are only ever added) -----

    public async Task<Result<VoiceIndustryAnswersRecorded>> RecordIndustryRefusalAsync(VoiceIndustryAnswerSet answers, CancellationToken ct = default)
    {
        var problem = ValidateIndustryAnswers(answers);
        if (problem is not null) return Invalid<VoiceIndustryAnswersRecorded>(problem);
        if (!answers.Answers.Any(a => a.Answer))
            return Invalid<VoiceIndustryAnswersRecorded>("A refusal has a yes to at least one excluded industry.");

        try
        {
            await using var conn = (NpgsqlConnection)db.CreateConnection();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            await InsertIndustryAnswersAsync(conn, tx, null, answers, ct);
            await tx.CommitAsync(ct);
            return Result<VoiceIndustryAnswersRecorded>.Success(new VoiceIndustryAnswersRecorded(answers.Answers.Count));
        }
        catch (PostgresException ex) when (IsRefusal(ex))
        {
            return Refused<VoiceIndustryAnswersRecorded>(ex, "The answers were refused by the database.");
        }
    }

    private static async Task InsertIndustryAnswersAsync(
        NpgsqlConnection conn, System.Data.Common.DbTransaction tx, Guid? tenantId, VoiceIndustryAnswerSet answers, CancellationToken ct)
    {
        foreach (var answer in answers.Answers)
        {
            await conn.ExecuteAsync(new CommandDefinition("""
                INSERT INTO geek_voice.signup_industry_answers (
                    id, tenant_id, industry, question, answer, answered_at, ip_address, visitor_country)
                VALUES (
                    @Id, @TenantId, @Industry, @Question, @Answer, @AnsweredAt, @IpAddress::inet, @VisitorCountry);
                """,
                new
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    answer.Industry,
                    answer.Question,
                    answer.Answer,
                    AnsweredAt = answers.AnsweredAt.UtcDateTime,
                    answers.IpAddress,
                    answers.VisitorCountry,
                },
                tx,
                cancellationToken: ct));
        }
    }

    public async Task<Result<VoiceTenant>> GetTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var conn = await OpenForTenantAsync(tenantId, ct);
        var row = await conn.QuerySingleOrDefaultAsync<TenantRow>(new CommandDefinition(
            $"SELECT {TenantColumns} FROM geek_voice.tenants WHERE id = @TenantId;",
            new { TenantId = tenantId },
            cancellationToken: ct));
        return row is null ? NotFound<VoiceTenant>("The tenant was not found.") : Result<VoiceTenant>.Success(row.ToModel());
    }

    public async Task<Result<VoiceTenant>> ActivateWithoutBillingAsync(Guid tenantId, CancellationToken ct = default)
    {
        try
        {
            await using var conn = await OpenForTenantAsync(tenantId, ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            var status = await conn.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
                "SELECT status FROM geek_voice.tenants WHERE id = @TenantId FOR UPDATE;",
                new { TenantId = tenantId },
                transaction: tx,
                cancellationToken: ct));
            if (status is null)
            {
                await tx.RollbackAsync(ct);
                return NotFound<VoiceTenant>("The tenant was not found.");
            }

            if (!string.Equals(status, VoiceTenantStatuses.PendingPayment, StringComparison.Ordinal))
            {
                await tx.RollbackAsync(ct);
                return Result<VoiceTenant>.Failure("The tenant is not waiting for payment, so it cannot be activated.", VoiceFailureCodes.Conflict);
            }

            // No tenant_subscriptions row and no tenant_telephony row: an exempt tenant is billed by nobody and owns no Twilio subaccount.
            var row = await conn.QuerySingleAsync<TenantRow>(new CommandDefinition($"""
                UPDATE geek_voice.tenants SET status = 'active', updated_at = NOW()
                WHERE id = @TenantId
                RETURNING {TenantColumns};
                """,
                new { TenantId = tenantId },
                transaction: tx,
                cancellationToken: ct));

            await tx.CommitAsync(ct);
            return Result<VoiceTenant>.Success(row.ToModel());
        }
        catch (PostgresException ex) when (IsRefusal(ex))
        {
            return Refused<VoiceTenant>(ex, "The activation was refused by the database.");
        }
    }

    public async Task<Result<VoiceTenant>> ActivateTenantAsync(Guid tenantId, ActivateVoiceTenantCommand command, CancellationToken ct = default)
    {
        var problem = ValidateActivation(command);
        if (problem is not null) return Invalid<VoiceTenant>(problem);

        try
        {
            await using var conn = await OpenForTenantAsync(tenantId, ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            var status = await conn.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
                "SELECT status FROM geek_voice.tenants WHERE id = @TenantId FOR UPDATE;",
                new { TenantId = tenantId },
                transaction: tx,
                cancellationToken: ct));
            if (status is null)
            {
                await tx.RollbackAsync(ct);
                return NotFound<VoiceTenant>("The tenant was not found.");
            }

            if (!string.Equals(status, VoiceTenantStatuses.PendingPayment, StringComparison.Ordinal))
            {
                await tx.RollbackAsync(ct);
                return Result<VoiceTenant>.Failure("The tenant is not waiting for payment, so it cannot be activated.", VoiceFailureCodes.Conflict);
            }

            await conn.ExecuteAsync(new CommandDefinition("""
                INSERT INTO geek_voice.tenant_subscriptions (
                    tenant_id, tier_code, monthly_price_usd, included_minutes, overage_rate_usd, paypal_subscription_id,
                    paypal_vault_token_id, current_period_start, current_period_end, billing_status)
                VALUES (
                    @TenantId, @TierCode, @MonthlyPriceUsd, @IncludedMinutes, @OverageRateUsd, @PaypalSubscriptionId,
                    @PaypalVaultTokenId, @CurrentPeriodStart, @CurrentPeriodEnd, @BillingStatus);
                """,
                new
                {
                    TenantId = tenantId,
                    command.TierCode,
                    command.MonthlyPriceUsd,
                    command.IncludedMinutes,
                    command.OverageRateUsd,
                    command.PaypalSubscriptionId,
                    command.PaypalVaultTokenId,
                    CurrentPeriodStart = command.CurrentPeriodStart.UtcDateTime,
                    CurrentPeriodEnd = command.CurrentPeriodEnd.UtcDateTime,
                    command.BillingStatus,
                },
                transaction: tx,
                cancellationToken: ct));

            await conn.ExecuteAsync(new CommandDefinition("""
                INSERT INTO geek_voice.tenant_telephony (
                    tenant_id, twilio_subaccount_sid, twilio_auth_token_encrypted, assigned_phone_number, twilio_phone_sid,
                    a2p_brand_sid, a2p_campaign_sid, a2p_status, sms_notifications_enabled)
                VALUES (
                    @TenantId, @TwilioSubaccountSid, @TwilioAuthTokenEncrypted, @AssignedPhoneNumber, @TwilioPhoneSid,
                    @A2pBrandSid, @A2pCampaignSid, @A2pStatus, @SmsNotificationsEnabled);
                """,
                new
                {
                    TenantId = tenantId,
                    command.TwilioSubaccountSid,
                    command.TwilioAuthTokenEncrypted,
                    command.AssignedPhoneNumber,
                    command.TwilioPhoneSid,
                    command.A2pBrandSid,
                    command.A2pCampaignSid,
                    command.A2pStatus,
                    command.SmsNotificationsEnabled,
                },
                transaction: tx,
                cancellationToken: ct));

            var row = await conn.QuerySingleAsync<TenantRow>(new CommandDefinition($"""
                UPDATE geek_voice.tenants SET status = 'active', updated_at = NOW()
                WHERE id = @TenantId
                RETURNING {TenantColumns};
                """,
                new { TenantId = tenantId },
                transaction: tx,
                cancellationToken: ct));

            await tx.CommitAsync(ct);
            return Result<VoiceTenant>.Success(row.ToModel());
        }
        catch (PostgresException ex) when (IsRefusal(ex))
        {
            return Refused<VoiceTenant>(ex, "The activation was refused by the database.");
        }
    }

    // ----- Telephony, configuration, calendar connections -----

    public async Task<Result<VoiceTelephony>> GetTelephonyAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var conn = await OpenForTenantAsync(tenantId, ct);
        var row = await conn.QuerySingleOrDefaultAsync<TelephonyRow>(new CommandDefinition("""
            SELECT
                tenant_id AS TenantId, twilio_subaccount_sid AS TwilioSubaccountSid,
                twilio_auth_token_encrypted AS TwilioAuthTokenEncrypted, assigned_phone_number AS AssignedPhoneNumber,
                twilio_phone_sid AS TwilioPhoneSid, a2p_brand_sid AS A2pBrandSid,
                a2p_campaign_sid AS A2pCampaignSid, a2p_status AS A2pStatus, sms_notifications_enabled AS SmsNotificationsEnabled
            FROM geek_voice.tenant_telephony
            WHERE tenant_id = @TenantId;
            """,
            new { TenantId = tenantId },
            cancellationToken: ct));
        return row is null ? NotFound<VoiceTelephony>("The tenant has no telephony.") : Result<VoiceTelephony>.Success(row.ToModel());
    }

    public async Task<Result<VoiceReceptionistConfig>> GetConfigAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var conn = await OpenForTenantAsync(tenantId, ct);
        var row = await conn.QuerySingleOrDefaultAsync<ConfigRow>(new CommandDefinition(
            $"SELECT {ConfigColumns} FROM geek_voice.receptionist_configs WHERE tenant_id = @TenantId;",
            new { TenantId = tenantId },
            cancellationToken: ct));
        return row is null ? NotFound<VoiceReceptionistConfig>("The tenant has no receptionist settings.") : Result<VoiceReceptionistConfig>.Success(row.ToModel());
    }

    public async Task<Result<VoiceReceptionistConfig>> SaveConfigAsync(Guid tenantId, VoiceReceptionistConfig config, CancellationToken ct = default)
    {
        var problem = ValidateConfig(config);
        if (problem is not null) return Invalid<VoiceReceptionistConfig>(problem);

        try
        {
            await using var conn = await OpenForTenantAsync(tenantId, ct);
            var row = await conn.QuerySingleAsync<ConfigRow>(new CommandDefinition($"""
                INSERT INTO geek_voice.receptionist_configs (
                    tenant_id, greeting_message, system_prompt_template, business_hours_json, qualifying_questions_json,
                    transfer_mode, transfer_target_number, transfer_caller_id_mode, emergency_keywords, recording_disclosure_enabled,
                    message_email_enabled, message_destination_email, include_transcript, notify_on_abandoned_calls, booking_enabled, consent_mode, knowledge_run_id)
                VALUES (
                    @TenantId, @GreetingMessage, @SystemPromptTemplate, @BusinessHours::jsonb, @QualifyingQuestions::jsonb,
                    @TransferMode, @TransferTargetNumber, @TransferCallerIdMode, ARRAY(SELECT jsonb_array_elements_text(@EmergencyKeywords::jsonb)), @RecordingDisclosureEnabled,
                    @MessageEmailEnabled, @MessageDestinationEmail, @IncludeTranscript, @NotifyOnAbandonedCalls, @BookingEnabled, @ConsentMode, @KnowledgeRunId)
                ON CONFLICT (tenant_id) DO UPDATE SET
                    greeting_message = EXCLUDED.greeting_message,
                    system_prompt_template = EXCLUDED.system_prompt_template,
                    business_hours_json = EXCLUDED.business_hours_json,
                    qualifying_questions_json = EXCLUDED.qualifying_questions_json,
                    transfer_mode = EXCLUDED.transfer_mode,
                    transfer_target_number = EXCLUDED.transfer_target_number,
                    transfer_caller_id_mode = EXCLUDED.transfer_caller_id_mode,
                    emergency_keywords = EXCLUDED.emergency_keywords,
                    recording_disclosure_enabled = EXCLUDED.recording_disclosure_enabled,
                    message_email_enabled = EXCLUDED.message_email_enabled,
                    message_destination_email = EXCLUDED.message_destination_email,
                    include_transcript = EXCLUDED.include_transcript,
                    notify_on_abandoned_calls = EXCLUDED.notify_on_abandoned_calls,
                    booking_enabled = EXCLUDED.booking_enabled,
                    consent_mode = EXCLUDED.consent_mode,
                    knowledge_run_id = EXCLUDED.knowledge_run_id,
                    updated_at = NOW()
                RETURNING {ConfigColumns};
                """,
                new
                {
                    TenantId = tenantId,
                    config.GreetingMessage,
                    config.SystemPromptTemplate,
                    BusinessHours = config.BusinessHours.GetRawText(),
                    QualifyingQuestions = config.QualifyingQuestions.GetRawText(),
                    config.TransferMode,
                    config.TransferTargetNumber,
                    config.TransferCallerIdMode,
                    // Dapper expands an array parameter into a list of parameters, so the keywords travel as JSON and the SQL builds the text[].
                    EmergencyKeywords = JsonSerializer.Serialize(config.EmergencyKeywords),
                    config.RecordingDisclosureEnabled,
                    config.MessageEmailEnabled,
                    config.MessageDestinationEmail,
                    config.IncludeTranscript,
                    config.NotifyOnAbandonedCalls,
                    config.BookingEnabled,
                    config.ConsentMode,
                    config.KnowledgeRunId,
                },
                cancellationToken: ct));
            return Result<VoiceReceptionistConfig>.Success(row.ToModel());
        }
        catch (PostgresException ex) when (IsRefusal(ex))
        {
            return Refused<VoiceReceptionistConfig>(ex, "The settings were refused by the database.");
        }
    }

    public async Task<Result<VoiceMcpConnection>> GetMcpConnectionAsync(Guid tenantId, string provider, CancellationToken ct = default)
    {
        if (!VoiceMcpProviders.All.Contains(provider, StringComparer.Ordinal))
            return Invalid<VoiceMcpConnection>($"The provider must be one of: {string.Join(", ", VoiceMcpProviders.All)}.");

        await using var conn = await OpenForTenantAsync(tenantId, ct);
        var row = await conn.QuerySingleOrDefaultAsync<McpRow>(new CommandDefinition("""
            SELECT
                tenant_id AS TenantId, provider AS Provider, oauth_access_token_encrypted AS OauthAccessTokenEncrypted,
                oauth_refresh_token_encrypted AS OauthRefreshTokenEncrypted, token_expires_at AS TokenExpiresAt,
                external_calendar_id AS ExternalCalendarId, is_active AS IsActive
            FROM geek_voice.tenant_mcp_connections
            WHERE tenant_id = @TenantId AND provider = @Provider AND is_active = TRUE;
            """,
            new { TenantId = tenantId, Provider = provider },
            cancellationToken: ct));
        return row is null ? NotFound<VoiceMcpConnection>("The tenant has no active connection for that provider.") : Result<VoiceMcpConnection>.Success(row.ToModel());
    }

    public async Task<Result<VoiceMcpConnection>> SaveMcpConnectionAsync(Guid tenantId, string provider, SaveVoiceMcpConnectionCommand command, CancellationToken ct = default)
    {
        var problem = ValidateMcpConnection(provider, command);
        if (problem is not null) return Invalid<VoiceMcpConnection>(problem);

        try
        {
            await using var conn = await OpenForTenantAsync(tenantId, ct);
            var row = await conn.QuerySingleAsync<McpRow>(new CommandDefinition("""
                INSERT INTO geek_voice.tenant_mcp_connections (
                    tenant_id, provider, oauth_access_token_encrypted, oauth_refresh_token_encrypted, token_expires_at, external_calendar_id, is_active)
                VALUES (@TenantId, @Provider, @AccessToken, @RefreshToken, @TokenExpiresAt, @ExternalCalendarId, TRUE)
                ON CONFLICT (tenant_id, provider) DO UPDATE SET
                    oauth_access_token_encrypted = EXCLUDED.oauth_access_token_encrypted,
                    oauth_refresh_token_encrypted = EXCLUDED.oauth_refresh_token_encrypted,
                    token_expires_at = EXCLUDED.token_expires_at,
                    external_calendar_id = EXCLUDED.external_calendar_id,
                    is_active = TRUE
                RETURNING
                    tenant_id AS TenantId, provider AS Provider, oauth_access_token_encrypted AS OauthAccessTokenEncrypted,
                    oauth_refresh_token_encrypted AS OauthRefreshTokenEncrypted, token_expires_at AS TokenExpiresAt,
                    external_calendar_id AS ExternalCalendarId, is_active AS IsActive;
                """,
                new
                {
                    TenantId = tenantId,
                    Provider = provider,
                    AccessToken = command.OauthAccessTokenEncrypted,
                    RefreshToken = command.OauthRefreshTokenEncrypted,
                    TokenExpiresAt = command.TokenExpiresAt?.UtcDateTime,
                    command.ExternalCalendarId,
                },
                cancellationToken: ct));
            return Result<VoiceMcpConnection>.Success(row.ToModel());
        }
        catch (PostgresException ex) when (IsRefusal(ex))
        {
            return Refused<VoiceMcpConnection>(ex, "The connection was refused by the database.");
        }
    }

    /// <summary>
    /// The owner withdraws a calendar, CRM or Drive connection. The row is <b>deleted</b>, not left with <c>is_active = FALSE</c>: it
    /// holds the owner's encrypted OAuth tokens, and keeping a credential after its owner withdrew permission is retaining something
    /// they revoked. Deleting is the fail-safe reading, so it is what this does, and there is nothing left to reactivate. A connection
    /// that is already gone answers <c>Removed: false</c> and is not a failure — an owner withdrawing permission twice has still
    /// withdrawn it. An unknown provider is refused, as saving one is. Only this tenant's row can go: the tenant is named in the WHERE
    /// clause and in the connection's own session setting.
    /// </summary>
    public async Task<Result<VoiceConnectionRemoved>> DisconnectMcpAsync(Guid tenantId, string provider, CancellationToken ct = default)
    {
        if (!VoiceMcpProviders.All.Contains(provider, StringComparer.Ordinal))
            return Invalid<VoiceConnectionRemoved>($"The provider must be one of: {string.Join(", ", VoiceMcpProviders.All)}.");

        await using var conn = await OpenForTenantAsync(tenantId, ct);
        // Inactive rows go too: a withdrawn connection leaves no ciphertext behind, whatever state it was in.
        var removed = await conn.ExecuteAsync(new CommandDefinition("""
            DELETE FROM geek_voice.tenant_mcp_connections
            WHERE tenant_id = @TenantId AND provider = @Provider;
            """,
            new { TenantId = tenantId, Provider = provider },
            cancellationToken: ct));
        return Result<VoiceConnectionRemoved>.Success(new VoiceConnectionRemoved(removed > 0));
    }

    // ----- Bring-your-own-storage -----

    private const string StorageColumns = """
        tenant_id AS TenantId, provider AS Provider, access_key_id_encrypted AS AccessKeyIdEncrypted,
        secret_access_key_encrypted AS SecretAccessKeyEncrypted, bucket AS Bucket, key_prefix AS KeyPrefix,
        region AS Region, is_active AS IsActive
        """;

    public async Task<Result<VoiceStorageConnection>> GetStorageConnectionAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var conn = await OpenForTenantAsync(tenantId, ct);
        var row = await conn.QuerySingleOrDefaultAsync<VoiceStorageConnection>(new CommandDefinition($"""
            SELECT {StorageColumns}
            FROM geek_voice.storage_connections
            WHERE tenant_id = @TenantId AND is_active = TRUE;
            """,
            new { TenantId = tenantId },
            cancellationToken: ct));
        return row is null
            ? NotFound<VoiceStorageConnection>("The tenant has no active storage connection.")
            : Result<VoiceStorageConnection>.Success(row);
    }

    public async Task<Result<VoiceStorageConnection>> SaveStorageConnectionAsync(Guid tenantId, SaveVoiceStorageConnectionCommand command, CancellationToken ct = default)
    {
        var problem = ValidateStorageConnection(command);
        if (problem is not null) return Invalid<VoiceStorageConnection>(problem);

        try
        {
            await using var conn = await OpenForTenantAsync(tenantId, ct);
            var row = await conn.QuerySingleAsync<VoiceStorageConnection>(new CommandDefinition($"""
                INSERT INTO geek_voice.storage_connections (
                    tenant_id, provider, access_key_id_encrypted, secret_access_key_encrypted, bucket, key_prefix, region, is_active)
                VALUES (@TenantId, @Provider, @AccessKeyIdEncrypted, @SecretAccessKeyEncrypted, @Bucket, @KeyPrefix, @Region, TRUE)
                ON CONFLICT (tenant_id) DO UPDATE SET
                    provider = EXCLUDED.provider,
                    access_key_id_encrypted = EXCLUDED.access_key_id_encrypted,
                    secret_access_key_encrypted = EXCLUDED.secret_access_key_encrypted,
                    bucket = EXCLUDED.bucket,
                    key_prefix = EXCLUDED.key_prefix,
                    region = EXCLUDED.region,
                    is_active = TRUE,
                    updated_at = NOW()
                RETURNING {StorageColumns};
                """,
                new
                {
                    TenantId = tenantId,
                    command.Provider,
                    command.AccessKeyIdEncrypted,
                    command.SecretAccessKeyEncrypted,
                    command.Bucket,
                    command.KeyPrefix,
                    command.Region,
                },
                cancellationToken: ct));
            return Result<VoiceStorageConnection>.Success(row);
        }
        catch (PostgresException ex) when (IsRefusal(ex))
        {
            return Refused<VoiceStorageConnection>(ex, "The storage connection was refused by the database.");
        }
    }

    /// <summary>
    /// The owner withdraws the bucket their call records were written to. The row is <b>deleted</b>, not left with
    /// <c>is_active = FALSE</c>: it holds the owner's encrypted access key and secret, and keeping a credential after its owner
    /// withdrew permission is retaining something they revoked. Deleting is the fail-safe reading, so it is what this does. A
    /// connection that is already gone answers <c>Removed: false</c> and is not a failure — an owner withdrawing permission twice has
    /// still withdrawn it. One tenant per row and the tenant named in the WHERE clause: another tenant's bucket can never go with it.
    /// </summary>
    public async Task<Result<VoiceConnectionRemoved>> DisconnectStorageAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var conn = await OpenForTenantAsync(tenantId, ct);
        // Inactive rows go too: a withdrawn bucket leaves no ciphertext behind, whatever state it was in.
        var removed = await conn.ExecuteAsync(new CommandDefinition("""
            DELETE FROM geek_voice.storage_connections
            WHERE tenant_id = @TenantId;
            """,
            new { TenantId = tenantId },
            cancellationToken: ct));
        return Result<VoiceConnectionRemoved>.Success(new VoiceConnectionRemoved(removed > 0));
    }

    // ----- Calls and appointments -----

    public async Task<Result<VoiceCallStored>> UpsertCallAsync(Guid tenantId, UpsertVoiceCallCommand command, CancellationToken ct = default)
    {
        var problem = ValidateCall(command);
        if (problem is not null) return Invalid<VoiceCallStored>(problem);

        try
        {
            await using var conn = await OpenForTenantAsync(tenantId, ct);

            // The update arm names the tenant too: a call SID that already belongs to another tenant is a conflict, never an overwrite.
            var callId = await conn.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition("""
                INSERT INTO geek_voice.call_logs (
                    tenant_id, twilio_call_sid, call_status, started_at, ended_at, duration_seconds,
                    billable_minutes, disposition)
                VALUES (
                    @TenantId, @TwilioCallSid, @CallStatus, @StartedAt, @EndedAt, @DurationSeconds,
                    @BillableMinutes, @Disposition)
                ON CONFLICT (twilio_call_sid) DO UPDATE SET
                    call_status = EXCLUDED.call_status,
                    ended_at = COALESCE(EXCLUDED.ended_at, geek_voice.call_logs.ended_at),
                    duration_seconds = GREATEST(EXCLUDED.duration_seconds, geek_voice.call_logs.duration_seconds),
                    billable_minutes = GREATEST(EXCLUDED.billable_minutes, geek_voice.call_logs.billable_minutes),
                    disposition = COALESCE(EXCLUDED.disposition, geek_voice.call_logs.disposition)
                WHERE geek_voice.call_logs.tenant_id = EXCLUDED.tenant_id
                RETURNING id;
                """,
                new
                {
                    TenantId = tenantId,
                    command.TwilioCallSid,
                    command.CallStatus,
                    StartedAt = command.StartedAt.UtcDateTime,
                    EndedAt = command.EndedAt?.UtcDateTime,
                    command.DurationSeconds,
                    command.BillableMinutes,
                    command.Disposition,
                },
                cancellationToken: ct));

            return callId is null
                ? Result<VoiceCallStored>.Failure("That call SID belongs to another tenant.", VoiceFailureCodes.Conflict)
                : Result<VoiceCallStored>.Success(new VoiceCallStored(callId.Value));
        }
        catch (PostgresException ex) when (IsRefusal(ex))
        {
            return Refused<VoiceCallStored>(ex, "The call was refused by the database.");
        }
    }

    public async Task<Result<VoiceAppointmentCreated>> CreateAppointmentAsync(Guid tenantId, CreateVoiceAppointmentCommand command, CancellationToken ct = default)
    {
        var problem = ValidateAppointment(command);
        if (problem is not null) return Invalid<VoiceAppointmentCreated>(problem);

        try
        {
            await using var conn = await OpenForTenantAsync(tenantId, ct);

            // A foreign key checks that the call exists, not whose it is, so the tenant is checked here.
            if (command.CallLogId is not null)
            {
                var owned = await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
                    "SELECT EXISTS (SELECT 1 FROM geek_voice.call_logs WHERE id = @CallLogId AND tenant_id = @TenantId);",
                    new { command.CallLogId, TenantId = tenantId },
                    cancellationToken: ct));
                if (!owned) return Invalid<VoiceAppointmentCreated>("The call does not belong to this tenant.");
            }

            var appointmentId = await conn.QuerySingleAsync<Guid>(new CommandDefinition("""
                INSERT INTO geek_voice.appointments (
                    tenant_id, call_log_id, scheduled_start, scheduled_end, external_event_id, mcp_provider)
                VALUES (
                    @TenantId, @CallLogId, @ScheduledStart, @ScheduledEnd, @ExternalEventId, @McpProvider)
                RETURNING id;
                """,
                new
                {
                    TenantId = tenantId,
                    command.CallLogId,
                    ScheduledStart = command.ScheduledStart.UtcDateTime,
                    ScheduledEnd = command.ScheduledEnd.UtcDateTime,
                    command.ExternalEventId,
                    command.McpProvider,
                },
                cancellationToken: ct));
            return Result<VoiceAppointmentCreated>.Success(new VoiceAppointmentCreated(appointmentId));
        }
        catch (PostgresException ex) when (IsRefusal(ex))
        {
            return Refused<VoiceAppointmentCreated>(ex, "The appointment was refused by the database.");
        }
    }

    // ----- Connection -----

    private Task<NpgsqlConnection> OpenForTenantAsync(Guid tenantId, CancellationToken ct) =>
        VoiceConnections.OpenForTenantAsync(db, tenantId, ct);

    // ----- Failures -----

    /// <summary>Unique, check, foreign-key and malformed-value violations are the caller's mistake; every other database fault propagates.</summary>
    private static bool IsRefusal(PostgresException ex) => ex.SqlState is PostgresErrorCodes.UniqueViolation
        or PostgresErrorCodes.CheckViolation
        or PostgresErrorCodes.ForeignKeyViolation
        or PostgresErrorCodes.InvalidTextRepresentation
        or PostgresErrorCodes.InvalidDatetimeFormat;

    private Result<T> Refused<T>(PostgresException ex, string message)
    {
        LogRefused(ex.SqlState, ex.ConstraintName);
        return ex.SqlState switch
        {
            PostgresErrorCodes.UniqueViolation => Result<T>.Failure(message, VoiceFailureCodes.Conflict),
            PostgresErrorCodes.ForeignKeyViolation => Result<T>.Failure("The tenant was not found.", VoiceFailureCodes.NotFound),
            _ => Result<T>.Failure(message, VoiceFailureCodes.InvalidRequest),
        };
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "geek_voice refused a write: sqlstate {SqlState}, constraint {Constraint}.")]
    private partial void LogRefused(string? sqlState, string? constraint);

    // ----- Validation -----

    private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);

    private static string? ValidateIndustryAnswers(VoiceIndustryAnswerSet? set)
    {
        if (set is null || set.Answers is not { Count: > 0 }) return "The excluded-industry answers are required.";
        if (set.Answers.Any(a => a is null || Blank(a.Industry) || Blank(a.Question))) return "Every excluded-industry answer names its industry and its question.";
        if (set.Answers.Select(a => a.Industry).Distinct(StringComparer.Ordinal).Count() != set.Answers.Count) return "An excluded industry is answered once.";
        if (Blank(set.IpAddress) || !System.Net.IPAddress.TryParse(set.IpAddress, out _)) return "The visitor's address is required with the excluded-industry answers.";
        if (Blank(set.VisitorCountry)) return "The visitor's country is required with the excluded-industry answers.";
        return null;
    }

    private static string? ValidateCreateTenant(CreateVoiceTenantCommand c)
    {
        var answers = ValidateIndustryAnswers(c.IndustryAnswers);
        if (answers is not null) return answers;
        if (c.IndustryAnswers.Answers.Any(a => a.Answer)) return "A business that answered yes to an excluded industry cannot be a tenant.";
        if (Blank(c.BusinessName)) return "A business name is required.";
        if (Blank(c.IndustryVertical)) return "An industry is required.";
        if (!string.Equals(c.CountryCode, "US", StringComparison.Ordinal)) return "Only US businesses can sign up.";
        if (c.StateCode is null || c.StateCode.Length != 2) return "The state must be a two-letter code.";
        if (Blank(c.ContactEmail) || !c.ContactEmail.Contains('@', StringComparison.Ordinal)) return "A contact email is required.";
        if (Blank(c.ContactPhone) || !E164().IsMatch(c.ContactPhone)) return "The contact phone must be in E.164 form, such as +15615550101.";
        return null;
    }

    private static string? ValidateActivation(ActivateVoiceTenantCommand c)
    {
        if (!VoiceSubscriptionTiers.All.Contains(c.TierCode, StringComparer.Ordinal)) return $"The tier must be one of: {string.Join(", ", VoiceSubscriptionTiers.All)}.";
        if (c.MonthlyPriceUsd < 0 || c.IncludedMinutes < 0 || c.OverageRateUsd < 0) return "Price, included minutes and overage rate cannot be negative.";
        if (Blank(c.PaypalSubscriptionId) || Blank(c.PaypalVaultTokenId)) return "The PayPal subscription and vault token are required.";
        if (c.CurrentPeriodEnd <= c.CurrentPeriodStart) return "The billing period must end after it starts.";
        if (Blank(c.BillingStatus)) return "A billing status is required.";
        if (Blank(c.TwilioSubaccountSid) || Blank(c.TwilioAuthTokenEncrypted) || Blank(c.TwilioPhoneSid)) return "The Twilio subaccount, encrypted token and phone SID are required.";
        if (Blank(c.AssignedPhoneNumber) || !E164().IsMatch(c.AssignedPhoneNumber)) return "The assigned number must be in E.164 form.";
        if (Blank(c.A2pStatus)) return "An A2P status is required.";
        return null;
    }

    private static string? ValidateConfig(VoiceReceptionistConfig c)
    {
        if (Blank(c.GreetingMessage) || Blank(c.SystemPromptTemplate)) return "The greeting and prompt are required.";
        if (!IsJson(c.BusinessHours) || !IsJson(c.QualifyingQuestions)) return "Business hours and qualifying questions are required.";
        if (!IsQualifyingQuestions(c.QualifyingQuestions)) return "Qualifying questions must be a list, or an opening question with areas that each have a name and non-blank questions.";
        if (!VoiceTransferModes.All.Contains(c.TransferMode, StringComparer.Ordinal)) return $"The transfer mode must be one of: {string.Join(", ", VoiceTransferModes.All)}.";
        if (!VoiceTransferCallerIdModes.All.Contains(c.TransferCallerIdMode, StringComparer.Ordinal)) return $"The caller ID mode must be one of: {string.Join(", ", VoiceTransferCallerIdModes.All)}.";
        if (c.TransferTargetNumber is not null && !E164().IsMatch(c.TransferTargetNumber)) return "The transfer number must be in E.164 form.";
        if (c.EmergencyKeywords is null || c.EmergencyKeywords.Any(Blank)) return "Emergency keywords cannot be blank.";
        if (c.MessageEmailEnabled && !IsEmailAddress(c.MessageDestinationEmail)) return "Message email needs a valid destination email address.";
        if (!VoiceConsentModes.All.Contains(c.ConsentMode, StringComparer.Ordinal)) return $"The consent mode must be one of: {string.Join(", ", VoiceConsentModes.All)}.";
        return null;
    }

    private static bool IsEmailAddress(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && System.Net.Mail.MailAddress.TryCreate(value, out var address)
        && string.Equals(address.Address, value, StringComparison.Ordinal);


    private static string? ValidateMcpConnection(string provider, SaveVoiceMcpConnectionCommand? c)
    {
        if (!VoiceMcpProviders.All.Contains(provider, StringComparer.Ordinal)) return $"The provider must be one of: {string.Join(", ", VoiceMcpProviders.All)}.";
        if (c is null) return "A connection is required.";
        if (Blank(c.OauthAccessTokenEncrypted) || Blank(c.OauthRefreshTokenEncrypted)) return "Both the access token and the refresh token are required.";
        return null;
    }

    private static string? ValidateStorageConnection(SaveVoiceStorageConnectionCommand? c)
    {
        if (c is null) return "A storage connection is required.";
        if (!VoiceStorageProviders.All.Contains(c.Provider, StringComparer.Ordinal))
            return $"The provider must be one of: {string.Join(", ", VoiceStorageProviders.All)}.";
        if (Blank(c.AccessKeyIdEncrypted) || Blank(c.SecretAccessKeyEncrypted)) return "Both the access key and the secret are required.";
        if (Blank(c.Bucket)) return "A bucket is required.";
        if (Blank(c.Region)) return "A region is required.";
        if (c.KeyPrefix is null) return "A key prefix is required, and may be empty.";
        if (c.KeyPrefix.StartsWith('/')) return "A key prefix does not begin with a slash.";
        return null;
    }


    private static string? ValidateCall(UpsertVoiceCallCommand c)
    {
        if (Blank(c.TwilioCallSid) || Blank(c.CallStatus)) return "The Twilio call SID and status are required.";
        if (c.DurationSeconds < 0 || c.BillableMinutes < 0) return "Duration and billable minutes cannot be negative.";
        if (c.EndedAt is not null && c.EndedAt < c.StartedAt) return "A call cannot end before it starts.";
        return null;
    }

    private static string? ValidateAppointment(CreateVoiceAppointmentCommand c)
    {
        if (c.ScheduledEnd <= c.ScheduledStart) return "An appointment must end after it starts.";
        if (!VoiceMcpProviders.Calendars.Contains(c.McpProvider, StringComparer.Ordinal)) return $"The calendar provider must be one of: {string.Join(", ", VoiceMcpProviders.Calendars)}.";
        return null;
    }

    private static bool IsJson(JsonElement value) => value.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null);

    /// <summary>
    /// A list (a paying tenant's starts empty and is its own to fill), or the area set Geek @ Your Spot LLC's own receptionist uses:
    /// {"openingQuestion": "...", "areas": [{"name": "...", "questions": ["..."]}]}, every text non-blank.
    /// </summary>
    private static bool IsQualifyingQuestions(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array) return true;
        if (value.ValueKind != JsonValueKind.Object) return false;
        if (!value.TryGetProperty("openingQuestion", out var opening) || !IsText(opening)) return false;
        if (!value.TryGetProperty("areas", out var areas) || areas.ValueKind != JsonValueKind.Array || areas.GetArrayLength() == 0) return false;
        return areas.EnumerateArray().All(area =>
            area.ValueKind == JsonValueKind.Object
            && area.TryGetProperty("name", out var name) && IsText(name)
            && area.TryGetProperty("questions", out var questions) && questions.ValueKind == JsonValueKind.Array
            && questions.GetArrayLength() > 0 && questions.EnumerateArray().All(IsText));
    }

    private static bool IsText(JsonElement value) => value.ValueKind == JsonValueKind.String && !Blank(value.GetString());

    private static string VectorLiteral(IReadOnlyList<float> embedding) =>
        "[" + string.Join(",", embedding.Select(v => v.ToString("R", CultureInfo.InvariantCulture))) + "]";

    // ----- Rows -----
    // Dapper fills these by column alias. Times arrive as UTC DateTime; the public records use DateTimeOffset.

    private static JsonElement ParseJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private sealed class TenantRow
    {
        public Guid Id { get; set; }
        public string BusinessName { get; set; } = "";
        public string IndustryVertical { get; set; } = "";
        public DateTime ComplianceAttestedAt { get; set; }
        public string CountryCode { get; set; } = "";
        public string StateCode { get; set; } = "";
        public string ContactEmail { get; set; } = "";
        public string ContactPhone { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTime CreatedAt { get; set; }

        public VoiceTenant ToModel() => new(
            Id, BusinessName, IndustryVertical, Utc(ComplianceAttestedAt), CountryCode, StateCode, ContactEmail, ContactPhone, Status, Utc(CreatedAt));
    }

    private sealed class TelephonyRow
    {
        public Guid TenantId { get; set; }
        public string TwilioSubaccountSid { get; set; } = "";
        public string TwilioAuthTokenEncrypted { get; set; } = "";
        public string AssignedPhoneNumber { get; set; } = "";
        public string TwilioPhoneSid { get; set; } = "";
        public string? A2pBrandSid { get; set; }
        public string? A2pCampaignSid { get; set; }
        public string A2pStatus { get; set; } = "";
        public bool SmsNotificationsEnabled { get; set; }

        public VoiceTelephony ToModel() => new(
            TenantId, TwilioSubaccountSid, TwilioAuthTokenEncrypted, AssignedPhoneNumber, TwilioPhoneSid,
            A2pBrandSid, A2pCampaignSid, A2pStatus, SmsNotificationsEnabled);
    }

    private sealed class ConfigRow
    {
        public string GreetingMessage { get; set; } = "";
        public string SystemPromptTemplate { get; set; } = "";
        public string BusinessHours { get; set; } = "";
        public string QualifyingQuestions { get; set; } = "";
        public string TransferMode { get; set; } = "";
        public string? TransferTargetNumber { get; set; }
        public string TransferCallerIdMode { get; set; } = "";
        public string[] EmergencyKeywords { get; set; } = [];
        public bool RecordingDisclosureEnabled { get; set; }
        public bool MessageEmailEnabled { get; set; }
        public string? MessageDestinationEmail { get; set; }
        public bool IncludeTranscript { get; set; }
        public bool NotifyOnAbandonedCalls { get; set; }
        public bool BookingEnabled { get; set; }
        public string ConsentMode { get; set; } = VoiceConsentModes.PassiveDisclosure;
        public Guid? KnowledgeRunId { get; set; }

        public VoiceReceptionistConfig ToModel() => new(
            GreetingMessage, SystemPromptTemplate, ParseJson(BusinessHours),
            ParseJson(QualifyingQuestions), TransferMode, TransferTargetNumber, TransferCallerIdMode,
            EmergencyKeywords, RecordingDisclosureEnabled,
            MessageEmailEnabled, MessageDestinationEmail, IncludeTranscript, NotifyOnAbandonedCalls, BookingEnabled, ConsentMode,
            KnowledgeRunId);
    }

    private sealed class McpRow
    {
        public Guid TenantId { get; set; }
        public string Provider { get; set; } = "";
        public string OauthAccessTokenEncrypted { get; set; } = "";
        public string OauthRefreshTokenEncrypted { get; set; } = "";
        public DateTime? TokenExpiresAt { get; set; }
        public string? ExternalCalendarId { get; set; }
        public bool IsActive { get; set; }

        public VoiceMcpConnection ToModel() => new(
            TenantId, Provider, OauthAccessTokenEncrypted, OauthRefreshTokenEncrypted,
            TokenExpiresAt is null ? null : Utc(TokenExpiresAt.Value), ExternalCalendarId, IsActive);
    }

}
