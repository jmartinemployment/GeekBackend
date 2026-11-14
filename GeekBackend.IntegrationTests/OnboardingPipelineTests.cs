using System.Net;
using System.Net.Http.Json;

namespace GeekBackend.IntegrationTests;

/// <summary>
/// The signup route through the real middleware order: it needs the API key, a refused signup is a 400 with its code, a visitor
/// outside the US is refused whatever the form says, and the eleventh call for one visitor address is a 429 while another
/// visitor, calling from the same portal, is not. A static check that the controller carries the cap cannot see that Program.cs never
/// added the limiter to the pipeline, which is what this does.
/// </summary>
public sealed class OnboardingPipelineTests(GeekApiTestFactory factory) : IClassFixture<GeekApiTestFactory>
{
    private const string Route = "/api/v1/onboarding/validate-eligibility";

    // The form says Canada, so the gate refuses before any call to GeekRepository is made: nothing here needs a store.
    private static readonly object RefusedByForm = new
    {
        businessName = "Maple Heating", industryVertical = "HVAC", countryCode = "CA", stateCode = "ON",
        contactEmail = "owner@maple.example", contactPhone = "+14165550101", complianceAttested = true,
    };

    private static readonly object DeclaresUs = new
    {
        businessName = "Maple Heating", industryVertical = "HVAC", countryCode = "US", stateCode = "FL",
        contactEmail = "owner@maple.example", contactPhone = "+15615550101", complianceAttested = true,
    };

    private HttpClient Portal(string? visitorIp, string? visitorCountry)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", "integration-test-backend-key");
        if (visitorIp is not null) client.DefaultRequestHeaders.Add("X-Visitor-Ip", visitorIp);
        if (visitorCountry is not null) client.DefaultRequestHeaders.Add("X-Visitor-Country", visitorCountry);
        return client;
    }

    [Fact]
    public async Task A_call_without_the_api_key_is_refused_before_the_gate()
    {
        var response = await factory.CreateClient().PostAsJsonAsync(Route, RefusedByForm);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_visitor_outside_the_US_is_refused_though_the_form_says_US()
    {
        var response = await Portal("198.51.100.7", "CA").PostAsJsonAsync(Route, DeclaresUs);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("visitor_country_not_supported", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_call_that_forwards_no_visitor_is_refused_as_unverified()
    {
        var response = await Portal(null, null).PostAsJsonAsync(Route, DeclaresUs);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("visitor_unverified", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_eleventh_call_for_one_visitor_address_is_refused_and_another_visitor_is_not()
    {
        var busy = Portal("203.0.113.50", "US");

        for (var i = 1; i <= 10; i++)
        {
            var response = await busy.PostAsJsonAsync(Route, RefusedByForm);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("country_not_supported", await response.Content.ReadAsStringAsync());
        }

        var eleventh = await busy.PostAsJsonAsync(Route, RefusedByForm);
        var other = await Portal("203.0.113.51", "US").PostAsJsonAsync(Route, RefusedByForm);

        Assert.Equal(HttpStatusCode.TooManyRequests, eleventh.StatusCode);
        Assert.Contains("rate_limited", await eleventh.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, other.StatusCode);
    }
}
