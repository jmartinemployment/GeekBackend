using GeekAPI.Services.Voice.Mcp;
using GeekAPI.Services.Voice.OwnerAuth;
using GeekAPI.Services.Voice;
using GeekAPI.Services.Voice.Storage;
using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.Voice;

/// <summary>
/// The owner's own check that the storage they connected still works (plans/client-and-app-plan.md, "Call logs and transcripts").
///
/// It asks for the tenant's live access token, which renews the token when it is close to expiring, so it exercises the one thing most
/// likely to have broken since: an owner who withdrew this application's access in their Google account learns it here rather than from
/// a call record that could not be written. It writes nothing, so checking leaves nothing behind in the owner's folder.
///
/// The tenant comes from the owner's tenant_id claim and a URL naming another tenant is refused (403).
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = ReceptionistAuth.Scheme)]
public sealed class StorageController(
    GoogleAccessTokens tokens,
    IVoiceStore store,
    IVoiceBillingStore billing,
    VoiceCredentialProtector protector,
    ILogger<StorageController> logger) : ControllerBase
{
    [HttpGet("api/v1/tenants/{tenantId:guid}/storage/check")]
    public async Task<IActionResult> Check(Guid tenantId, CancellationToken ct)
    {
        var refused = OwnerTenantClaim.Refuse(User, tenantId);
        if (refused is not null) return refused;

        var token = await tokens.ForAsync(tenantId, GoogleDriveOptions.Provider, McpFailureCodes.StorageNotConnected, ct);
        if (token.IsSuccess) return Ok(new { connected = true, provider = GoogleDriveOptions.Provider, folder = GoogleDriveOptions.FolderName });

        var error = string.IsNullOrWhiteSpace(token.Error) ? "The storage connection could not be checked." : token.Error;
        return StatusCode(StatusFor(token.ErrorCode), new { error, code = token.ErrorCode });
    }


    /// <summary>What the owner is asked for, and what the plan's setup page hands them to paste before they create the key.</summary>
    public sealed record BucketRequest(string? Provider, string? AccessKeyId, string? SecretAccessKey, string? Bucket, string? KeyPrefix, string? Region);

    /// <summary>
    /// The least this business must grant, built beside the writer so the two cannot drift. It is read before anything is saved,
    /// because the owner needs it to make the key in the first place.
    /// </summary>
    [HttpGet("api/v1/tenants/{tenantId:guid}/storage/policy")]
    public IActionResult Policy(Guid tenantId, [FromQuery] string provider, [FromQuery] string bucket, [FromQuery] string? keyPrefix)
    {
        var refused = OwnerTenantClaim.Refuse(User, tenantId);
        if (refused is not null) return refused;

        if (!VoiceStorageProviders.All.Contains(provider, StringComparer.Ordinal))
            return BadRequest(new { error = $"The provider must be one of: {string.Join(", ", VoiceStorageProviders.All)}.", code = VoiceFailureCodes.InvalidRequest });
        if (string.IsNullOrWhiteSpace(bucket))
            return BadRequest(new { error = "A bucket is required.", code = VoiceFailureCodes.InvalidRequest });

        return Ok(new { policy = StorageSetupPolicy.For(provider, bucket.Trim(), keyPrefix ?? string.Empty) });
    }

    /// <summary>
    /// Saves this business's bucket. The keys are sealed here and GeekRepository is handed ciphertext only. Object storage is a
    /// Standard-and-above ability, so a tenant on a lower tier is refused rather than quietly saved and never used.
    /// </summary>
    [HttpPut("api/v1/tenants/{tenantId:guid}/storage/bucket")]
    public async Task<IActionResult> SaveBucket(Guid tenantId, [FromBody] BucketRequest? request, CancellationToken ct)
    {
        var refused = OwnerTenantClaim.Refuse(User, tenantId);
        if (refused is not null) return refused;
        if (request is null) return BadRequest(new { error = "A storage connection is required.", code = VoiceFailureCodes.InvalidRequest });

        if (string.IsNullOrWhiteSpace(request.AccessKeyId) || string.IsNullOrWhiteSpace(request.SecretAccessKey))
            return BadRequest(new { error = "Both the access key and the secret are required.", code = VoiceFailureCodes.InvalidRequest });

        var standing = await billing.GetStandingAsync(tenantId, ct);
        if (!standing.IsSuccess)
            return StatusCode(StatusFor(standing.ErrorCode), new { error = standing.Error ?? "The subscription could not be read.", code = standing.ErrorCode });
        if (!VoiceSubscriptionTiers.WithObjectStorage.Contains(standing.Value!.TierCode, StringComparer.Ordinal))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = "Writing call records to your own bucket is part of Standard and above. Google Drive is available on every plan.",
                code = TierRequired,
            });
        }

        var keyId = protector.Protect(request.AccessKeyId, VoiceSecretPurposes.McpAccessToken);
        if (!keyId.IsSuccess) return StatusCode(StatusFor(keyId.ErrorCode), new { error = keyId.Error, code = keyId.ErrorCode });
        var secret = protector.Protect(request.SecretAccessKey, VoiceSecretPurposes.McpRefreshToken);
        if (!secret.IsSuccess) return StatusCode(StatusFor(secret.ErrorCode), new { error = secret.Error, code = secret.ErrorCode });

        var saved = await store.SaveStorageConnectionAsync(
            tenantId,
            new SaveVoiceStorageConnectionCommand(
                request.Provider ?? string.Empty, keyId.Value!, secret.Value!,
                (request.Bucket ?? string.Empty).Trim(), (request.KeyPrefix ?? string.Empty).Trim('/'), (request.Region ?? string.Empty).Trim()),
            ct);
        if (!saved.IsSuccess)
            return StatusCode(StatusFor(saved.ErrorCode), new { error = saved.Error ?? "The storage connection could not be saved.", code = saved.ErrorCode });

        logger.LogInformation("Tenant {TenantId} connected a {Provider} bucket for call records.", tenantId, saved.Value!.Provider);
        return Ok(new { connected = true, provider = saved.Value.Provider, bucket = saved.Value.Bucket, keyPrefix = saved.Value.KeyPrefix });
    }

    /// <summary>A tenant whose plan does not include writing to its own bucket.</summary>
    public const string TierRequired = "tier_required";

    /// <summary>The HTTP status a failure code is answered with. Anything unlisted is the service being unavailable.</summary>
    internal static int StatusFor(string? code) => code switch
    {
        VoiceFailureCodes.InvalidRequest => StatusCodes.Status400BadRequest,
        McpFailureCodes.StorageNotConnected or VoiceFailureCodes.NotFound => StatusCodes.Status404NotFound,
        McpFailureCodes.ReauthorizationRequired or VoiceFailureCodes.Conflict => StatusCodes.Status409Conflict,
        McpFailureCodes.StorageWriteFailed => StatusCodes.Status502BadGateway,
        _ => StatusCodes.Status503ServiceUnavailable,
    };
}
