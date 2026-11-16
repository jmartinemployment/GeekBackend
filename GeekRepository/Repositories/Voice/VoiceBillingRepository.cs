using Dapper;
using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;
using GeekRepository.Infrastructure;
using Npgsql;

using static GeekRepository.Repositories.Voice.VoiceResults;

namespace GeekRepository.Repositories.Voice;

/// <summary>
/// End-of-cycle billing over geek_voice (docs/Geek-Receptionist.md section 5, item 2). Closing a period, settling its charge and moving
/// the period on each lock the subscription row (<c>FOR UPDATE</c>) in one transaction, so two GeekAPI instances cannot close the same
/// period twice or move it on while it is being closed. Minutes are counted by when a call started: a call that started before the
/// period ended belongs to it, and the next period starts exactly where this one ended. Each call runs once; a database fault that is
/// not a refusal below is raised, not retried, not defaulted.
/// </summary>
public sealed class VoiceBillingRepository(IDbConnectionFactory db) : IVoiceBillingStore
{
    /// <summary>The most subscriptions one sweep is handed; the rest are found by the next sweep.</summary>
    public const int DueBatch = 100;

    private const string ChargeSelect = """
        SELECT
            c.id AS Id, c.tenant_id AS TenantId, c.subscription_id AS SubscriptionId,
            s.paypal_subscription_id AS PaypalSubscriptionId, s.paypal_vault_token_id AS PaypalVaultTokenId,
            c.period_start AS PeriodStart, c.period_end AS PeriodEnd, c.included_minutes AS IncludedMinutes,
            c.used_minutes AS UsedMinutes, c.overage_minutes AS OverageMinutes, c.overage_rate_usd AS OverageRateUsd,
            c.amount_usd AS AmountUsd, c.status AS Status, c.paypal_order_id AS PaypalOrderId,
            c.paypal_capture_id AS PaypalCaptureId, c.failure AS Failure, c.created_at AS CreatedAt, c.settled_at AS SettledAt
        FROM geek_voice.overage_charges c
        JOIN geek_voice.tenant_subscriptions s ON s.id = c.subscription_id AND s.tenant_id = c.tenant_id
        """;

    public async Task<Result<IReadOnlyList<VoiceSubscriptionDue>>> ListDueAsync(ListVoiceSubscriptionsDueCommand command, CancellationToken ct = default)
    {
        if (command.EndedBy == default) return Invalid<IReadOnlyList<VoiceSubscriptionDue>>("A cut-off time is required.");

        await using var conn = (NpgsqlConnection)db.CreateConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // Local to this transaction: the setting ends with it and never travels with the pooled connection.
        await conn.ExecuteAsync(new CommandDefinition(
            "SELECT set_config('app.billing_sweep', 'on', true);", transaction: tx, cancellationToken: ct));
        var rows = (await conn.QueryAsync<DueRow>(new CommandDefinition("""
            SELECT tenant_id AS TenantId, id AS SubscriptionId, current_period_end AS CurrentPeriodEnd
            FROM geek_voice.tenant_subscriptions
            WHERE billing_status = @Active AND current_period_end <= @EndedBy
            ORDER BY current_period_end
            LIMIT @Limit;
            """,
            new { Active = VoiceBillingStatuses.Active, EndedBy = command.EndedBy.UtcDateTime, Limit = DueBatch },
            transaction: tx,
            cancellationToken: ct))).Select(r => new VoiceSubscriptionDue(r.TenantId, r.SubscriptionId, Utc(r.CurrentPeriodEnd))).ToList();
        await tx.CommitAsync(ct);

        return Result<IReadOnlyList<VoiceSubscriptionDue>>.Success(rows);
    }

    public async Task<Result<VoiceBillingPeriodClosed>> CloseBillingPeriodAsync(Guid tenantId, CloseVoiceBillingPeriodCommand command, CancellationToken ct = default)
    {
        if (command.SubscriptionId == Guid.Empty || command.PeriodEnd == default)
            return Invalid<VoiceBillingPeriodClosed>("A subscription and the end of its period are required.");

        await using var conn = await VoiceConnections.OpenForTenantAsync(db, tenantId, ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var subscription = await LockSubscriptionAsync(conn, tx, tenantId, command.SubscriptionId, ct);
        if (subscription is null)
        {
            await tx.RollbackAsync(ct);
            return NotFound<VoiceBillingPeriodClosed>("The subscription was not found.");
        }

        var periodStart = Utc(subscription.CurrentPeriodStart);
        var periodEnd = Utc(subscription.CurrentPeriodEnd);
        if (periodEnd != command.PeriodEnd)
        {
            await tx.RollbackAsync(ct);
            return Result<VoiceBillingPeriodClosed>.Failure("The billing period has already moved on.", VoiceFailureCodes.Conflict);
        }

        var existing = await conn.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
            "SELECT id FROM geek_voice.overage_charges WHERE tenant_id = @TenantId AND subscription_id = @SubscriptionId AND period_start = @PeriodStart;",
            new { TenantId = tenantId, command.SubscriptionId, PeriodStart = periodStart.UtcDateTime },
            transaction: tx,
            cancellationToken: ct));
        if (existing is not null)
        {
            var held = await ReadChargeAsync(conn, tx, tenantId, existing.Value, ct);
            await tx.CommitAsync(ct);
            return Result<VoiceBillingPeriodClosed>.Success(new VoiceBillingPeriodClosed(held!, Created: false));
        }

        var usedMinutes = await conn.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COALESCE(SUM(billable_minutes), 0)::int
            FROM geek_voice.call_logs
            WHERE tenant_id = @TenantId AND started_at >= @PeriodStart AND started_at < @PeriodEnd;
            """,
            new { TenantId = tenantId, PeriodStart = periodStart.UtcDateTime, PeriodEnd = periodEnd.UtcDateTime },
            transaction: tx,
            cancellationToken: ct));

        var overageMinutes = Math.Max(0, usedMinutes - subscription.IncludedMinutes);
        var amount = overageMinutes * subscription.OverageRateUsd;
        var status = overageMinutes == 0 ? VoiceOverageStatuses.NoOverage : VoiceOverageStatuses.Charging;

        var chargeId = await conn.QuerySingleAsync<Guid>(new CommandDefinition("""
            INSERT INTO geek_voice.overage_charges (
                tenant_id, subscription_id, period_start, period_end, included_minutes, used_minutes, overage_minutes,
                overage_rate_usd, amount_usd, status, settled_at)
            VALUES (
                @TenantId, @SubscriptionId, @PeriodStart, @PeriodEnd, @IncludedMinutes, @UsedMinutes, @OverageMinutes,
                @OverageRateUsd, @AmountUsd, @Status, CASE WHEN @Status = 'charging' THEN NULL ELSE NOW() END)
            RETURNING id;
            """,
            new
            {
                TenantId = tenantId,
                command.SubscriptionId,
                PeriodStart = periodStart.UtcDateTime,
                PeriodEnd = periodEnd.UtcDateTime,
                subscription.IncludedMinutes,
                UsedMinutes = usedMinutes,
                OverageMinutes = overageMinutes,
                subscription.OverageRateUsd,
                AmountUsd = amount,
                Status = status,
            },
            transaction: tx,
            cancellationToken: ct));

        var charge = await ReadChargeAsync(conn, tx, tenantId, chargeId, ct);
        await tx.CommitAsync(ct);
        return Result<VoiceBillingPeriodClosed>.Success(new VoiceBillingPeriodClosed(charge!, Created: true));
    }

    public async Task<Result<VoiceOverageCharge>> SettleOverageChargeAsync(Guid tenantId, Guid chargeId, SettleVoiceOverageChargeCommand command, CancellationToken ct = default)
    {
        var problem = ValidateSettlement(command);
        if (problem is not null) return Invalid<VoiceOverageCharge>(problem);

        await using var conn = await VoiceConnections.OpenForTenantAsync(db, tenantId, ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var updated = await conn.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition("""
            UPDATE geek_voice.overage_charges
            SET status = @Status, paypal_order_id = @PaypalOrderId, paypal_capture_id = @PaypalCaptureId, failure = @Failure, settled_at = NOW()
            WHERE id = @ChargeId AND tenant_id = @TenantId AND status = 'charging'
            RETURNING id;
            """,
            new { ChargeId = chargeId, TenantId = tenantId, command.Status, command.PaypalOrderId, command.PaypalCaptureId, command.Failure },
            transaction: tx,
            cancellationToken: ct));

        var charge = await ReadChargeAsync(conn, tx, tenantId, chargeId, ct);
        await tx.CommitAsync(ct);

        if (charge is null) return NotFound<VoiceOverageCharge>("The charge was not found.");
        return updated is null
            ? Result<VoiceOverageCharge>.Failure("The charge has already been settled.", VoiceFailureCodes.Conflict)
            : Result<VoiceOverageCharge>.Success(charge);
    }

    public async Task<Result<VoiceBillingPeriod>> AdvanceBillingPeriodAsync(Guid tenantId, AdvanceVoiceBillingPeriodCommand command, CancellationToken ct = default)
    {
        if (command.SubscriptionId == Guid.Empty || command.FromPeriodEnd == default)
            return Invalid<VoiceBillingPeriod>("A subscription and the end of its period are required.");
        if (command.NewPeriodEnd <= command.FromPeriodEnd)
            return Invalid<VoiceBillingPeriod>("The next period must end after the current one.");

        await using var conn = await VoiceConnections.OpenForTenantAsync(db, tenantId, ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var subscription = await LockSubscriptionAsync(conn, tx, tenantId, command.SubscriptionId, ct);
        if (subscription is null)
        {
            await tx.RollbackAsync(ct);
            return NotFound<VoiceBillingPeriod>("The subscription was not found.");
        }

        if (Utc(subscription.CurrentPeriodEnd) != command.FromPeriodEnd)
        {
            await tx.RollbackAsync(ct);
            return Result<VoiceBillingPeriod>.Failure("The billing period has already moved on.", VoiceFailureCodes.Conflict);
        }

        var chargeStatus = await conn.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
            "SELECT status FROM geek_voice.overage_charges WHERE tenant_id = @TenantId AND subscription_id = @SubscriptionId AND period_start = @PeriodStart;",
            new { TenantId = tenantId, command.SubscriptionId, PeriodStart = Utc(subscription.CurrentPeriodStart).UtcDateTime },
            transaction: tx,
            cancellationToken: ct));
        if (chargeStatus is null || chargeStatus == VoiceOverageStatuses.Charging)
        {
            await tx.RollbackAsync(ct);
            return Result<VoiceBillingPeriod>.Failure(
                chargeStatus is null ? "The period has not been closed." : "The period's overage charge has no outcome yet.",
                VoiceFailureCodes.Conflict);
        }

        var period = await conn.QuerySingleAsync<PeriodRow>(new CommandDefinition("""
            UPDATE geek_voice.tenant_subscriptions
            SET current_period_start = current_period_end, current_period_end = @NewPeriodEnd
            WHERE id = @SubscriptionId AND tenant_id = @TenantId
            RETURNING id AS SubscriptionId, current_period_start AS PeriodStart, current_period_end AS PeriodEnd;
            """,
            new { TenantId = tenantId, command.SubscriptionId, NewPeriodEnd = command.NewPeriodEnd.UtcDateTime },
            transaction: tx,
            cancellationToken: ct));
        await tx.CommitAsync(ct);

        return Result<VoiceBillingPeriod>.Success(new VoiceBillingPeriod(period.SubscriptionId, Utc(period.PeriodStart), Utc(period.PeriodEnd)));
    }

    public async Task<Result<bool>> SetBillingStatusAsync(Guid tenantId, SetVoiceBillingStatusCommand command, CancellationToken ct = default)
    {
        if (command.SubscriptionId == Guid.Empty || string.IsNullOrWhiteSpace(command.BillingStatus))
            return Invalid<bool>("A subscription and a billing status are required.");

        await using var conn = await VoiceConnections.OpenForTenantAsync(db, tenantId, ct);
        var updated = await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE geek_voice.tenant_subscriptions SET billing_status = @BillingStatus WHERE id = @SubscriptionId AND tenant_id = @TenantId;",
            new { TenantId = tenantId, command.SubscriptionId, command.BillingStatus },
            cancellationToken: ct));
        return updated == 0 ? NotFound<bool>("The subscription was not found.") : Result<bool>.Success(true);
    }

    private static string? ValidateSettlement(SettleVoiceOverageChargeCommand c)
    {
        if (c.Status == VoiceOverageStatuses.Charged)
        {
            if (string.IsNullOrWhiteSpace(c.PaypalOrderId) || string.IsNullOrWhiteSpace(c.PaypalCaptureId))
                return "A charge that went through carries PayPal's order and capture ids.";
            return c.Failure is null ? null : "A charge that went through has no failure.";
        }

        if (c.Status == VoiceOverageStatuses.Failed)
            return string.IsNullOrWhiteSpace(c.Failure) ? "A failed charge carries its reason." : null;

        return $"A charge is settled as {VoiceOverageStatuses.Charged} or {VoiceOverageStatuses.Failed}.";
    }

    private static Task<SubscriptionRow?> LockSubscriptionAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid tenantId, Guid subscriptionId, CancellationToken ct) =>
        conn.QuerySingleOrDefaultAsync<SubscriptionRow>(new CommandDefinition("""
            SELECT included_minutes AS IncludedMinutes, overage_rate_usd AS OverageRateUsd,
                   current_period_start AS CurrentPeriodStart, current_period_end AS CurrentPeriodEnd
            FROM geek_voice.tenant_subscriptions
            WHERE id = @SubscriptionId AND tenant_id = @TenantId
            FOR UPDATE;
            """,
            new { TenantId = tenantId, SubscriptionId = subscriptionId },
            transaction: tx,
            cancellationToken: ct));

    private static async Task<VoiceOverageCharge?> ReadChargeAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid tenantId, Guid chargeId, CancellationToken ct)
    {
        var row = await conn.QuerySingleOrDefaultAsync<ChargeRow>(new CommandDefinition(
            ChargeSelect + " WHERE c.id = @ChargeId AND c.tenant_id = @TenantId;",
            new { ChargeId = chargeId, TenantId = tenantId },
            transaction: tx,
            cancellationToken: ct));
        return row?.ToModel();
    }

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    // ----- Rows -----

    private sealed class DueRow
    {
        public Guid TenantId { get; set; }
        public Guid SubscriptionId { get; set; }
        public DateTime CurrentPeriodEnd { get; set; }
    }

    private sealed class SubscriptionRow
    {
        public int IncludedMinutes { get; set; }
        public decimal OverageRateUsd { get; set; }
        public DateTime CurrentPeriodStart { get; set; }
        public DateTime CurrentPeriodEnd { get; set; }
    }

    private sealed class PeriodRow
    {
        public Guid SubscriptionId { get; set; }
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
    }

    private sealed class ChargeRow
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid SubscriptionId { get; set; }
        public string PaypalSubscriptionId { get; set; } = "";
        public string PaypalVaultTokenId { get; set; } = "";
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        public int IncludedMinutes { get; set; }
        public int UsedMinutes { get; set; }
        public int OverageMinutes { get; set; }
        public decimal OverageRateUsd { get; set; }
        public decimal AmountUsd { get; set; }
        public string Status { get; set; } = "";
        public string? PaypalOrderId { get; set; }
        public string? PaypalCaptureId { get; set; }
        public string? Failure { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? SettledAt { get; set; }

        public VoiceOverageCharge ToModel() => new(
            Id, TenantId, SubscriptionId, PaypalSubscriptionId, PaypalVaultTokenId, Utc(PeriodStart), Utc(PeriodEnd), IncludedMinutes,
            UsedMinutes, OverageMinutes, OverageRateUsd, AmountUsd, Status, PaypalOrderId, PaypalCaptureId, Failure, Utc(CreatedAt),
            SettledAt is null ? null : Utc(SettledAt.Value));
    }
}
