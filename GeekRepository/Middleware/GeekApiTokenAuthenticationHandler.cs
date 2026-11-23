using System.Security.Claims;
using System.Text.Encodings.Web;
using GeekRepository.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace GeekRepository.Middleware;

/// <summary>
/// Service-to-service auth by proof of identity: GeekAPI presents a short-lived token it signed, as <c>Authorization: Bearer</c>. It
/// refuses rather than abstains, like the key handlers: a missing, malformed, expired, replayed or wrongly signed token is a failure.
/// </summary>
public sealed class GeekApiTokenAuthenticationHandler : AuthenticationHandler<GeekApiTokenAuthenticationOptions>
{
    public const string SchemeName = "GeekApiToken";
    private const string BearerPrefix = "Bearer ";

    private readonly GeekApiTokenValidator _validator;

    public GeekApiTokenAuthenticationHandler(
        IOptionsMonitor<GeekApiTokenAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        GeekApiTokenValidator validator)
        : base(options, logger, encoder)
    {
        _validator = validator;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Exactly one Authorization header, for the same reason the key handlers take exactly one key header.
        if (!Request.Headers.TryGetValue("Authorization", out var provided) || provided.Count != 1)
            return AuthenticateResult.Fail("Authorization is missing.");

        var header = provided[0];
        if (header is null || !header.StartsWith(BearerPrefix, StringComparison.Ordinal))
            return AuthenticateResult.Fail("Authorization is not a Bearer token.");

        var check = await _validator.CheckAsync(header[BearerPrefix.Length..].Trim());
        if (!check.IsValid)
            return AuthenticateResult.Fail(check.Reason);

        // The scope the keys grant today, so every route that accepts the keys accepts the token.
        var identity = new ClaimsIdentity(SchemeName);
        identity.AddClaim(new Claim("scope", RepositoryAuthConstants.InternalApiScope));
        identity.AddClaim(new Claim(ClaimTypes.Name, "geek-api"));
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}

public sealed class GeekApiTokenAuthenticationOptions : AuthenticationSchemeOptions
{
}
