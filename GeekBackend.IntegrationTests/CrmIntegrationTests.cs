extern alias GeekRepo;

using GeekApplication.Models.Crm;
using GeekRepo::GeekRepository.Data;
using GeekRepo::GeekRepository.Repositories.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeekBackend.IntegrationTests;

/// <summary>
/// geek_crm against a real Postgres: the script applies, a caller's number finds the same person
/// every time, a Twilio retry records one activity, STOP sets the opt-out, an import matches instead
/// of duplicating, and a deal cannot pass Qualified without a primary contact. Runs only with
/// TEST_DATABASE_URL set.
/// </summary>
public sealed class CrmIntegrationTests
{
    private static readonly Guid Tenant = CrmSeed.GeekAtYourSpotTenantId;

    private static CrmRepository NewRepository()
    {
        var options = new DbContextOptionsBuilder<CrmDbContext>().UseNpgsql(IntegrationTestConfig.DatabaseUrl!).Options;
        var db = new CrmDbContext(options);
        return new CrmRepository(db, TimeProvider.System, NullLogger<CrmRepository>.Instance, new PostgresCrmTenantScope(db));
    }

    private static string RandomPhone() => "+1561" + Random.Shared.Next(2000000, 9999999).ToString(System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task A_caller_is_one_person_and_a_retried_webhook_is_one_activity()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured) return;
        await SqlMigrationTestHelper.ApplyAsync();
        var phone = RandomPhone();

        var first = await NewRepository().EnsureParticipantByPhoneAsync(new(Tenant, phone, ParticipantSources.PhoneCall, "test"));
        var second = await NewRepository().EnsureParticipantByPhoneAsync(new(Tenant, phone, ParticipantSources.PhoneCall, "test"));
        Assert.True(first.IsSuccess, first.Error);
        Assert.True(first.Value!.Created);
        Assert.False(second.Value!.Created);
        Assert.Equal(first.Value.ParticipantId, second.Value.ParticipantId);

        var sid = "CA" + Guid.NewGuid().ToString("N");
        RecordActivityCommand Missed() => new(Tenant, first.Value.ParticipantId, null, ActivityChannels.Call, ActivityDirections.Inbound,
            DateTime.UtcNow, null, null, null, null, null, ActivityOutcomes.Missed, sid, "receptionist");
        var a = await NewRepository().RecordActivityAsync(Missed());
        var b = await NewRepository().RecordActivityAsync(Missed());
        Assert.True(a.IsSuccess, a.Error);
        Assert.True(a.Value!.Created);
        Assert.False(b.Value!.Created);
        Assert.Equal(a.Value.ActivityId, b.Value.ActivityId);

        var optOut = await NewRepository().SetSmsOptOutAsync(new(Tenant, phone, true));
        Assert.True(optOut.Value);

        var participant = await NewRepository().GetParticipantAsync(Tenant, first.Value.ParticipantId);
        Assert.True(participant.IsSuccess, participant.Error);
        Assert.True(Assert.Single(participant.Value!.ContactPoints).SmsOptOut);
        Assert.Single(participant.Value.RecentActivities);
        Assert.Equal("(561) " + phone[5..8] + "-" + phone[8..], participant.Value.DisplayName);
    }

    [Fact]
    public async Task An_import_links_people_to_companies_and_a_second_import_matches_instead_of_duplicating()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured) return;
        await SqlMigrationTestHelper.ApplyAsync();
        var unique = Guid.NewGuid().ToString("N")[..8];
        var prospects = new[]
        {
            new ImportProspect("Maria", "Lopez", "Owner", $"maria-{unique}@example.com", [new ImportPhone(RandomPhone(), "mobile")],
                "Boca Raton", "Florida", "United States", null, $"Boca Dental {unique}", $"https://www.bocadental-{unique}.com",
                RandomPhone(), "1 Main St", "Boca Raton", "Florida", "33431", "United States", "dental", "621210, 621999", "8021", 8, "1m_5m",
                "Owner", false, $"apollo-{unique}"),
        };

        var first = await NewRepository().ImportProspectsAsync(new(Tenant, "test import", prospects));
        Assert.True(first.IsSuccess, first.Error);
        Assert.Equal((1, 1, 0), (first.Value!.PeopleCreated, first.Value.OrganizationsCreated, first.Value.Matched));

        var again = await NewRepository().ImportProspectsAsync(new(Tenant, "test import", prospects));
        Assert.Equal((0, 0, 2), (again.Value!.PeopleCreated, again.Value.OrganizationsCreated, again.Value.Matched));

        var orgs = await NewRepository().ListParticipantsAsync(Tenant, LifecycleStages.Lead, ParticipantKinds.Organization, 200);
        var org = Assert.Single(orgs.Value!, o => o.DisplayName == $"Boca Dental {unique}");
        var detail = await NewRepository().GetParticipantAsync(Tenant, org.Id);
        Assert.Equal("621210", detail.Value!.Organization!.NaicsCode);
        var link = Assert.Single(detail.Value.Links);
        Assert.Equal((ParticipantLinkRoles.Owner, true, "Maria Lopez"), (link.Role, link.IsPrimary, link.OtherDisplayName));
    }

    [Fact]
    public async Task A_deal_needs_a_primary_contact_to_pass_qualified()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured) return;
        await SqlMigrationTestHelper.ApplyAsync();

        var deal = await NewRepository().CreateDealAsync(new(Tenant, "Test deal", "missed_call_text_back", null, null, """{"missedCallsPerWeek":"10"}""", 297m, 49m, null));
        Assert.True(deal.IsSuccess, deal.Error);
        Assert.Equal("New", deal.Value!.StageName);

        var refused = await NewRepository().MoveDealStageAsync(deal.Value.Id, new(Tenant, "Meeting booked", null, "test"));
        Assert.Equal(CrmFailureCodes.InvalidRequest, refused.ErrorCode);

        var lost = await NewRepository().MoveDealStageAsync(deal.Value.Id, new(Tenant, "Lost", "no budget", "test"));
        Assert.True(lost.IsSuccess, lost.Error);
        Assert.Equal((StageOutcomes.Lost, "no budget"), (lost.Value!.StageOutcome, lost.Value.LostReason));
        Assert.NotNull(lost.Value.ClosedAt);
    }

    [Fact]
    public async Task A_read_that_sets_no_tenant_sees_no_rows()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured) return;
        await SqlMigrationTestHelper.ApplyAsync();

        // The seeded tenant has a pipeline, so a table that returns rows to a connection with no
        // tenant set is a boundary that row-level security is not enforcing (a superuser or BYPASSRLS role).
        await using var conn = new Npgsql.NpgsqlConnection(IntegrationTestConfig.DatabaseUrl!);
        await conn.OpenAsync();
        await using var role = new Npgsql.NpgsqlCommand("SELECT rolsuper OR rolbypassrls FROM pg_roles WHERE rolname = current_user", conn);
        Assert.False((bool)(await role.ExecuteScalarAsync())!, "The test role bypasses row-level security, so this check proves nothing.");

        await using var count = new Npgsql.NpgsqlCommand("SELECT count(*) FROM geek_crm.pipelines", conn);
        Assert.Equal(0L, (long)(await count.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task One_tenant_cannot_read_or_move_another_tenants_deal()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured) return;
        await SqlMigrationTestHelper.ApplyAsync();

        var deal = await NewRepository().CreateDealAsync(new(Tenant, "Isolation deal", "missed_call_text_back", null, null, null, null, null, null));
        Assert.True(deal.IsSuccess, deal.Error);

        var otherTenant = Guid.NewGuid();
        var moved = await NewRepository().MoveDealStageAsync(deal.Value!.Id, new(otherTenant, "Lost", "no budget", "test"));
        Assert.False(moved.IsSuccess);

        var listed = await NewRepository().ListDealsAsync(otherTenant, false, 200);
        Assert.DoesNotContain(listed.Value ?? [], d => d.Id == deal.Value.Id);
    }

    // ----- Phase 2: the catalog, line items and winning a deal into holdings -----

    /// <summary>Runs the work and then fails before the commit, as a crash partway through a win would: the transaction must roll back.</summary>
    private sealed class FailAfterWorkScope(ICrmTenantScope inner) : ICrmTenantScope
    {
        public Task<T> RunPlatformAsync<T>(Guid sellerTenantId, Func<Task<T>> work, Func<T, bool> keep, CancellationToken ct) =>
            inner.RunPlatformAsync(sellerTenantId, work, keep, ct);

        public Task<T> RunAsync<T>(Guid tenantId, Func<Task<T>> work, Func<T, bool> keep, CancellationToken ct) =>
            inner.RunAsync(tenantId, async () =>
            {
                await work();
                throw new InvalidOperationException("The win failed after it wrote its holdings.");
            }, keep, ct);
    }

    private static async Task<(Guid DealId, Guid ContactId, int Items)> DealWithLineItemsAsync(int items)
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var contact = await NewRepository().EnsureParticipantByPhoneAsync(new(Tenant, RandomPhone(), ParticipantSources.PhoneCall, "test"));
        Assert.True(contact.IsSuccess, contact.Error);
        var deal = await NewRepository().CreateDealAsync(new(Tenant, "Win test " + unique, "missed_call_text_back", null, contact.Value!.ParticipantId, null, null, null, null));
        Assert.True(deal.IsSuccess, deal.Error);
        for (var i = 0; i < items; i++)
        {
            var product = await NewRepository().CreateProductAsync(new(Tenant, $"Product {i} {unique}", $"p{i}-{unique}", ProductKinds.Service, ProductCategories.Accounting, null, null, i % 2 == 0 ? BillingPeriods.Monthly : BillingPeriods.OneTime));
            Assert.True(product.IsSuccess, product.Error);
            var added = await NewRepository().AddLineItemAsync(deal.Value!.Id, new(Tenant, product.Value!.Id, 1, 100m + i, null, 0));
            Assert.True(added.IsSuccess, added.Error);
        }
        return (deal.Value!.Id, contact.Value.ParticipantId, items);
    }

    [Fact]
    public async Task Winning_a_deal_makes_one_holding_per_line_item_and_a_second_win_makes_none()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured) return;
        await SqlMigrationTestHelper.ApplyAsync();
        var (dealId, contactId, n) = await DealWithLineItemsAsync(3);

        var first = await NewRepository().WinDealAsync(Tenant, dealId, "test");
        Assert.True(first.IsSuccess, first.Error);
        Assert.Equal((n, n), (first.Value!.HoldingsCreated, first.Value.Holdings.Count));
        Assert.Equal(StageOutcomes.Won, first.Value.Deal.StageOutcome);

        var second = await NewRepository().WinDealAsync(Tenant, dealId, "test");
        Assert.True(second.IsSuccess, second.Error);
        Assert.Equal((0, n), (second.Value!.HoldingsCreated, second.Value.Holdings.Count));
        Assert.Equal(n, (await NewRepository().ListHoldingsAsync(Tenant, contactId)).Value!.Count);
    }

    [Fact]
    public async Task Two_wins_at_once_make_exactly_one_holding_per_line_item()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured) return;
        await SqlMigrationTestHelper.ApplyAsync();
        var (dealId, contactId, n) = await DealWithLineItemsAsync(4);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => NewRepository().WinDealAsync(Tenant, dealId, "test")));

        Assert.All(results, r => Assert.True(r.IsSuccess, r.Error));
        Assert.Equal(n, results.Sum(r => r.Value!.HoldingsCreated));
        Assert.Equal(n, (await NewRepository().ListHoldingsAsync(Tenant, contactId)).Value!.Count);
    }

    [Fact]
    public async Task A_win_that_fails_partway_leaves_no_holdings_and_the_old_stage()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured) return;
        await SqlMigrationTestHelper.ApplyAsync();
        var (dealId, contactId, _) = await DealWithLineItemsAsync(2);

        var options = new DbContextOptionsBuilder<CrmDbContext>().UseNpgsql(IntegrationTestConfig.DatabaseUrl!).Options;
        var db = new CrmDbContext(options);
        var failing = new CrmRepository(db, TimeProvider.System, NullLogger<CrmRepository>.Instance, new FailAfterWorkScope(new PostgresCrmTenantScope(db)));
        var failed = await failing.WinDealAsync(Tenant, dealId, "test");

        Assert.False(failed.IsSuccess);
        Assert.Empty((await NewRepository().ListHoldingsAsync(Tenant, contactId)).Value!);
        var deals = await NewRepository().ListDealsAsync(Tenant, openOnly: true, 200);
        Assert.Contains(deals.Value!, d => d.Id == dealId && d.StageOutcome == StageOutcomes.Open);
    }

    [Fact]
    public async Task Another_tenant_sees_none_of_the_catalog_the_line_items_or_the_holdings()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured) return;
        await SqlMigrationTestHelper.ApplyAsync();
        var (dealId, contactId, _) = await DealWithLineItemsAsync(1);
        await NewRepository().WinDealAsync(Tenant, dealId, "test");
        var other = Guid.NewGuid();

        Assert.Empty((await NewRepository().ListProductsAsync(other, null, true)).Value ?? []);
        Assert.False((await NewRepository().ListLineItemsAsync(other, dealId)).IsSuccess);
        Assert.False((await NewRepository().ListHoldingsAsync(other, contactId)).IsSuccess);
        Assert.False((await NewRepository().WinDealAsync(other, dealId, "test")).IsSuccess);
    }

    // ----- Phase 2, the rest: prices, and the platform table's gate -----

    [Fact]
    public async Task A_price_change_keeps_history_and_a_booked_level_is_held_once_by_the_database()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured) return;
        await SqlMigrationTestHelper.ApplyAsync();
        var unique = Guid.NewGuid().ToString("N")[..8];
        var product = (await NewRepository().CreateProductAsync(new(Tenant, "Setup " + unique, "setup-" + unique, ProductKinds.Service, ProductCategories.Accounting, null, null, BillingPeriods.OneTime))).Value!;

        Assert.True((await NewRepository().SetPriceAsync(product.Id, new(Tenant, null, null, 100m, null, new DateOnly(2026, 1, 1)))).IsSuccess);
        Assert.True((await NewRepository().SetPriceAsync(product.Id, new(Tenant, null, null, 120m, null, new DateOnly(2026, 7, 1)))).IsSuccess);
        Assert.Equal(100m, (await NewRepository().GetPriceInEffectAsync(Tenant, product.Id, null, new DateOnly(2026, 3, 1))).Value!.UnitPrice);
        Assert.Equal(120m, (await NewRepository().GetPriceInEffectAsync(Tenant, product.Id, null, new DateOnly(2026, 8, 1))).Value!.UnitPrice);

        await using var conn = new Npgsql.NpgsqlConnection(IntegrationTestConfig.DatabaseUrl!);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using (var set = new Npgsql.NpgsqlCommand("SELECT set_config('app.tenant_id', @t, true)", conn, tx))
        {
            set.Parameters.AddWithValue("t", Tenant.ToString());
            await set.ExecuteScalarAsync();
        }
        await using var first = new Npgsql.NpgsqlCommand("INSERT INTO geek_crm.pricing_uplift_tiers (id, tenant_id, booked_pct_from, uplift_pct, created_at) VALUES (gen_random_uuid(), @t, 33.33, 5, now())", conn, tx);
        first.Parameters.AddWithValue("t", Tenant);
        await first.ExecuteNonQueryAsync();
        await using var second = new Npgsql.NpgsqlCommand("INSERT INTO geek_crm.pricing_uplift_tiers (id, tenant_id, booked_pct_from, uplift_pct, created_at) VALUES (gen_random_uuid(), @t, 33.33, 9, now())", conn, tx);
        second.Parameters.AddWithValue("t", Tenant);
        var duplicate = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => second.ExecuteNonQueryAsync());
        Assert.Equal(Npgsql.PostgresErrorCodes.UniqueViolation, duplicate.SqlState);
    }

    [Fact]
    public async Task A_subscription_is_visible_to_a_platform_operation_and_to_nothing_else()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured) return;
        await SqlMigrationTestHelper.ApplyAsync();
        var unique = Guid.NewGuid().ToString("N")[..8];
        var subscriber = Guid.NewGuid();

        await using var conn = new Npgsql.NpgsqlConnection(IntegrationTestConfig.DatabaseUrl!);
        await conn.OpenAsync();
        await using (var tx = await conn.BeginTransactionAsync())
        {
            await using var flag = new Npgsql.NpgsqlCommand("SELECT set_config('app.platform', 'on', true)", conn, tx);
            await flag.ExecuteScalarAsync();
            await using var insert = new Npgsql.NpgsqlCommand("INSERT INTO geek_crm.tenants (id, name, slug, created_at) VALUES (@id, @name, @slug, now())", conn, tx);
            insert.Parameters.AddWithValue("id", subscriber);
            insert.Parameters.AddWithValue("name", "Subscriber " + unique);
            insert.Parameters.AddWithValue("slug", "sub-" + unique);
            await insert.ExecuteNonQueryAsync();
            await tx.CommitAsync();
        }

        var code = "geek-receptionist-" + unique;
        var product = await NewRepository().CreateProductAsync(new(Tenant, "Receptionist " + unique, code, ProductKinds.GeekSaas, ProductCategories.BusinessOperations, null, null, BillingPeriods.Monthly));
        Assert.True(product.IsSuccess, product.Error);

        var platform = NewRepository();
        var made = await platform.CreateSubscriptionAsync(new(subscriber, code, "Starter", HoldingStatuses.Active, new DateOnly(2026, 11, 11), null, null));
        Assert.True(made.IsSuccess, made.Error);
        Assert.Contains((await platform.ListSubscriptionsAsync(subscriber)).Value!, s => s.Id == made.Value!.Id);

        // A tenant-scoped transaction (the seller's own tenant, no platform flag) sees no subscription at all.
        await using var tenantTx = await conn.BeginTransactionAsync();
        await using (var set = new Npgsql.NpgsqlCommand("SELECT set_config('app.tenant_id', @t, true)", conn, tenantTx))
        {
            set.Parameters.AddWithValue("t", Tenant.ToString());
            await set.ExecuteScalarAsync();
        }
        await using var count = new Npgsql.NpgsqlCommand("SELECT count(*) FROM geek_crm.tenant_subscriptions", conn, tenantTx);
        Assert.Equal(0L, (long)(await count.ExecuteScalarAsync())!);
    }
}

