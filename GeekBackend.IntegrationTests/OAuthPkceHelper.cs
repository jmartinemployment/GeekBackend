using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace GeekBackend.IntegrationTests;

internal static class OAuthPkceHelper
{
    public static (string Verifier, string Challenge) CreatePkcePair()
    {
        var verifierBytes = RandomNumberGenerator.GetBytes(32);
        var verifier = Base64UrlEncode(verifierBytes);
        var challenge = Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return (verifier, challenge);
    }

    public static string BuildAuthorizeUrl(
        string issuer,
        string clientId,
        string redirectUri,
        string challenge,
        string scope = "openid offline_access")
    {
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = clientId,
            ["response_type"] = "code",
            ["scope"] = scope,
            ["redirect_uri"] = redirectUri,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256"
        };

        return QueryHelpers.AddQueryString($"{issuer.TrimEnd('/')}/connect/authorize", query);
    }

    public static async Task<OAuthTokenResponse> LoginAndExchangeCodeAsync(
        HttpClient client,
        string authorizeUrl,
        string email,
        string password,
        string redirectUri,
        string codeVerifier,
        string clientId)
    {
        var code = await LoginAndGetAuthorizationCodeAsync(
            client,
            authorizeUrl,
            email,
            password,
            redirectUri);

        using var tokenContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = clientId,
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = codeVerifier
        });

        var tokenResponse = await client.PostAsync("/connect/token", tokenContent);
        var body = await tokenResponse.Content.ReadAsStringAsync();
        if (!tokenResponse.IsSuccessStatusCode)
            throw new InvalidOperationException($"Token exchange failed ({(int)tokenResponse.StatusCode}): {body}");

        return JsonSerializer.Deserialize<OAuthTokenResponse>(body, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("Token response was empty.");
    }

    public static async Task<string> LoginAndGetAuthorizationCodeAsync(
        HttpClient client,
        string authorizeUrl,
        string email,
        string password,
        string redirectUri)
    {
        var authorizeResponse = await client.GetAsync(authorizeUrl);
        var loginLocation = await FollowToLoginIfNeededAsync(client, authorizeResponse);

        var returnUrl = ExtractReturnUrl(loginLocation)
            ?? authorizeUrl.Replace(client.BaseAddress!.ToString().TrimEnd('/'), string.Empty, StringComparison.Ordinal);

        using var loginForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = password,
            ["Input.ReturnUrl"] = returnUrl,
            ["Input.Button"] = "login"
        });

        var loginPath = loginLocation.IsAbsoluteUri
            ? loginLocation.PathAndQuery
            : loginLocation.ToString();
        var loginResponse = await client.PostAsync(loginPath, loginForm);
        if (IsTwoFactorRedirect(loginResponse))
            throw new InvalidOperationException("Login redirected to 2FA; use a non-2FA test user for this flow.");

        var afterLogin = loginResponse.Headers.Location is not null
            ? await client.GetAsync(loginResponse.Headers.Location)
            : await client.GetAsync(authorizeUrl);

        var codeResponse = await FollowUntilRedirectUriAsync(client, afterLogin, redirectUri);
        return ExtractAuthorizationCode(codeResponse, redirectUri);
    }

    public static bool IsTwoFactorRedirect(HttpResponseMessage response) =>
        response.Headers.Location?.AbsolutePath.Contains("/Account/TwoFactor", StringComparison.OrdinalIgnoreCase) == true;

    private static async Task<Uri> FollowToLoginIfNeededAsync(HttpClient client, HttpResponseMessage response)
    {
        var current = response;
        for (var i = 0; i < 10; i++)
        {
            if (current.Headers.Location is null)
                break;

            var next = current.Headers.Location.IsAbsoluteUri
                ? current.Headers.Location
                : new Uri(client.BaseAddress!, current.Headers.Location);

            if (next.AbsolutePath.Contains("/Account/Login", StringComparison.OrdinalIgnoreCase))
                return next;

            current = await client.GetAsync(next);
        }

        return current.Headers.Location ?? new Uri("/Account/Login", UriKind.Relative);
    }

    private static async Task<HttpResponseMessage> FollowUntilRedirectUriAsync(
        HttpClient client,
        HttpResponseMessage response,
        string redirectUri)
    {
        var current = response;
        for (var i = 0; i < 15; i++)
        {
            if (current.Headers.Location is not null)
            {
                var location = current.Headers.Location.IsAbsoluteUri
                    ? current.Headers.Location
                    : new Uri(client.BaseAddress!, current.Headers.Location);

                if (location.AbsoluteUri.StartsWith(redirectUri, StringComparison.OrdinalIgnoreCase))
                    return current;

                current = await client.GetAsync(location);
                continue;
            }

            if ((int)current.StatusCode is >= 200 and < 300)
                break;
        }

        return current;
    }

    private static string ExtractAuthorizationCode(HttpResponseMessage response, string redirectUri)
    {
        var location = response.Headers.Location;
        if (location is null)
            throw new InvalidOperationException("Authorization did not return a redirect with an authorization code.");

        var target = location.IsAbsoluteUri ? location : new Uri(redirectUri + location);
        if (!target.AbsoluteUri.StartsWith(redirectUri, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unexpected redirect: {target}");

        var query = QueryHelpers.ParseQuery(target.Query);
        if (!query.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
            throw new InvalidOperationException("Authorization code missing from redirect.");

        return code.ToString();
    }

    private static string? ExtractReturnUrl(Uri loginLocation)
    {
        var query = QueryHelpers.ParseQuery(loginLocation.Query);
        return query.TryGetValue("ReturnUrl", out var value) ? value.ToString() : null;
    }

    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

internal sealed class OAuthTokenResponse
{
    public string? Access_token { get; set; }
    public string? Refresh_token { get; set; }
    public string? Token_type { get; set; }
    public int Expires_in { get; set; }
}
