using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;

namespace GeekAPI.Services.Voice.Onboarding;

/// <summary>Why creating the owner's login was refused. The controller turns the code into a status; the wording is for people.</summary>
public static class OwnerLoginFailureCodes
{
    public const string TenantNotActive = "tenant_not_active";
    public const string OwnerExists = "owner_exists";
    public const string CredentialsRefused = "credentials_refused";
    public const string NotConfigured = "owner_login_not_configured";
    public const string AuthUnavailable = "auth_unavailable";
}

/// <summary>
/// What the last step of /signup sends once the subscription is active (docs/Geek-Receptionist.md section 5, item 4). Every field is
/// nullable so a field that was left out is told apart from one that was sent empty.
/// </summary>
public sealed record CreateOwnerLoginRequest(Guid? TenantId, string? Email, string? Password, string? DisplayName);

/// <summary>The owner's login now exists. The owner signs in at GeekOAuth, and every token issued to them carries <c>tenant_id</c>.</summary>
public sealed record OwnerLoginCreated(Guid TenantId, string UserId);

/// <summary>
/// Creates the login of a tenant's one owner in GeekOAuth, with the tenant on it so the owner's tokens carry the tenant_id claim every
/// <c>/api/v1/tenants/*</c> route checks. Only a tenant whose subscription is active gets one, so a signup that never paid has no login.
/// GeekOAuth is asked once, with GeekAPI's client-credentials token (GeekOAuthTokenHandler, CLIENT_SECRET_GEEKAPI); nothing here retries,
/// and the password goes to GeekOAuth in the request body and is never logged, stored or returned. A failure the caller can act on (a
/// password GeekOAuth refuses, an owner that exists) keeps its own code; every other GeekOAuth failure is the service being unavailable.
/// </summary>
public sealed partial class OwnerLoginProvisioner(IVoiceStore store, IHttpClientFactory clients, IConfiguration configuration, ILogger<OwnerLoginProvisioner> logger)
{
    public const string ClientName = "GeekOAuthOwners";
    internal const string DefaultAuthUrl = "https://auth.geekatyourspot.com";
    private const string SecretVariable = "CLIENT_SECRET_GEEKAPI";

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex EmailShape();

    public async Task<Result<OwnerLoginCreated>> CreateAsync(CreateOwnerLoginRequest? request, CancellationToken ct = default)
    {
        if (request?.TenantId is not { } tenantId || tenantId == Guid.Empty)
            return Fail("A tenant is required.", VoiceFailureCodes.InvalidRequest);
        if (string.IsNullOrWhiteSpace(request.Email) || !EmailShape().IsMatch(request.Email.Trim()))
            return Fail("A valid email is required.", VoiceFailureCodes.InvalidRequest);
        if (string.IsNullOrEmpty(request.Password) || request.Password.Length < 8)
            return Fail("A password of at least 8 characters is required.", VoiceFailureCodes.InvalidRequest);

        if (string.IsNullOrWhiteSpace(configuration[SecretVariable]))
        {
            logger.LogError("Creating an owner login is not possible: {Variable} is not set.", SecretVariable);
            return Result<OwnerLoginCreated>.Failure("Owner login is not configured.", OwnerLoginFailureCodes.NotConfigured);
        }

        var tenant = await store.GetTenantAsync(tenantId, ct).ConfigureAwait(false);
        if (!tenant.IsSuccess)
            return Result<OwnerLoginCreated>.Failure(tenant.Error ?? "The tenant could not be read.", tenant.ErrorCode ?? VoiceFailureCodes.StoreUnavailable);
        if (tenant.Value!.Status != VoiceTenantStatuses.Active)
            return Fail("An owner login can be created only after the subscription is active.", OwnerLoginFailureCodes.TenantNotActive);

        try
        {
            var client = clients.CreateClient(ClientName);
            using var response = await client.PostAsJsonAsync(
                "api/internal/tenant-owners",
                new { tenantId, email = request.Email.Trim(), password = request.Password, displayName = request.DisplayName },
                ct).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.Created)
            {
                var created = await ReadAsync(response, ct).ConfigureAwait(false);
                if (created.UserId is null)
                {
                    logger.LogError("GeekOAuth created the owner of tenant {TenantId} but its answer carried no user id.", tenantId);
                    return Result<OwnerLoginCreated>.Failure("The owner login answer could not be read.", OwnerLoginFailureCodes.AuthUnavailable);
                }
                logger.LogInformation("Owner login created for tenant {TenantId}.", tenantId);
                return Result<OwnerLoginCreated>.Success(new OwnerLoginCreated(tenantId, created.UserId));
            }

            if (response.StatusCode == HttpStatusCode.Conflict)
                return Fail("That tenant already has an owner, or that email already has a login.", OwnerLoginFailureCodes.OwnerExists);

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                var refused = await ReadAsync(response, ct).ConfigureAwait(false);
                return Fail(string.IsNullOrWhiteSpace(refused.Error) ? "The login details were refused." : refused.Error, OwnerLoginFailureCodes.CredentialsRefused);
            }

            logger.LogError("GeekOAuth refused to create the owner of tenant {TenantId} with {Status}.", tenantId, (int)response.StatusCode);
            return Result<OwnerLoginCreated>.Failure("The login service refused the request.", OwnerLoginFailureCodes.AuthUnavailable);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogError(ex, "GeekOAuth could not be reached to create the owner of tenant {TenantId}.", tenantId);
            return Result<OwnerLoginCreated>.Failure("The login service could not be reached.", OwnerLoginFailureCodes.AuthUnavailable);
        }
    }

    private static async Task<(string? UserId, string? Error)> ReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct).ConfigureAwait(false);
            var root = doc.RootElement;
            string? Text(string name) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            return (Text("userId"), Text("error"));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private Result<OwnerLoginCreated> Fail(string error, string code)
    {
        logger.LogWarning("Creating an owner login was refused with {Code}.", code);
        return Result<OwnerLoginCreated>.Failure(error, code);
    }
}
