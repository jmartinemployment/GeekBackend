using System.Text;
using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Drive;
using GeekAPI.Services.ContentCreatorV2.Gsc;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.ContentCreatorV2;

/// <summary>Content Creator–owned Google Drive connections for Knowledge ingest.</summary>
[ApiController]
[Route("api/geek-content-creator-v2/drive")]
public sealed class GccV2DriveController(
    ICurrentUserContext user,
    HttpGccV2Repository repo,
    GccV2GscSearchAnalyticsClient googleOAuth,
    GccV2DriveFilesClient driveFiles,
    GccV2DriveOAuthStateStore oauthState) : ControllerBase
{
    private static readonly string[] Scopes =
    [
        "https://www.googleapis.com/auth/drive.readonly",
        "https://www.googleapis.com/auth/userinfo.email",
        "https://www.googleapis.com/auth/userinfo.profile",
    ];

    private string Owner => user.UserId.ToString("D");

    [HttpGet("connections")]
    public async Task<ActionResult<object>> ListConnections(CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var rows = await repo.ListDriveConnectionsAsync(Owner, ct);
        return Ok(new
        {
            contractVersion = "gcc-drive-connections.v1",
            connections = rows.Select(Summary),
        });
    }

    /// <summary>
    /// Registers a Drive account for this owner. Live OAuth stores encrypted refresh tokens;
    /// stub connections (empty ciphertext) are allowed for local/e2e Knowledge ingest.
    /// </summary>
    [HttpPost("connections")]
    public async Task<ActionResult<object>> UpsertConnection(UpsertConnectionRequest request, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var accountLabel = (request.AccountLabel ?? "").Trim();
        if (string.IsNullOrWhiteSpace(accountLabel))
            return BadRequest(new { error = "accountLabel is required (Google email or stub label)." });

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
                    error = "GEEK_CC_GSC_ENCRYPTION_KEY is required to store live Drive refresh tokens.",
                });
            }
            (cipher, iv, tag) = GccV2GscCredentialProtector.Encrypt(request.RefreshToken.Trim());
            status = "connected";
        }

        var saved = await repo.UpsertDriveConnectionAsync(
            new UpsertGccV2DriveConnectionCommand(Owner, accountLabel, status, cipher, iv, tag), ct);
        return Ok(new
        {
            contractVersion = "gcc-drive-connections.v1",
            connection = Summary(saved),
        });
    }

    [HttpDelete("connections/{id:guid}")]
    public async Task<IActionResult> DeleteConnection(Guid id, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        await repo.DeleteDriveConnectionAsync(id, Owner, ct);
        return NoContent();
    }

    [HttpGet("oauth/connect-url")]
    public ActionResult<object> ConnectUrl([FromQuery] string? returnPath)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        if (!GccV2DriveOAuthEnv.IsConfigured)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                error =
                    "Drive Google OAuth is not configured. Set GEEK_CC_DRIVE_GOOGLE_REDIRECT_URI (and client id/secret or fall back to GSC Google client vars) plus GEEK_CC_GSC_ENCRYPTION_KEY.",
                mode = "stub",
            });
        }

        var safeReturn = SanitizeReturnPath(returnPath);
        var (state, expiresAt) = oauthState.Create(new GccV2DriveOAuthStatePayload(Owner, null, safeReturn));
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = GccV2DriveOAuthEnv.ClientId,
            ["redirect_uri"] = GccV2DriveOAuthEnv.RedirectUri,
            ["response_type"] = "code",
            ["access_type"] = "offline",
            ["prompt"] = "consent",
            ["state"] = state,
            ["scope"] = string.Join(' ', Scopes),
        };
        return Ok(new
        {
            contractVersion = "gcc-drive-oauth.v1",
            mode = "oauth",
            url = BuildUri("https://accounts.google.com/o/oauth2/v2/auth", query).ToString(),
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
            if (!GccV2DriveOAuthEnv.IsConfigured)
                throw new InvalidOperationException("Drive Google OAuth is not configured on this server.");

            var payload = oauthState.Consume(state);
            var token = await googleOAuth.ExchangeAuthorizationCodeAsync(
                code.Trim(),
                GccV2DriveOAuthEnv.ClientId,
                GccV2DriveOAuthEnv.ClientSecret,
                GccV2DriveOAuthEnv.RedirectUri,
                ct);
            if (string.IsNullOrWhiteSpace(token.RefreshToken))
            {
                throw new InvalidOperationException(
                    "Google did not provide a refresh token. Reconnect with consent.");
            }

            var (email, _) = await driveFiles.GetUserInfoAsync(token.AccessToken, ct);
            var accountLabel = string.IsNullOrWhiteSpace(payload.AccountLabel) ? email : payload.AccountLabel!;
            var (cipher, iv, tag) = GccV2GscCredentialProtector.Encrypt(token.RefreshToken);
            var saved = await repo.UpsertDriveConnectionAsync(
                new UpsertGccV2DriveConnectionCommand(payload.Owner, accountLabel, "connected", cipher, iv, tag), ct);

            return Redirect(BuildReturnRedirect(
                appBase, state, saved.Id, saved.AccountLabel, null, payload.ReturnPath));
        }
        catch (Exception ex)
        {
            var msg = ex.Message.Length > 200 ? ex.Message[..200] : ex.Message;
            return Redirect(BuildReturnRedirect(appBase, state, null, null, msg));
        }
    }

    private static object Summary(GccV2DriveConnectionDto connection) => new
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
        if (connectionId is { } id) query["driveConnectionId"] = id.ToString("D");
        if (!string.IsNullOrWhiteSpace(accountLabel)) query["driveAccount"] = accountLabel;
        if (!string.IsNullOrWhiteSpace(error)) query["driveError"] = error;
        var uri = BuildUri($"{appBase}{path}", query);
        return uri.ToString();
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
