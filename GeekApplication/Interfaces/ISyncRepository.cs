namespace GeekApplication.Interfaces;

public interface ISyncRepository
{
    Task<Result<SyncQueue>> EnqueueAsync(Guid userId, Guid targetDeviceId, string payload);
    Task<Result<SyncQueue>> FindByIdAsync(Guid queueId);
    Task<Result<List<SyncQueue>>> GetPendingAsync(Guid userId, Guid deviceId);
    Task<Result<bool>> MarkProcessedAsync(Guid queueId);
    Task<Result<bool>> MarkFailedAsync(Guid queueId, string errorMessage);
    Task<Result<SyncConflict>> LogConflictAsync(Guid userId, Guid deviceId, string fieldName, string expectedValue, string actualValue);
    Task<Result<List<SyncConflict>>> GetConflictsAsync(Guid userId);
    Task<Result<bool>> ResolveConflictAsync(Guid conflictId, string resolution);
}
