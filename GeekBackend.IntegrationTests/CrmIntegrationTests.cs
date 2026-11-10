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
        return new CrmRepository(new CrmDbContext(options), TimeProvider.System, NullLogger<CrmRepository>.Instance);
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
}
