using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace GeekRepository.Middleware;

/// <summary>
/// The credential for /internal/v1/* (docs/Geek-Receptionist.md section 1): GeekAPI presents
/// <c>GEEK_REPOSITORY_API_KEY</c> as <c>X-Internal-Api-Key</c>. It refuses rather than abstains, in the same way as
/// <see cref="RepoApiKeyAuthenticationHandler"/>: a missing, repeated, empty or wrong key is a failure, and the expected key is
/// resolved once at startup so no request has a path on which it is absent. The two credentials do not stand in for each other:
/// /repo/* answers only to X-Repo-Key and /internal/v1/* only to X-Internal-Api-Key.
/// </summary>
public sealed class InternalApiKeyAuthenticationHandler : AuthenticationHandler<InternalApiKeyAuthenticationOptions>
{
    public const string SchemeName = "InternalApiKey";
    private const string Header = "X-Internal-Api-Key";

    public InternalApiKeyAuthenticationHandler(
        IOptionsMonitor<InternalApiKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Header, out var provided) || provided.Count != 1)
        {
            return Task.FromResult(AuthenticateResult.Fail($"{Header} is missing."));
        }

        var providedKey = provided[0];
        if (string.IsNullOrEmpty(providedKey))
        {
            return Task.FromResult(AuthenticateResult.Fail($"{Header} is empty."));
        }

        var providedBytes = Encoding.UTF8.GetBytes(providedKey);
        var expectedBytes = Encoding.UTF8.GetBytes(Options.ExpectedKey);
        if (!CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes))
        {
            return Task.FromResult(AuthenticateResult.Fail($"{Header} is not valid."));
        }

        var identity = new ClaimsIdentity(SchemeName);
        identity.AddClaim(new Claim("scope", "internal.api"));
        identity.AddClaim(new Claim(ClaimTypes.Name, "geek-api-internal"));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}

public sealed class InternalApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    /// <summary>The key a caller must present. Resolved from <c>GEEK_REPOSITORY_API_KEY</c> at startup and never empty.</summary>
    public string ExpectedKey { get; set; } = string.Empty;
}
