using System.Data;
using Dapper;
using GeekApplication.Entities;
using GeekApplication.Interfaces;
using GeekApplication.Results;
using GeekRepository.Infrastructure;

namespace GeekRepository.Repositories;

public sealed class SyncRepository : ISyncRepository
{
	private readonly IDbConnectionFactory _db;

	public SyncRepository(IDbConnectionFactory db) => _db = db;

	public async Task<Result<SyncQueue>> EnqueueAsync(Guid userId, Guid targetDeviceId, string payload)
	{
		try
		{
			const string sql = """
				INSERT INTO sync_queue (id, user_id, target_device_id, payload, status)
				VALUES (@id, @userId, @targetDeviceId, @payload, 'pending')
				RETURNING id, user_id as UserId, target_device_id as TargetDeviceId,
						  payload, status, created_at as CreatedAt
				""";

			using var conn = _db.CreateConnection();
			var queueId = Guid.NewGuid();
			var item = await conn.QueryFirstOrDefaultAsync<SyncQueue>(sql, new
			{
				id = queueId,
				userId,
				targetDeviceId,
				payload
			});

			return item != null
				? Result<SyncQueue>.Success(item)
				: Result<SyncQueue>.Failure("Failed to enqueue");
		}
		catch (Exception ex)
		{
			return Result<SyncQueue>.Failure($"Enqueue failed: {ex.Message}");
		}
	}

	public async Task<Result<SyncQueue>> FindByIdAsync(Guid queueId)
	{
		try
		{
			const string sql = """
				SELECT id, user_id as UserId, target_device_id as TargetDeviceId,
					   payload, status, created_at as CreatedAt
				FROM sync_queue
				WHERE id = @queueId
				""";

			using var conn = _db.CreateConnection();
			var item = await conn.QueryFirstOrDefaultAsync<SyncQueue>(sql, new { queueId });

			return item != null
				? Result<SyncQueue>.Success(item)
				: Result<SyncQueue>.NotFound("Queue item not found");
		}
		catch (Exception ex)
		{
			return Result<SyncQueue>.Failure($"Query failed: {ex.Message}");
		}
	}

	public async Task<Result<List<SyncQueue>>> GetPendingAsync(Guid userId, Guid deviceId)
	{
		try
		{
			const string sql = """
				SELECT id, user_id as UserId, target_device_id as TargetDeviceId,
					   payload, status, created_at as CreatedAt
				FROM sync_queue
				WHERE user_id = @userId AND target_device_id = @deviceId AND status = 'pending'
				ORDER BY created_at ASC
				""";

			using var conn = _db.CreateConnection();
			var items = (await conn.QueryAsync<SyncQueue>(sql, new { userId, deviceId })).ToList();

			return Result<List<SyncQueue>>.Success(items);
		}
		catch (Exception ex)
		{
			return Result<List<SyncQueue>>.Failure($"Query failed: {ex.Message}");
		}
	}

	public async Task<Result<bool>> MarkProcessedAsync(Guid queueId)
	{
		try
		{
			const string sql = """
				UPDATE sync_queue
				SET status = 'processed', processed_at = CURRENT_TIMESTAMP
				WHERE id = @queueId
				""";

			using var conn = _db.CreateConnection();
			var rowsAffected = await conn.ExecuteAsync(sql, new { queueId });

			return rowsAffected > 0
				? Result<bool>.Success(true)
				: Result<bool>.NotFound("Queue item not found");
		}
		catch (Exception ex)
		{
			return Result<bool>.Failure($"Mark processed failed: {ex.Message}");
		}
	}

	public async Task<Result<bool>> MarkFailedAsync(Guid queueId, string errorMessage)
	{
		try
		{
			const string sql = """
				UPDATE sync_queue
				SET status = 'failed', error_message = @errorMessage, failed_at = CURRENT_TIMESTAMP
				WHERE id = @queueId
				""";

			using var conn = _db.CreateConnection();
			var rowsAffected = await conn.ExecuteAsync(sql, new { queueId, errorMessage });

			return rowsAffected > 0
				? Result<bool>.Success(true)
				: Result<bool>.NotFound("Queue item not found");
		}
		catch (Exception ex)
		{
			return Result<bool>.Failure($"Mark failed failed: {ex.Message}");
		}
	}

	public async Task<Result<SyncConflict>> LogConflictAsync(Guid userId, Guid deviceId, string fieldName, string expectedValue, string actualValue)
	{
		try
		{
			const string sql = """
				INSERT INTO sync_conflicts (id, user_id, device_id, field_name, expected_value, actual_value, status)
				VALUES (@id, @userId, @deviceId, @fieldName, @expectedValue, @actualValue, 'unresolved')
				RETURNING id, user_id as UserId, device_id as DeviceId, field_name as FieldName,
						  expected_value as ExpectedValue, actual_value as ActualValue,
						  status, created_at as CreatedAt
				""";

			using var conn = _db.CreateConnection();
			var conflictId = Guid.NewGuid();
			var conflict = await conn.QueryFirstOrDefaultAsync<SyncConflict>(sql, new
			{
				id = conflictId,
				userId,
				deviceId,
				fieldName,
				expectedValue,
				actualValue
			});

			return conflict != null
				? Result<SyncConflict>.Success(conflict)
				: Result<SyncConflict>.Failure("Failed to log conflict");
		}
		catch (Exception ex)
		{
			return Result<SyncConflict>.Failure($"Log conflict failed: {ex.Message}");
		}
	}

	public async Task<Result<List<SyncConflict>>> GetConflictsAsync(Guid userId)
	{
		try
		{
			const string sql = """
				SELECT id, user_id as UserId, device_id as DeviceId, field_name as FieldName,
					   expected_value as ExpectedValue, actual_value as ActualValue,
					   status, created_at as CreatedAt
				FROM sync_conflicts
				WHERE user_id = @userId AND status = 'unresolved'
				ORDER BY created_at DESC
				""";

			using var conn = _db.CreateConnection();
			var conflicts = (await conn.QueryAsync<SyncConflict>(sql, new { userId })).ToList();

			return Result<List<SyncConflict>>.Success(conflicts);
		}
		catch (Exception ex)
		{
			return Result<List<SyncConflict>>.Failure($"Query failed: {ex.Message}");
		}
	}

	public async Task<Result<bool>> ResolveConflictAsync(Guid conflictId, string resolution)
	{
		try
		{
			const string sql = """
				UPDATE sync_conflicts
				SET status = 'resolved', resolution = @resolution, resolved_at = CURRENT_TIMESTAMP
				WHERE id = @conflictId
				""";

			using var conn = _db.CreateConnection();
			var rowsAffected = await conn.ExecuteAsync(sql, new { conflictId, resolution });

			return rowsAffected > 0
				? Result<bool>.Success(true)
				: Result<bool>.NotFound("Conflict not found");
		}
		catch (Exception ex)
		{
			return Result<bool>.Failure($"Resolve failed: {ex.Message}");
		}
	}
}
