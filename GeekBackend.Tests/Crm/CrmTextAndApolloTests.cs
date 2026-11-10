using GeekAPI.Services.Crm;
using GeekApplication.Models.Crm;
using GeekRepository.Data.Entities.Crm;
using GeekRepository.Repositories.Crm;

namespace GeekBackend.Tests.Crm;

public class CrmTextAndApolloTests
{
    [Theory]
    [InlineData("'+1 855-473-4778", "+18554734778")]
    [InlineData("+1 (561) 555-0100", "+15615550100")]
    [InlineData("5615550100", "+15615550100")]
    [InlineData("15615550100", "+15615550100")]
    [InlineData("+44 20 7946 0958", "+442079460958")]
    [InlineData("555-0100", null)]
    [InlineData("", null)]
    public void Phone_numbers_normalize_to_E164(string raw, string? expected) =>
        Assert.Equal(expected, CrmText.ToE164(raw));

    [Theory]
    [InlineData("https://www.firstresponseplumbers.com/", "firstresponseplumbers.com")]
    [InlineData("bocadental.com", "bocadental.com")]
    [InlineData(null, null)]
    public void Website_domains_drop_scheme_and_www(string? url, string? expected) => Assert.Equal(expected, CrmText.Domain(url));

    [Theory]
    [InlineData("238220, 236118", 6, "238220")]
    [InlineData("1711", 4, "1711")]
    [InlineData("23822", 6, null)]
    public void First_industry_code_must_have_the_right_digit_count(string raw, int digits, string? expected) =>
        Assert.Equal(expected, CrmText.FirstCode(raw, digits));

    [Theory]
    [InlineData("Owner", ParticipantLinkRoles.Owner)]
    [InlineData("President & CEO", ParticipantLinkRoles.Owner)]
    [InlineData("Office Manager", ParticipantLinkRoles.Employee)]
    [InlineData(null, ParticipantLinkRoles.Employee)]
    public void Titles_map_to_link_roles(string? title, string role) => Assert.Equal(role, CrmText.RoleFromTitle(title));

    [Fact]
    public void Apollo_export_rows_become_prospects_by_header_name()
    {
        const string csv = "﻿First Name,Last Name,Title,Company Name,Email,Mobile Phone,Corporate Phone,Do Not Call,# Employees,Industry,Website,Company Address,Company City,Company State,Annual Revenue,NAICS Codes,SIC Codes,Apollo Contact Id,Seniority\r\n"
                         + "Jon,Carpenter,Owner,\"First Response Plumbing, Inc.\",jon@example.com,,'+1 855-473-4778,false,2,construction,https://firstresponseplumbers.com,\"20423 State Road 7, Boca Raton, Florida, United States, 33498\",Boca Raton,Florida,1500000,\"238220, 236118\",1711,abc123,Owner\r\n"
                         + ",,,,,,,,,,,,,,,,,,\r\n";

        var p = Assert.Single(ApolloCsv.Parse(csv));
        Assert.Equal("Jon", p.FirstName);
        Assert.Equal("First Response Plumbing, Inc.", p.CompanyName);
        Assert.Equal("+18554734778", p.CompanyPhoneE164);
        Assert.Equal("20423 State Road 7", p.CompanyAddressLine1);
        Assert.Equal("33498", p.CompanyPostalCode);
        Assert.Equal("1m_5m", p.AnnualRevenueBand);
        Assert.Equal("238220, 236118", p.NaicsCode);
        Assert.Equal(2, p.EmployeeCount);
        Assert.Equal("abc123", p.ExternalId);
        Assert.False(p.DoNotCall);
        Assert.Empty(p.Phones);
    }

    [Theory]
    [InlineData("200000", "under_250k")]
    [InlineData("1.5M", "1m_5m")]
    [InlineData("$30,000,000", "over_25m")]
    [InlineData("unknown", null)]
    public void Revenue_maps_to_a_band(string raw, string? band) => Assert.Equal(band, ApolloCsv.RevenueBand(raw));

    [Fact]
    public void Past_qualified_a_deal_needs_a_primary_contact_but_lost_never_does()
    {
        var stages = new[] { "New", "Qualified", "Meeting booked", "Proposal sent", "Won", "Lost" }
            .Select((name, i) => new CrmStageRow
            {
                Name = name,
                Position = i + 1,
                Outcome = name == "Won" ? StageOutcomes.Won : name == "Lost" ? StageOutcomes.Lost : StageOutcomes.Open,
            }).ToList();

        Assert.False(CrmRepository.RequiresPrimaryContact(stages[0], stages));
        Assert.False(CrmRepository.RequiresPrimaryContact(stages[1], stages));
        Assert.True(CrmRepository.RequiresPrimaryContact(stages[2], stages));
        Assert.True(CrmRepository.RequiresPrimaryContact(stages[4], stages));
        Assert.False(CrmRepository.RequiresPrimaryContact(stages[5], stages));
    }
}
