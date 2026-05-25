using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace GeekRepository.Middleware;

/// <summary>
/// Legacy bootstrap auth for local/integration tests while <c>geekapi</c> client credentials are provisioned.
/// Remove <c>REPO_API_KEY</c> in production once OAuth is verified.
/// </summary>
public sealed class RepoApiKeyAuthenticationHandler : AuthenticationHandler<RepoApiKeyAuthenticationOptions>
{
    public const string SchemeName = "RepoApiKey";
    private const string Header = "X-Repo-Key";

    public RepoApiKeyAuthenticationHandler(
        IOptionsMonitor<RepoApiKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var expectedKey = Environment.GetEnvironmentVariable("REPO_API_KEY");
        if (string.IsNullOrWhiteSpace(expectedKey))
            return Task.FromResult(AuthenticateResult.NoResult());

        if (!Request.Headers.TryGetValue(Header, out var provided)
            || !string.Equals(provided, expectedKey, StringComparison.Ordinal))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity(SchemeName);
        identity.AddClaim(new Claim("scope", "internal.api"));
        identity.AddClaim(new Claim(ClaimTypes.Name, "repo-api-key-bootstrap"));
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

public sealed class RepoApiKeyAuthenticationOptions : AuthenticationSchemeOptions;
