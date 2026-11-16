using System.Data;
using GeekApplication.Models.Voice;
using GeekApplication.Results;
using GeekRepository.Infrastructure;
using GeekRepository.Repositories.Voice;

namespace GeekBackend.Tests.Voice;

/// <summary>
/// The refusals the billing store makes before it touches the database (a connection factory that throws proves none is opened). What
/// the database enforces (one close per period, the row lock, the settled-before-advance rule, the sweep policy) needs a real Postgres
/// and is not run here.
/// </summary>
public sealed class VoiceBillingStoreTests
{
    private sealed class NoDatabase : IDbConnectionFactory
    {
        public IDbConnection CreateConnection() => throw new InvalidOperationException("This test must be refused before it asks for a connection.");
    }

    private static readonly Guid Tenant = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid Subscription = Guid.Parse("88888888-8888-8888-8888-888888888888");
    private static readonly DateTimeOffset End = new(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);
    private static VoiceBillingRepository Store() => new(new NoDatabase());

    private static async Task AssertInvalid<T>(Task<Result<T>> call)
    {
        var result = await call;
        Assert.False(result.IsSuccess);
        Assert.Equal(VoiceFailureCodes.InvalidRequest, result.ErrorCode);
    }

    [Fact]
    public async Task The_due_list_needs_a_cut_off() =>
        await AssertInvalid(Store().ListDueAsync(new ListVoiceSubscriptionsDueCommand(default)));

    [Fact]
    public async Task Closing_needs_a_subscription_and_its_period_end()
    {
        await AssertInvalid(Store().CloseBillingPeriodAsync(Tenant, new CloseVoiceBillingPeriodCommand(Guid.Empty, End)));
        await AssertInvalid(Store().CloseBillingPeriodAsync(Tenant, new CloseVoiceBillingPeriodCommand(Subscription, default)));
    }

    [Theory]
    [InlineData(VoiceOverageStatuses.Charged, null, "CAP", null)]
    [InlineData(VoiceOverageStatuses.Charged, "ORD", " ", null)]
    [InlineData(VoiceOverageStatuses.Charged, "ORD", "CAP", "but also failed")]
    [InlineData(VoiceOverageStatuses.Failed, null, null, " ")]
    [InlineData(VoiceOverageStatuses.Charging, null, null, null)]
    [InlineData(VoiceOverageStatuses.NoOverage, null, null, null)]
    [InlineData("paid", "ORD", "CAP", null)]
    public async Task A_charge_is_settled_only_as_charged_with_its_ids_or_failed_with_its_reason(string status, string? order, string? capture, string? failure) =>
        await AssertInvalid(Store().SettleOverageChargeAsync(Tenant, Guid.NewGuid(), new SettleVoiceOverageChargeCommand(status, order, capture, failure)));

    [Fact]
    public async Task The_next_period_must_end_after_the_current_one()
    {
        await AssertInvalid(Store().AdvanceBillingPeriodAsync(Tenant, new AdvanceVoiceBillingPeriodCommand(Subscription, End, End)));
        await AssertInvalid(Store().AdvanceBillingPeriodAsync(Tenant, new AdvanceVoiceBillingPeriodCommand(Subscription, End, End.AddDays(-1))));
        await AssertInvalid(Store().AdvanceBillingPeriodAsync(Tenant, new AdvanceVoiceBillingPeriodCommand(Guid.Empty, End, End.AddMonths(1))));
    }

    [Theory]
    [InlineData(" ", "CAP")]
    [InlineData("ORD", "")]
    public async Task An_owners_payment_carries_PayPals_order_and_capture(string order, string capture) =>
        await AssertInvalid(Store().CollectOverageChargeAsync(Tenant, Guid.NewGuid(), new CollectVoiceOverageChargeCommand(order, capture)));

    [Fact]
    public async Task A_billing_status_cannot_be_blank() =>
        await AssertInvalid(Store().SetBillingStatusAsync(Tenant, new SetVoiceBillingStatusCommand(Subscription, " ")));
}
