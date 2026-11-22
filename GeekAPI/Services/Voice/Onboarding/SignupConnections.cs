using System.Text.Json;
using GeekAPI.Services.Voice.Mcp;
using GeekAPI.Services.Voice.Storage;
using GeekApplication.Interfaces.Receptionist;
using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;

namespace GeekAPI.Services.Voice.Onboarding;

/// <summary>The portal asking for the address a visitor is sent to approve a connection for their business.</summary>
public sealed record BeginSignupConnectionRequest(Guid? TenantId, string? Provider);

/// <summary>The approval Google sent back, handed on by the portal.</summary>
public sealed record CompleteSignupConnectionRequest(Guid? TenantId, string? Provider, string? Code, string? State, string? Error);

/// <summary>The portal asking for a connection to be shown to work.</summary>
public sealed record CheckSignupConnectionRequest(Guid? TenantId, string? Provider);

/// <summary>The bucket a visitor typed in. The keys are sealed on arrival and never answered back.</summary>
public sealed record SaveSignupBucketRequest(Guid? TenantId, string? Provider, string? Bucket, string? Region, string? AccessKeyId, string? SecretAccessKey, string? KeyPrefix);

/// <summary>
/// Connecting a business's calendar, Drive folder or bucket during /signup, before it has paid, and showing that the connection works.
///
/// <para>
/// The owner's own routes need a sign-in token and an active subscription, and neither exists yet at this point of the signup. These
/// are called by the portal's server with the onboarding key, for a business that is still waiting for payment and for nothing else:
/// an active, suspended, cancelled or unknown business is refused, so this can never overwrite a paying business's connection. The
/// business is named by the portal, which is the same trust every other onboarding route places in it.
/// </para>
///
/// <para>
/// A connection is shown to work by using it, not by being told it was saved: the calendar is asked for its calendars, and a Drive
/// folder or a bucket is written one small file, which its owner can delete. A check that fails says what failed.
/// </para>
/// </summary>
public sealed class SignupConnections(
    IVoiceStore store,
    McpConnectionService connections,
    McpTools calendar,
    GoogleDriveCallRecordStorage drive,
    S3CompatibleCallRecordStorage bucket,
    VoiceCredentialProtector protector,
    TimeProvider clock,
    ILogger<SignupConnections> logger)
{
    /// <summary>The providers a visitor may connect through Google's approval screen during signup.</summary>
    private static readonly string[] GoogleProviders = [GoogleCalendarOptions.Provider, GoogleDriveOptions.Provider];

    public const string CheckFileName = "geek-receptionist-connection-check.txt";

    public async Task<Result<string>> BeginAsync(BeginSignupConnectionRequest? request, CancellationToken ct = default)
    {
        var pending = await PendingAsync(request?.TenantId, ct).ConfigureAwait(false);
        if (!pending.IsSuccess) return Result<string>.Failure(pending.Error!, pending.ErrorCode!);
        var provider = GoogleProviderOf(request?.Provider);
        if (!provider.IsSuccess) return Result<string>.Failure(provider.Error!, provider.ErrorCode!);

        return await connections.BeginAsync(pending.Value, provider.Value!, allowWaitingForPayment: true, ct).ConfigureAwait(false);
    }

    public async Task<Result<Guid>> CompleteAsync(CompleteSignupConnectionRequest? request, CancellationToken ct = default)
    {
        var pending = await PendingAsync(request?.TenantId, ct).ConfigureAwait(false);
        if (!pending.IsSuccess) return Result<Guid>.Failure(pending.Error!, pending.ErrorCode!);
        var provider = GoogleProviderOf(request?.Provider);
        if (!provider.IsSuccess) return Result<Guid>.Failure(provider.Error!, provider.ErrorCode!);

        return await connections.CompleteAsync(pending.Value, provider.Value!, request!.Code, request.State, request.Error, ct).ConfigureAwait(false);
    }

    /// <summary>Uses the connection: the calendar lists its calendars, a Drive folder or a bucket is written one small file.</summary>
    public async Task<Result<string>> CheckAsync(Guid? tenantId, string? provider, CancellationToken ct = default)
    {
        var pending = await PendingAsync(tenantId, ct).ConfigureAwait(false);
        if (!pending.IsSuccess) return Result<string>.Failure(pending.Error!, pending.ErrorCode!);

        switch (provider)
        {
            case GoogleCalendarOptions.Provider:
            {
                var listed = await calendar.CallAsync(pending.Value, GoogleCalendarOptions.Provider, "list_calendars", null, ct).ConfigureAwait(false);
                return listed.IsSuccess
                    ? Result<string>.Success("The calendar answered.")
                    : Result<string>.Failure(listed.Error ?? "The calendar could not be checked.", listed.ErrorCode ?? VoiceFailureCodes.StoreUnavailable);
            }
            case GoogleDriveOptions.Provider:
                return await WriteCheckFileAsync(drive, pending.Value, ct).ConfigureAwait(false);
            case VoiceStorageProviders.AwsS3:
                return await WriteCheckFileAsync(bucket, pending.Value, ct).ConfigureAwait(false);
            default:
                return Result<string>.Failure("That connection cannot be checked.", VoiceFailureCodes.InvalidRequest);
        }
    }

    public async Task<Result<string>> SaveBucketAsync(SaveSignupBucketRequest? request, CancellationToken ct = default)
    {
        var pending = await PendingAsync(request?.TenantId, ct).ConfigureAwait(false);
        if (!pending.IsSuccess) return Result<string>.Failure(pending.Error!, pending.ErrorCode!);

        if (request is null || !string.Equals(request.Provider, VoiceStorageProviders.AwsS3, StringComparison.Ordinal))
            return Result<string>.Failure($"The provider must be {VoiceStorageProviders.AwsS3}.", VoiceFailureCodes.InvalidRequest);
        if (string.IsNullOrWhiteSpace(request.Bucket) || string.IsNullOrWhiteSpace(request.Region))
            return Result<string>.Failure("A bucket and a region are required.", VoiceFailureCodes.InvalidRequest);
        if (string.IsNullOrWhiteSpace(request.AccessKeyId) || string.IsNullOrWhiteSpace(request.SecretAccessKey))
            return Result<string>.Failure("Both the access key and the secret are required.", VoiceFailureCodes.InvalidRequest);

        var keyId = protector.Protect(request.AccessKeyId, VoiceSecretPurposes.StorageAccessKeyId);
        if (!keyId.IsSuccess) return Result<string>.Failure(keyId.Error!, keyId.ErrorCode!);
        var secret = protector.Protect(request.SecretAccessKey, VoiceSecretPurposes.StorageSecretAccessKey);
        if (!secret.IsSuccess) return Result<string>.Failure(secret.Error!, secret.ErrorCode!);

        var saved = await store.SaveStorageConnectionAsync(
            pending.Value,
            new SaveVoiceStorageConnectionCommand(
                request.Provider, keyId.Value!, secret.Value!, request.Bucket.Trim(), (request.KeyPrefix ?? string.Empty).Trim('/'), request.Region.Trim()),
            ct).ConfigureAwait(false);
        if (!saved.IsSuccess)
            return Result<string>.Failure(saved.Error ?? "The storage connection could not be saved.", saved.ErrorCode ?? VoiceFailureCodes.StoreUnavailable);

        logger.LogInformation("Tenant {TenantId} connected a bucket during signup.", pending.Value);
        return Result<string>.Success(saved.Value!.Bucket);
    }

    private async Task<Result<string>> WriteCheckFileAsync(ICallRecordStorage storage, Guid tenantId, CancellationToken ct)
    {
        var body = $"Geek-Receptionist wrote this file on {clock.GetUtcNow():yyyy-MM-dd} to confirm that your connection works. You can delete it.";
        var written = await storage.WriteAsync(tenantId, new CallRecordObject(CheckFileName, body, "text/plain"), ct).ConfigureAwait(false);
        return written.IsSuccess
            ? Result<string>.Success("A test file was written.")
            : Result<string>.Failure(written.Error ?? "The test file could not be written.", written.ErrorCode ?? McpFailureCodes.StorageWriteFailed);
    }

    private static Result<string> GoogleProviderOf(string? provider) =>
        provider is not null && GoogleProviders.Contains(provider, StringComparer.Ordinal)
            ? Result<string>.Success(provider)
            : Result<string>.Failure($"The connections that can be made during signup are: {string.Join(", ", GoogleProviders)}.", McpFailureCodes.ProviderNotSupported);

    /// <summary>The tenant, only when it exists and is still waiting for payment.</summary>
    private async Task<Result<Guid>> PendingAsync(Guid? tenantId, CancellationToken ct)
    {
        if (tenantId is null || tenantId == Guid.Empty)
            return Result<Guid>.Failure("A business is required.", VoiceFailureCodes.InvalidRequest);

        var tenant = await store.GetTenantAsync(tenantId.Value, ct).ConfigureAwait(false);
        if (!tenant.IsSuccess)
            return Result<Guid>.Failure(tenant.Error ?? "The business could not be read.", tenant.ErrorCode ?? VoiceFailureCodes.StoreUnavailable);
        if (tenant.Value!.Status != VoiceTenantStatuses.PendingPayment)
            return Result<Guid>.Failure("A connection can be made during signup only while the business is waiting for payment.", VoiceFailureCodes.Conflict);

        return Result<Guid>.Success(tenantId.Value);
    }
}
