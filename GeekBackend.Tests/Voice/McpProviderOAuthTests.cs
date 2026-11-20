using System.Net;
using System.Text;
using GeekAPI.Services.Voice.Mcp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace GeekBackend.Tests.Voice;

/// <summary>
/// The provider registry, and the one place vendors disagree that a renewal cannot paper over: whether the refresh token rotates.
///
/// <para>
/// Microsoft's documented behaviour is that refresh tokens "replace themselves with a fresh token upon every use", and Google's is that
/// the stored one stays valid. Storing the old token again against a rotating vendor renews perfectly today and strands the tenant once
/// the original ages out - a failure that would appear months later, on a live call, with nothing in a test to have caught it.
/// </para>
/// </summary>
public class McpProviderOAuthTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private sealed class Stub(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];
        public List<string> Urls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Urls.Add(request.RequestUri!.ToString());
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static (GoogleCalendarOAuth OAuth, Stub Handler) OAuthWith(string body)
    {
        var handler = new Stub(body);
        var options = new GoogleCalendarOptions("client-id", "client-secret", "https://portal.example");
        return (new GoogleCalendarOAuth(new HttpClient(handler), options, new FakeTimeProvider(Now), NullLogger<GoogleCalendarOAuth>.Instance), handler);
    }

    /// <summary>A provider that rotates, standing in for Microsoft so the behaviour is tested without a second vendor being wired up.</summary>
    private static McpProviderOAuth Rotating => McpProviders.Calendar with
    {
        Provider = "rotating_test",
        DisplayName = "Rotating Vendor",
        TokenEndpoint = "https://login.example/token",
        RotatesRefreshToken = true,
    };

    [Fact]
    public async Task A_rotating_vendors_new_refresh_token_is_carried_back_to_be_stored()
    {
        var (oauth, _) = OAuthWith("""{"access_token":"new-access","expires_in":3600,"refresh_token":"the-next-one"}""");

        var renewed = await oauth.RefreshAsync("the-old-one", Rotating);

        Assert.True(renewed.IsSuccess);
        Assert.Equal("the-next-one", renewed.Value!.RefreshToken);
    }

    [Fact]
    public async Task A_rotating_vendor_that_sends_no_refresh_token_fails_rather_than_keeping_the_spent_one()
    {
        var (oauth, _) = OAuthWith("""{"access_token":"new-access","expires_in":3600}""");

        var renewed = await oauth.RefreshAsync("the-old-one", Rotating);

        Assert.False(renewed.IsSuccess);
        Assert.Equal(McpFailureCodes.OAuthUnavailable, renewed.ErrorCode);
    }

    [Fact]
    public async Task Google_does_not_rotate_so_nothing_new_is_carried_back_and_the_stored_token_stands()
    {
        var (oauth, _) = OAuthWith("""{"access_token":"new-access","expires_in":3600}""");

        var renewed = await oauth.RefreshAsync("the-stored-one", McpProviders.Calendar);

        Assert.True(renewed.IsSuccess);
        Assert.Null(renewed.Value!.RefreshToken);
    }

    [Fact]
    public async Task A_renewal_goes_to_the_providers_own_token_endpoint_and_not_a_hardcoded_one()
    {
        var (oauth, handler) = OAuthWith("""{"access_token":"a","expires_in":3600,"refresh_token":"b"}""");

        await oauth.RefreshAsync("r", Rotating);

        Assert.Equal("https://login.example/token", Assert.Single(handler.Urls));
    }

    [Fact]
    public void Googles_authorization_address_carries_the_two_parameters_a_refresh_token_depends_on()
    {
        var (oauth, _) = OAuthWith("{}");

        var url = oauth.AuthorizationUrl("the-state", McpProviders.Calendar);

        Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth?", url, StringComparison.Ordinal);
        Assert.Contains("access_type=offline", url, StringComparison.Ordinal);
        Assert.Contains("prompt=consent", url, StringComparison.Ordinal);
        Assert.Contains("state=the-state", url, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_registry_entry_names_its_credentials_and_a_redirect_the_portal_can_serve()
    {
        foreach (var entry in McpProviders.All)
        {
            Assert.StartsWith("https://", entry.AuthorizeEndpoint, StringComparison.Ordinal);
            Assert.StartsWith("https://", entry.TokenEndpoint, StringComparison.Ordinal);
            Assert.NotEmpty(entry.Scopes);
            Assert.False(string.IsNullOrWhiteSpace(entry.ClientIdVariable));
            Assert.False(string.IsNullOrWhiteSpace(entry.ClientSecretVariable));
            Assert.False(string.IsNullOrWhiteSpace(entry.PartialGrantMessage));
            Assert.False(string.IsNullOrWhiteSpace(entry.DisplayName));
        }
    }

    [Fact]
    public void A_provider_this_product_cannot_connect_is_not_in_the_registry()
    {
        Assert.Null(McpProviders.For("outlook"));
        Assert.Null(McpProviders.For("hubspot"));
        Assert.NotNull(McpProviders.For("google_calendar"));
        Assert.NotNull(McpProviders.For("google_drive"));
    }
}
