using System.Text;
using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Gsc;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.ContentCreatorV2;

/// <summary>Content Creator–owned Google Search Console connections (not Geek SEO projects).</summary>
[ApiController]
[Route("api/geek-content-creator-v2/gsc")]
public sealed class GccV2GscController(
    ICurrentUserContext user,
    HttpGccV2Repository repo,
    GccV2GscSearchAnalyticsClient searchAnalytics,
    GccV2GscOAuthStateStore oauthState) : ControllerBase
{
    private static readonly string[] Scopes =
    [
        "https://www.googleapis.com/auth/webmasters.readonly",
    ];

    private string Owner => user.UserId.ToString("D");

    [HttpGet("connections")]
    public async Task<ActionResult<object>> ListConnections(CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var rows = await repo.ListGscConnectionsAsync(Owner, ct);
        return Ok(new
        {
            contractVersion = "gcc-gsc-connections.v1",
            connections = rows.Select(Summary),
        });
    }

    /// <summary>
    /// Registers a verified GSC property for this owner. Live OAuth callback stores encrypted tokens;
    /// stub connections (empty ciphertext) are allowed so Query Planner can use CC-owned connection IDs
    /// without calling Geek SEO.
    /// </summary>
    [HttpPost("connections")]
    public async Task<ActionResult<object>> UpsertConnection(UpsertConnectionRequest request, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var siteUrl = (request.SiteUrl ?? "").Trim();
        if (string.IsNullOrWhiteSpace(siteUrl))
            return BadRequest(new { error = "siteUrl is required (e.g. sc-domain:example.com)." });

        byte[] cipher = [];
        byte[] iv = [];
        byte[] tag = [];
        var status = "stub";
        if (!string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            if (!GccV2GscCredentialProtector.IsConfigured())
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                {
                    error = "GEEK_CC_GSC_ENCRYPTION_KEY is required to store live GSC refresh tokens.",
                });
            }
            (cipher, iv, tag) = GccV2GscCredentialProtector.Encrypt(request.RefreshToken.Trim());
            status = "connected";
        }

        var saved = await repo.UpsertGscConnectionAsync(
            new UpsertGccV2GscConnectionCommand(Owner, siteUrl, status, cipher, iv, tag), ct);
        return Ok(new
        {
            contractVersion = "gcc-gsc-connections.v1",
            connection = Summary(saved),
        });
    }

    [HttpDelete("connections/{id:guid}")]
    public async Task<IActionResult> DeleteConnection(Guid id, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        await repo.DeleteGscConnectionAsync(id, Owner, ct);
        return NoContent();
    }

    /// <summary>
    /// Starts Google OAuth for a CC-owned GSC connection. Returns 503 when Google env is unset
    /// so the UI can fall back to a stub connection in local/e2e.
    /// </summary>
    [HttpGet("oauth/connect-url")]
    public ActionResult<object> ConnectUrl(
        [FromQuery] string? siteUrl,
        [FromQuery] string? returnPath)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        if (!GccV2GscOAuthEnv.IsConfigured)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                error =
                    "GSC Google OAuth is not configured. Set GEEK_CC_GSC_GOOGLE_CLIENT_ID/SECRET, GEEK_CC_GSC_GOOGLE_REDIRECT_URI, and GEEK_CC_GSC_ENCRYPTION_KEY.",
                mode = "stub",
            });
        }

        var safeReturn = SanitizeReturnPath(returnPath);
        var (state, expiresAt) = oauthState.Create(new GccV2GscOAuthStatePayload(Owner, siteUrl, safeReturn));
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = GccV2GscOAuthEnv.ClientId,
            ["redirect_uri"] = GccV2GscOAuthEnv.RedirectUri,
            ["response_type"] = "code",
            ["access_type"] = "offline",
            ["prompt"] = "consent",
            ["state"] = state,
            ["scope"] = string.Join(' ', Scopes),
        };
        return Ok(new
        {
            contractVersion = "gcc-gsc-oauth.v1",
            mode = "oauth",
            url = BuildUri("https://accounts.google.com/o/oauth2/v2/auth", query).ToString(),
            expiresAt,
        });
    }

    /// <summary>Google OAuth redirect target — public path; ownership is enforced via signed state.</summary>
    [HttpGet("oauth/callback")]
    public async Task<IActionResult> OAuthCallback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        CancellationToken ct)
    {
        var appBase = ResolveAppBaseUrl();
        if (!string.IsNullOrWhiteSpace(error))
        {
            return Redirect(BuildReturnRedirect(appBase, state, null, null, error));
        }

        try
        {
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
                throw new InvalidOperationException("Missing authorization code or state.");
            if (!GccV2GscOAuthEnv.IsConfigured)
                throw new InvalidOperationException("GSC Google OAuth is not configured on this server.");

            var payload = oauthState.Consume(state);
            var token = await searchAnalytics.ExchangeAuthorizationCodeAsync(
                code.Trim(),
                GccV2GscOAuthEnv.ClientId,
                GccV2GscOAuthEnv.ClientSecret,
                GccV2GscOAuthEnv.RedirectUri,
                ct);
            if (string.IsNullOrWhiteSpace(token.RefreshToken))
            {
                throw new InvalidOperationException(
                    "Google did not provide a refresh token. Reconnect with consent.");
            }

            var accessibleSites = await searchAnalytics.ListAccessibleSiteUrlsAsync(token.AccessToken, ct);
            var siteUrl = GccV2GscSiteUrlMatcher.Match(accessibleSites, payload.SiteUrl);
            if (string.IsNullOrWhiteSpace(siteUrl))
            {
                throw new InvalidOperationException(
                    accessibleSites.Count == 0
                        ? "No Search Console property was returned for this Google account. Verify the site in Search Console, then reconnect."
                        : "No Search Console property matches the requested site URL. Add the site in Search Console, then reconnect.");
            }

            var (cipher, iv, tag) = GccV2GscCredentialProtector.Encrypt(token.RefreshToken);
            var saved = await repo.UpsertGscConnectionAsync(
                new UpsertGccV2GscConnectionCommand(payload.Owner, siteUrl, "connected", cipher, iv, tag), ct);

            return Redirect(BuildReturnRedirect(
                appBase,
                state,
                saved.Id,
                saved.SiteUrl,
                null,
                payload.ReturnPath));
        }
        catch (Exception ex)
        {
            var msg = ex.Message.Length > 200 ? ex.Message[..200] : ex.Message;
            return Redirect(BuildReturnRedirect(appBase, state, null, null, msg));
        }
    }

    [HttpGet("connections/{id:guid}/observed-queries")]
    public async Task<ActionResult<object>> ObservedQueries(
        Guid id,
        [FromQuery] DateOnly? startDate,
        [FromQuery] DateOnly? endDate,
        [FromQuery] int? rowLimit,
        CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var connection = await repo.GetGscConnectionAsync(id, Owner, ct);
        if (connection is null) return NotFound(new { error = "GSC connection not found." });

        var end = endDate ?? DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var start = startDate ?? end.AddDays(-90);
        var limit = rowLimit is null or < 1 ? 200 : Math.Min(rowLimit.Value, 1000);
        var fetchedAt = DateTimeOffset.UtcNow;
        var sourceId = $"gsc:{connection.Id:D}:{start:yyyy-MM-dd}:{end:yyyy-MM-dd}";

        IReadOnlyList<string> queries;
        if (connection.Status == "stub"
            || connection.EncryptedRefreshToken.Length == 0)
        {
            // Stub connections do not call Google; operators still get CC-owned provenance ids.
            queries = [];
        }
        else
        {
            if (!GccV2GscOAuthEnv.IsConfigured
                || string.IsNullOrWhiteSpace(GccV2GscOAuthEnv.ClientId)
                || string.IsNullOrWhiteSpace(GccV2GscOAuthEnv.ClientSecret))
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                {
                    error = "GEEK_CC_GSC_GOOGLE_CLIENT_ID/SECRET are required for live Search Console fetch.",
                });
            }

            try
            {
                var refresh = GccV2GscCredentialProtector.Decrypt(
                    connection.EncryptedRefreshToken,
                    connection.EncryptionIv,
                    connection.EncryptionTag);
                var access = await searchAnalytics.ExchangeRefreshTokenAsync(
                    refresh, GccV2GscOAuthEnv.ClientId, GccV2GscOAuthEnv.ClientSecret, ct);
                var rows = await searchAnalytics.QueryAsync(
                    access, connection.SiteUrl, start, end, limit, ct);
                queries = rows.Select(x => x.Query).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new { error = ex.Message });
            }
        }

        return Ok(new
        {
            contractVersion = "gcc-query-planner-observed.v1",
            connectionId = connection.Id,
            siteUrl = connection.SiteUrl,
            startDate = start,
            endDate = end,
            fetchedAtUtc = fetchedAt,
            source = new
            {
                sourceId,
                kind = "google-search-console",
                label = connection.SiteUrl,
                siteUrl = connection.SiteUrl,
                connectionId = connection.Id,
            },
            demandDisclaimer =
                "Observed GSC queries are first-party search analytics, not traffic, volume, ranking, or demand scores for planning heuristics.",
            queries = queries.Select(query => new
            {
                query,
                origin = "observed",
                sourceId,
                observedAtUtc = fetchedAt.ToString("O"),
            }),
            queryCount = queries.Count,
        });
    }

    private static object Summary(GccV2GscConnectionDto connection) => new
    {
        id = connection.Id,
        siteUrl = connection.SiteUrl,
        status = connection.Status,
        connectedAtUtc = connection.ConnectedAtUtc,
    };

    private string BuildReturnRedirect(
        string appBase,
        string? state,
        Guid? connectionId,
        string? siteUrl,
        string? error,
        string? returnPathOverride = null)
    {
        var returnPath = "/task-agents/query-planner";
        if (!string.IsNullOrWhiteSpace(returnPathOverride))
            returnPath = SanitizeReturnPath(returnPathOverride) ?? returnPath;
        else if (oauthState.TryPeek(state ?? string.Empty, out var payload)
                 && !string.IsNullOrWhiteSpace(payload?.ReturnPath))
        {
            returnPath = SanitizeReturnPath(payload.ReturnPath) ?? returnPath;
        }

        var qs = new List<string>();
        if (!string.IsNullOrWhiteSpace(error))
        {
            qs.Add("gsc=error");
            qs.Add($"message={Uri.EscapeDataString(error)}");
        }
        else
        {
            qs.Add("gsc=connected");
            if (connectionId.HasValue)
                qs.Add($"connectionId={Uri.EscapeDataString(connectionId.Value.ToString("D"))}");
            if (!string.IsNullOrWhiteSpace(siteUrl))
                qs.Add($"siteUrl={Uri.EscapeDataString(siteUrl)}");
        }

        return $"{appBase}{returnPath}?{string.Join('&', qs)}";
    }

    private static string? SanitizeReturnPath(string? returnPath)
    {
        if (string.IsNullOrWhiteSpace(returnPath))
            return null;
        var trimmed = returnPath.Trim();
        if (!trimmed.StartsWith('/') || trimmed.StartsWith("//", StringComparison.Ordinal))
            return null;
        if (trimmed.Contains('\\', StringComparison.Ordinal) || trimmed.Contains("://", StringComparison.Ordinal))
            return null;
        return trimmed;
    }

    private static string ResolveAppBaseUrl()
    {
        var configured = Environment.GetEnvironmentVariable("GEEK_CC_APP_URL");
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.Trim().TrimEnd('/');

        var cors = Environment.GetEnvironmentVariable("CORS_ORIGINS");
        var first = cors?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(first))
            return first.TrimEnd('/');

        return "http://localhost:3004";
    }

    private static Uri BuildUri(string baseUrl, IReadOnlyDictionary<string, string?> query)
    {
        var sb = new StringBuilder(baseUrl);
        sb.Append('?');
        var first = true;
        foreach (var pair in query)
        {
            if (pair.Value is null)
                continue;
            if (!first)
                sb.Append('&');
            first = false;
            sb.Append(Uri.EscapeDataString(pair.Key));
            sb.Append('=');
            sb.Append(Uri.EscapeDataString(pair.Value));
        }

        return new Uri(sb.ToString(), UriKind.Absolute);
    }

    public sealed record UpsertConnectionRequest(string SiteUrl, string? RefreshToken = null);
}
