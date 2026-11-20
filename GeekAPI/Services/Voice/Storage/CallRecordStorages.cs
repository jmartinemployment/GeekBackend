using GeekAPI.Services.Voice.Mcp;
using GeekApplication.Interfaces.Receptionist;
using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;

namespace GeekAPI.Services.Voice.Storage;

/// <summary>
/// Which storage a business's call records go to, if any. A bucket is looked for first, then a Drive folder, and a business that has
/// connected neither gets null, which is not a failure: its records are emailed, as they were before any of this existed.
///
/// A bucket wins over a Drive folder because it is the deliberate, tier-gated choice of the two: a business on Standard that set up a
/// bucket meant its records to go there. Nothing writes to both; one call makes one record.
/// </summary>
public sealed class CallRecordStorages(
    IVoiceStore store,
    S3CompatibleCallRecordStorage buckets,
    GoogleDriveCallRecordStorage drive,
    ILogger<CallRecordStorages> logger)
{
    public async Task<ICallRecordStorage?> ForAsync(Guid tenantId, CancellationToken ct = default)
    {
        var bucket = await store.GetStorageConnectionAsync(tenantId, ct).ConfigureAwait(false);
        if (bucket.IsSuccess) return buckets;
        if (bucket.ErrorCode != VoiceFailureCodes.NotFound)
        {
            // Not "no storage": the store could not say. Said out loud, and the call's record goes to email rather than nowhere.
            logger.LogError("Tenant {TenantId}: the storage connection could not be read ({Code}); this call's record goes to email.", tenantId, bucket.ErrorCode);
            return null;
        }

        var folder = await store.GetMcpConnectionAsync(tenantId, GoogleDriveOptions.Provider, ct).ConfigureAwait(false);
        if (folder.IsSuccess && folder.Value!.IsActive) return drive;
        if (!folder.IsSuccess && folder.ErrorCode != VoiceFailureCodes.NotFound)
            logger.LogError("Tenant {TenantId}: the Drive connection could not be read ({Code}); this call's record goes to email.", tenantId, folder.ErrorCode);

        return null;
    }
}
