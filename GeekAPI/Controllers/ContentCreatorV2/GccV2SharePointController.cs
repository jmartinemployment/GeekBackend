using System.Text;
using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Gsc;
using GeekAPI.Services.ContentCreatorV2.SharePoint;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.ContentCreatorV2;

/// <summary>Content Creator–owned SharePoint/OneDrive connections for Knowledge ingest.</summary>
[ApiController]
[Route("api/geek-content-creator-v2/sharepoint")]
public sealed class GccV2SharePointController(
    ICurrentUserContext user,
    HttpGccV2Repository repo,
    GccV2SharePointGraphClient graph,
    GccV2SharePointOAuthStateStore oauthState) : ControllerBase
{
    private string Owner => user.UserId.ToString("D");

    [HttpGet("connections")]
    public async Task<ActionResult<object>> ListConnections(CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var rows = await repo.ListSharePointConnectionsAsync(Owner, ct);
        return Ok(new
        {
            contractVersion = "gcc-sharepoint-connections.v1",
            connections = rows.Select(Summary),
        });
    }

    [HttpPost("connections")]
    public async Task<ActionResult<object>> UpsertConnection(UpsertConnectionRequest request, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var accountLabel = (request.AccountLabel ?? "").Trim();
        if (string.IsNullOrWhiteSpace(accountLabel))
            return BadRequest(new { error = "accountLabel is required (Microsoft email or stub label)." });

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
                    error = "GEEK_CC_GSC_ENCRYPTION_KEY is required to store live SharePoint refresh tokens.",
                });
            }
            (cipher, iv, tag) = GccV2GscCredentialProtector.Encrypt(request.RefreshToken.Trim());
            status = "connected";
        }

        var saved = await repo.UpsertSharePointConnectionAsync(
            new UpsertGccV2SharePointConnectionCommand(Owner, accountLabel, status, cipher, iv, tag), ct);
        return Ok(new
        {
            contractVersion = "gcc-sharepoint-connections.v1",
            connection = Summary(saved),
        });
    }

    [HttpDelete("connections/{id:guid}")]
    public async Task<IActionResult> DeleteConnection(Guid id, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        await repo.DeleteSharePointConnectionAsync(id, Owner, ct);
        return NoContent();
    }

    [HttpGet("oauth/connect-url")]
    public ActionResult<object> ConnectUrl([FromQuery] string? returnPath)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        if (!GccV2SharePointOAuthEnv.IsConfigured)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                error =
                    "SharePoint Microsoft OAuth is not configured. Set GEEK_CC_SHAREPOINT_CLIENT_ID/SECRET, GEEK_CC_SHAREPOINT_REDIRECT_URI, and GEEK_CC_GSC_ENCRYPTION_KEY (optional GEEK_CC_SHAREPOINT_TENANT).",
                mode = "stub",
            });
        }

        var safeReturn = SanitizeReturnPath(returnPath);
        var (state, expiresAt) = oauthState.Create(new GccV2SharePointOAuthStatePayload(Owner, null, safeReturn));
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = GccV2SharePointOAuthEnv.ClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = GccV2SharePointOAuthEnv.RedirectUri,
            ["response_mode"] = "query",
            ["scope"] = string.Join(' ', GccV2SharePointGraphClient.Scopes),
            ["state"] = state,
            ["prompt"] = "consent",
        };
        return Ok(new
        {
            contractVersion = "gcc-sharepoint-oauth.v1",
            mode = "oauth",
            url = BuildUri(GccV2SharePointOAuthEnv.AuthorizeEndpoint, query).ToString(),
            expiresAt,
        });
    }

    [HttpGet("oauth/callback")]
    public async Task<IActionResult> OAuthCallback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        CancellationToken ct)
    {
        var appBase = ResolveAppBaseUrl();
        if (!string.IsNullOrWhiteSpace(error))
            return Redirect(BuildReturnRedirect(appBase, state, null, null, error));

        try
        {
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
                throw new InvalidOperationException("Missing authorization code or state.");
            if (!GccV2SharePointOAuthEnv.IsConfigured)
                throw new InvalidOperationException("SharePoint Microsoft OAuth is not configured on this server.");

            var payload = oauthState.Consume(state);
            var token = await graph.ExchangeAuthorizationCodeAsync(code.Trim(), ct);
            if (string.IsNullOrWhiteSpace(token.RefreshToken))
            {
                throw new InvalidOperationException(
                    "Microsoft did not provide a refresh token. Reconnect with consent.");
            }

            var (email, _) = await graph.GetUserInfoAsync(token.AccessToken, ct);
            var accountLabel = string.IsNullOrWhiteSpace(payload.AccountLabel) ? email : payload.AccountLabel!;
            var (cipher, iv, tag) = GccV2GscCredentialProtector.Encrypt(token.RefreshToken);
            var saved = await repo.UpsertSharePointConnectionAsync(
                new UpsertGccV2SharePointConnectionCommand(
                    payload.Owner, accountLabel, "connected", cipher, iv, tag), ct);

            return Redirect(BuildReturnRedirect(
                appBase, state, saved.Id, saved.AccountLabel, null, payload.ReturnPath));
        }
        catch (Exception ex)
        {
            var msg = ex.Message.Length > 200 ? ex.Message[..200] : ex.Message;
            return Redirect(BuildReturnRedirect(appBase, state, null, null, msg));
        }
    }

    private static object Summary(GccV2SharePointConnectionDto connection) => new
    {
        id = connection.Id,
        accountLabel = connection.AccountLabel,
        status = connection.Status,
        connectedAtUtc = connection.ConnectedAtUtc,
        updatedAtUtc = connection.UpdatedAtUtc,
    };

    private static string? SanitizeReturnPath(string? returnPath)
    {
        if (string.IsNullOrWhiteSpace(returnPath)) return "/brand-sources?tab=knowledge";
        var trimmed = returnPath.Trim();
        if (!trimmed.StartsWith('/') || trimmed.StartsWith("//", StringComparison.Ordinal))
            return "/brand-sources?tab=knowledge";
        return trimmed;
    }

    private static string ResolveAppBaseUrl()
    {
        var configured = (Environment.GetEnvironmentVariable("GEEK_CC_APP_BASE_URL") ?? "").Trim().TrimEnd('/');
        return string.IsNullOrWhiteSpace(configured) ? "http://localhost:3004" : configured;
    }

    private string BuildReturnRedirect(
        string appBase,
        string? state,
        Guid? connectionId,
        string? accountLabel,
        string? error,
        string? returnPath = null)
    {
        var path = returnPath;
        if (string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(state)
            && oauthState.TryPeek(state, out var payload))
        {
            path = payload?.ReturnPath;
        }

        path = SanitizeReturnPath(path);
        var query = new Dictionary<string, string?>();
        if (connectionId is { } id) query["sharePointConnectionId"] = id.ToString("D");
        if (!string.IsNullOrWhiteSpace(accountLabel)) query["sharePointAccount"] = accountLabel;
        if (!string.IsNullOrWhiteSpace(error)) query["sharePointError"] = error;
        return BuildUri($"{appBase}{path}", query).ToString();
    }

    private static Uri BuildUri(string baseUrl, IReadOnlyDictionary<string, string?> query)
    {
        var builder = new StringBuilder(baseUrl);
        var first = !baseUrl.Contains('?', StringComparison.Ordinal);
        foreach (var (key, value) in query)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            builder.Append(first ? '?' : '&');
            first = false;
            builder.Append(Uri.EscapeDataString(key));
            builder.Append('=');
            builder.Append(Uri.EscapeDataString(value));
        }

        return new Uri(builder.ToString());
    }

    public sealed record UpsertConnectionRequest(string? AccountLabel, string? RefreshToken);
}
