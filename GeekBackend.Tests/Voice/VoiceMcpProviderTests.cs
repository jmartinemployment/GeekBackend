using GeekApplication.Models.Voice;

namespace GeekBackend.Tests.Voice;

/// <summary>
/// What each connected system is said to be able to do, and the lists derived from it.
///
/// <para>
/// These exist because the lists were once written by hand and disagreed with each other: <c>bccc1a30</c> (2026-11-19) carved Google
/// Drive out of the provider list as "everything that is not Drive is a calendar", which left HubSpot - a CRM - in
/// <see cref="VoiceMcpProviders.Calendars"/>, the list the appointments table's CHECK constraint mirrors. A derived list cannot drift
/// from the capability it is derived from; these assert that it is in fact derived, and that each provider's capabilities say what we
/// have actually built.
/// </para>
/// </summary>
public class VoiceMcpProviderTests
{
    [Fact]
    public void HubSpot_is_a_crm_and_not_somewhere_an_appointment_can_come_from()
    {
        Assert.Contains(VoiceMcpProviders.HubSpot, VoiceMcpProviders.Crms);
        Assert.DoesNotContain(VoiceMcpProviders.HubSpot, VoiceMcpProviders.Calendars);
    }

    [Fact]
    public void Google_drive_is_storage_and_not_a_calendar()
    {
        Assert.Contains(VoiceMcpProviders.GoogleDrive, VoiceMcpProviders.Stores);
        Assert.DoesNotContain(VoiceMcpProviders.GoogleDrive, VoiceMcpProviders.Calendars);
    }

    [Theory]
    [InlineData(VoiceMcpProviders.GoogleCalendar)]
    [InlineData(VoiceMcpProviders.Outlook)]
    [InlineData(VoiceMcpProviders.CalCom)]
    public void A_calendar_is_a_provider_that_declares_booking(string code)
    {
        Assert.Contains(code, VoiceMcpProviders.Calendars);
        Assert.True(VoiceMcpProviders.Known.Single(p => p.Code == code).Booking);
    }

    [Fact]
    public void Calendars_crms_and_stores_are_derived_from_the_capabilities_and_not_listed_by_hand()
    {
        Assert.Equal(VoiceMcpProviders.Known.Where(p => p.Booking).Select(p => p.Code), VoiceMcpProviders.Calendars);
        Assert.Equal(VoiceMcpProviders.Known.Where(p => p.Contacts).Select(p => p.Code), VoiceMcpProviders.Crms);
        Assert.Equal(VoiceMcpProviders.Known.Where(p => p.Storage).Select(p => p.Code), VoiceMcpProviders.Stores);
    }

    [Fact]
    public void Every_known_provider_is_in_all_exactly_once()
    {
        Assert.Equal(VoiceMcpProviders.Known.Select(p => p.Code), VoiceMcpProviders.All);
        Assert.Equal(VoiceMcpProviders.All.Count, VoiceMcpProviders.All.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Every_provider_can_do_at_least_one_thing_and_is_named_for_an_owner()
    {
        foreach (var provider in VoiceMcpProviders.Known)
        {
            Assert.True(
                provider.Booking || provider.Contacts || provider.Storage,
                $"{provider.Code} declares no capability, so nothing could ever use it.");
            Assert.False(string.IsNullOrWhiteSpace(provider.Name), $"{provider.Code} has no name to show an owner.");
        }
    }

    [Fact]
    public void Nothing_is_both_a_calendar_and_a_store()
    {
        Assert.Empty(VoiceMcpProviders.Calendars.Intersect(VoiceMcpProviders.Stores, StringComparer.Ordinal));
    }
}
