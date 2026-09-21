using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace GeekRepository.Middleware;

/// <summary>
/// Service-to-service auth: GeekAPI presents <c>REPO_API_KEY</c> as <c>X-Repo-Key</c>.
/// </summary>
/// <remarks>
/// This handler is the receiving end of the only credential guarding these tables, so it refuses
/// rather than abstains. It used to return <see cref="AuthenticateResult.NoResult"/> for a missing
/// key, a wrong key, and an unset <c>REPO_API_KEY</c> alike — "this handler has no opinion", which
/// is not a rejection. Whether that became a 401 depended entirely on the endpoint carrying an
/// authorization policy, and a wrong key was indistinguishable from no key at all.
///
/// The expected key is now resolved once at startup (see
/// <c>RepositoryAuthExtensions.AddGeekRepositoryAuth</c>, which refuses to start without it) and
/// handed in through <see cref="RepoApiKeyAuthenticationOptions.ExpectedKey"/>. There is therefore
/// no request-time path on which the key is absent, and nothing to fall back to.
/// </remarks>
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
        // Exactly one header value. Several X-Repo-Key headers would otherwise join into one
        // comma-separated string, and the question of which one was meant has no good answer.
        if (!Request.Headers.TryGetValue(Header, out var provided) || provided.Count != 1)
        {
            return Task.FromResult(AuthenticateResult.Fail($"{Header} is missing."));
        }

        var providedKey = provided[0];
        if (string.IsNullOrEmpty(providedKey))
        {
            return Task.FromResult(AuthenticateResult.Fail($"{Header} is empty."));
        }

        // Fixed-time comparison so a caller cannot learn the key one character at a time from how
        // long the comparison takes. FixedTimeEquals returns false for differing lengths rather
        // than throwing, so the length check is its own answer and not a special case here.
        var providedBytes = Encoding.UTF8.GetBytes(providedKey);
        var expectedBytes = Encoding.UTF8.GetBytes(Options.ExpectedKey);
        if (!CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes))
        {
            // The message never repeats what was presented: it is written to logs the caller does
            // not control and would put a near-miss credential in them.
            return Task.FromResult(AuthenticateResult.Fail($"{Header} is not valid."));
        }

        var identity = new ClaimsIdentity(SchemeName);
        identity.AddClaim(new Claim("scope", "internal.api"));
        identity.AddClaim(new Claim(ClaimTypes.Name, "repo-api-key-bootstrap"));
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

public sealed class RepoApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    /// <summary>
    /// The key a caller must present. Resolved from <c>REPO_API_KEY</c> once at startup and never
    /// empty — the service refuses to start rather than run with an unset key.
    /// </summary>
    public string ExpectedKey { get; set; } = string.Empty;
}
