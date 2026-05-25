using System.Collections.Immutable;
using System.Security.Claims;
using GeekAPI.Auth;
using GeekApplication.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace GeekAPI.Controllers.Auth;

[ApiController]
public sealed class AuthorizationController : ControllerBase
{
    private readonly IOpenIddictApplicationManager _applications;

    public AuthorizationController(IOpenIddictApplicationManager applications) =>
        _applications = applications;

    [HttpGet("~/connect/authorize")]
    [HttpPost("~/connect/authorize")]
    public async Task<IActionResult> Authorize(CancellationToken cancellationToken)
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("OpenIddict request cannot be resolved.");

        var cookieAuth = await HttpContext.AuthenticateAsync(GeekAuthConstants.CookieScheme);
        if (!cookieAuth.Succeeded || cookieAuth.Principal?.Identity?.IsAuthenticated != true)
        {
            return Challenge(
                authenticationSchemes: GeekAuthConstants.CookieScheme,
                properties: new AuthenticationProperties
                {
                    RedirectUri = BuildAuthorizeReturnUrl()
                });
        }

        var cookiePrincipal = cookieAuth.Principal
            ?? throw new InvalidOperationException("Authenticated principal is missing.");

        var identity = new ClaimsIdentity(
            cookiePrincipal.Claims,
            authenticationType: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
            nameType: Claims.Name,
            roleType: Claims.Role);

        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes(request.GetScopes());
        principal.SetResources(await GetResourcesAsync(request.GetScopes(), cancellationToken));

        foreach (var claim in principal.Claims)
            claim.SetDestinations(GetDestinations(claim, principal));

        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    [HttpPost("~/connect/token")]
    [EnableRateLimiting("token")]
    public async Task<IActionResult> Exchange(CancellationToken cancellationToken)
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("OpenIddict request cannot be resolved.");

        if (request.IsPasswordGrantType()
            || string.Equals(request.GrantType, GrantTypes.Password, StringComparison.Ordinal))
        {
            return Forbid(
                authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                properties: new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.UnsupportedGrantType,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                        "The password grant is not allowed."
                }));
        }

        if (!string.IsNullOrEmpty(request.GrantType)
            && request.GrantType.StartsWith("urn:geek:", StringComparison.OrdinalIgnoreCase))
        {
            return Forbid(
                authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                properties: new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.UnsupportedGrantType,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                        "Custom grants are not supported."
                }));
        }

        if (request.IsClientCredentialsGrantType())
        {
            var application = await _applications.FindByClientIdAsync(request.ClientId!, cancellationToken)
                ?? throw new InvalidOperationException("Application not found.");

            var identity = new ClaimsIdentity(
                authenticationType: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                nameType: Claims.Name,
                roleType: Claims.Role);

            identity.SetClaim(Claims.Subject, await _applications.GetClientIdAsync(application, cancellationToken)
                ?? request.ClientId!);
            identity.SetClaim(Claims.Name, await _applications.GetDisplayNameAsync(application, cancellationToken)
                ?? request.ClientId!);

            var principal = new ClaimsPrincipal(identity);
            principal.SetScopes(request.GetScopes());
            principal.SetResources(await GetResourcesAsync(request.GetScopes(), cancellationToken));

            foreach (var claim in principal.Claims)
                claim.SetDestinations(GetDestinations(claim, principal));

            return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        if (request.IsAuthorizationCodeGrantType() || request.IsRefreshTokenGrantType())
        {
            var result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            var principal = result.Principal
                ?? throw new InvalidOperationException("Cannot retrieve token principal.");

            foreach (var claim in principal.Claims)
                claim.SetDestinations(GetDestinations(claim, principal));

            return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        throw new InvalidOperationException("Unsupported grant type.");
    }

    [HttpGet("~/connect/userinfo")]
    [HttpPost("~/connect/userinfo")]
    public async Task<IActionResult> Userinfo(CancellationToken cancellationToken)
    {
        var result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var principal = result.Principal
            ?? throw new InvalidOperationException("Userinfo requires an authenticated principal.");

        return Ok(new
        {
            sub = principal.GetClaim(Claims.Subject),
            name = principal.GetClaim(Claims.Name),
            email = principal.GetClaim(Claims.Email)
        });
    }

    private string BuildAuthorizeReturnUrl()
    {
        var query = Request.HasFormContentType
            ? Request.Form.ToList()
            : Request.Query.ToList();
        return Request.PathBase + Request.Path + QueryString.Create(query);
    }

    private static Task<IEnumerable<string>> GetResourcesAsync(
        ImmutableArray<string> scopes,
        CancellationToken cancellationToken)
    {
        if (scopes.Any(static s => s == Scopes.OpenId))
            return Task.FromResult<IEnumerable<string>>(["geek-api"]);

        if (scopes.Any(static s => s == GeekOAuthConstants.InternalApiScope))
            return Task.FromResult<IEnumerable<string>>([GeekOAuthConstants.GeekRepositoryAudience]);

        return Task.FromResult<IEnumerable<string>>(Array.Empty<string>());
    }

    private static IEnumerable<string> GetDestinations(Claim claim, ClaimsPrincipal principal)
    {
        switch (claim.Type)
        {
            case Claims.Name or Claims.Email or Claims.Subject:
                yield return Destinations.AccessToken;
                if (principal.HasScope(Scopes.Profile) || principal.HasScope(Scopes.Email))
                    yield return Destinations.IdentityToken;
                yield break;
            default:
                yield return Destinations.AccessToken;
                yield break;
        }
    }
}
