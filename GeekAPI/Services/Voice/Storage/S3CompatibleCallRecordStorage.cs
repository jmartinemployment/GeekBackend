using System.Net;
using System.Net.Http.Headers;
using System.Text;
using GeekAPI.Services.Voice.Mcp;
using GeekApplication.Interfaces.Receptionist;
using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;

namespace GeekAPI.Services.Voice.Storage;

/// <summary>
/// Writes a finished call's record into the business's own bucket, on Amazon S3 or on Google Cloud Storage
/// (plans/client-and-app-plan.md, "AWS S3 / Google Cloud Storage (Standard &amp; Above)").
///
/// One implementation serves both because Google Cloud Storage speaks the S3 API, signed the same way, at its own endpoint; the setup
/// the plan describes hands out a key that may put an object and do nothing else, and this makes exactly that one request. There is no
/// code here that reads, lists or deletes, so a credential that somehow carried those rights would still never use them.
///
/// The keys are unsealed for the length of one request and never logged; a failure is its HTTP status and nothing more.
/// </summary>
public sealed class S3CompatibleCallRecordStorage(
    IVoiceStore store,
    VoiceCredentialProtector protector,
    IHttpClientFactory clients,
    TimeProvider clock,
    ILogger<S3CompatibleCallRecordStorage> logger) : ICallRecordStorage
{
    public const string HttpClientName = "object-storage";

    /// <summary>What the signature names the service as. Google Cloud Storage's S3 API is signed as s3 too.</summary>
    public const string SigningService = "s3";

    public string Provider => "object_storage";

    public async Task<Result<string>> WriteAsync(Guid tenantId, CallRecordObject record, CancellationToken ct = default)
    {
        var connection = await store.GetStorageConnectionAsync(tenantId, ct).ConfigureAwait(false);
        if (!connection.IsSuccess)
        {
            var code = connection.ErrorCode == VoiceFailureCodes.NotFound ? McpFailureCodes.StorageNotConnected : connection.ErrorCode;
            return Result<string>.Failure(connection.Error ?? "The storage connection could not be read.", code ?? VoiceFailureCodes.StoreUnavailable);
        }

        var keyId = protector.Unprotect(connection.Value!.AccessKeyIdEncrypted, VoiceSecretPurposes.McpAccessToken);
        if (!keyId.IsSuccess) return Result<string>.Failure(keyId.Error!, keyId.ErrorCode!);
        var secret = protector.Unprotect(connection.Value.SecretAccessKeyEncrypted, VoiceSecretPurposes.McpRefreshToken);
        if (!secret.IsSuccess) return Result<string>.Failure(secret.Error!, secret.ErrorCode!);

        return await PutAsync(connection.Value, keyId.Value!, secret.Value!, record, ct).ConfigureAwait(false);
    }

    /// <summary>The host a bucket is written to: Amazon's regional endpoint, or Google's S3-compatible one.</summary>
    internal static string HostFor(VoiceStorageConnection connection) =>
        connection.Provider == VoiceStorageProviders.GoogleCloudStorage
            ? "storage.googleapis.com"
            : $"{connection.Bucket}.s3.{connection.Region}.amazonaws.com";

    /// <summary>
    /// The object's key with a leading slash, under the business's own prefix. Google's endpoint is not bucket-scoped, so the bucket
    /// is the first part of the path there; Amazon's host already names the bucket.
    /// </summary>
    internal static string PathFor(VoiceStorageConnection connection, string objectPath)
    {
        var prefix = connection.KeyPrefix.Trim('/');
        var key = string.IsNullOrEmpty(prefix) ? objectPath : $"{prefix}/{objectPath}";
        return connection.Provider == VoiceStorageProviders.GoogleCloudStorage ? $"/{connection.Bucket}/{key}" : $"/{key}";
    }

    private async Task<Result<string>> PutAsync(
        VoiceStorageConnection connection, string keyId, string secret, CallRecordObject record, CancellationToken ct)
    {
        var host = HostFor(connection);
        var path = PathFor(connection, record.Path);
        var payload = Encoding.UTF8.GetBytes(record.Body);
        var now = clock.GetUtcNow();

        using var request = new HttpRequestMessage(HttpMethod.Put, $"https://{host}{AwsSignatureV4.CanonicalPath(path)}")
        {
            Content = new ByteArrayContent(payload),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(record.ContentType) { CharSet = "UTF-8" };
        request.Headers.TryAddWithoutValidation("x-amz-content-sha256", AwsSignatureV4.HashHex(payload));
        request.Headers.TryAddWithoutValidation("x-amz-date", AwsSignatureV4.AmzDate(now));
        request.Headers.TryAddWithoutValidation(
            "Authorization",
            AwsSignatureV4.AuthorizationHeader(keyId, secret, connection.Region, SigningService, host, path, payload, now));

        try
        {
            using var response = await clients.CreateClient(HttpClientName).SendAsync(request, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation("Wrote a call record to a tenant's {Provider} bucket.", connection.Provider);
                return Result<string>.Success(path);
            }

            // A key the bucket refuses is the owner's to replace; nothing here retries or tries another way in.
            var code = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                ? McpFailureCodes.ReauthorizationRequired
                : McpFailureCodes.StorageWriteFailed;
            logger.LogError("A tenant's {Provider} bucket refused a call record with {Status}.", connection.Provider, (int)response.StatusCode);
            return Result<string>.Failure("The bucket refused the call record.", code);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogError("A tenant's {Provider} bucket could not be reached ({Failure}).", connection.Provider, ex.GetType().Name);
            return Result<string>.Failure("The bucket could not be reached.", McpFailureCodes.StorageWriteFailed);
        }
    }
}
