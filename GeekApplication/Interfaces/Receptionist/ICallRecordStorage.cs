using GeekApplication.Results;

namespace GeekApplication.Interfaces.Receptionist;

/// <summary>
/// One finished call's record as it is written into a business's own storage: the path it goes to, what it holds, and the media type
/// of that body. Built in memory when a call ends and let go once it is written, like everything else about a call.
/// </summary>
public sealed record CallRecordObject(string Path, string Body, string ContentType);

/// <summary>
/// Somewhere a business has its own call records written: its Google Drive folder, its S3 or GCS bucket prefix
/// (plans/client-and-app-plan.md, "Call logs and transcripts (BYOS)"). Write-only by design, so there is nothing here that reads,
/// lists or deletes, and a business's records are not this platform's to go back and look at.
///
/// An implementation returns a failure rather than throwing for the ordinary refusals — a revoked token, a full quota, a bucket that
/// no longer takes writes — so the caller can fall back to the owner's email instead of losing the call.
/// </summary>
public interface ICallRecordStorage
{
    /// <summary>The provider this writes to, as the tenant's connection names it.</summary>
    string Provider { get; }

    /// <summary>Writes one record, answering with the provider's own id for what it wrote.</summary>
    Task<Result<string>> WriteAsync(Guid tenantId, CallRecordObject record, CancellationToken ct = default);
}
