using System.Data;
using System.Security.Cryptography;
using Dapper;
using GeekApplication.Auth;
using GeekApplication.Dtos;
using GeekApplication.Entities;
using GeekApplication.Interfaces;
using GeekApplication.Results;
using GeekRepository.Infrastructure;

namespace GeekRepository.Repositories;

public sealed class DeviceOauthRepository : IDeviceOauthRepository
{
    private const string DeviceColumns = """
		id, user_id as UserId, device_name as DeviceName, device_type as DeviceType,
		platform, bios_id as BiosId, device_fingerprint as DeviceFingerprint,
		is_active as IsTrusted, is_revoked as IsRevoked,
		public_key as PublicKey, challenge_nonce as ChallengeNonce,
		challenge_expires_at as ChallengeExpiresAt, trusted_until as TrustedUntil,
		created_at as CreatedAt, updated_at as UpdatedAt
		""";

    private readonly IDbConnectionFactory _db;

    public DeviceOauthRepository(IDbConnectionFactory db) => _db = db;

    public async Task<Result<DeviceOauth>> RegisterAsync(Guid userId, RegisterDeviceOauthRequest request)
    {
        try
        {
            var sql = $"""
				INSERT INTO devices_oauth (id, user_id, device_name, device_type, platform, bios_id, device_fingerprint, is_active)
				VALUES (@id, @userId, @deviceName, @deviceType, @platform, @biosId, @deviceFingerprint, true)
				RETURNING {DeviceColumns}
				""";

            using var conn = _db.CreateConnection();
            var deviceId = Guid.NewGuid();
            var device = await conn.QueryFirstOrDefaultAsync<DeviceOauth>(sql, new
            {
                id = deviceId,
                userId,
                deviceName = request.DeviceName,
                deviceType = request.DeviceType,
                platform = request.Platform,
                biosId = request.BiosId,
                deviceFingerprint = request.DeviceFingerprint
            });

            return device != null
                ? Result<DeviceOauth>.Success(device)
                : Result<DeviceOauth>.Failure("Failed to register device");
        }
        catch (Exception ex)
        {
            return Result<DeviceOauth>.Failure($"Register device failed: {ex.Message}");
        }
    }

    public async Task<Result<DeviceOauth>> FindByIdAsync(Guid deviceId)
    {
        try
        {
            var sql = $"""
				SELECT {DeviceColumns}
				FROM devices_oauth
				WHERE id = @deviceId
				""";

            using var conn = _db.CreateConnection();
            var device = await conn.QueryFirstOrDefaultAsync<DeviceOauth>(sql, new { deviceId });

            return device != null
                ? Result<DeviceOauth>.Success(device)
                : Result<DeviceOauth>.NotFound("Device not found");
        }
        catch (Exception ex)
        {
            return Result<DeviceOauth>.Failure($"Query failed: {ex.Message}");
        }
    }

    public async Task<Result<DeviceOauth>> FindByFingerprintAsync(Guid userId, string fingerprint)
    {
        try
        {
            var sql = $"""
				SELECT {DeviceColumns}
				FROM devices_oauth
				WHERE user_id = @userId AND device_fingerprint = @fingerprint
				""";

            using var conn = _db.CreateConnection();
            var device = await conn.QueryFirstOrDefaultAsync<DeviceOauth>(sql, new { userId, fingerprint });

            return device != null
                ? Result<DeviceOauth>.Success(device)
                : Result<DeviceOauth>.NotFound("Device not found");
        }
        catch (Exception ex)
        {
            return Result<DeviceOauth>.Failure($"Query failed: {ex.Message}");
        }
    }

    public async Task<Result<List<DeviceOauth>>> GetUserDevicesAsync(Guid userId)
    {
        try
        {
            var sql = $"""
				SELECT {DeviceColumns}
				FROM devices_oauth
				WHERE user_id = @userId
				ORDER BY created_at DESC
				""";

            using var conn = _db.CreateConnection();
            var devices = (await conn.QueryAsync<DeviceOauth>(sql, new { userId })).ToList();

            return Result<List<DeviceOauth>>.Success(devices);
        }
        catch (Exception ex)
        {
            return Result<List<DeviceOauth>>.Failure($"Query failed: {ex.Message}");
        }
    }

    public async Task<Result<List<DeviceOauth>>> FindByUserIdAsync(Guid userId)
    {
        try
        {
            var sql = $"""
				SELECT {DeviceColumns}
				FROM devices_oauth
				WHERE user_id = @userId
				ORDER BY created_at DESC
				""";

            using var conn = _db.CreateConnection();
            var devices = (await conn.QueryAsync<DeviceOauth>(sql, new { userId })).ToList();

            return Result<List<DeviceOauth>>.Success(devices);
        }
        catch (Exception ex)
        {
            return Result<List<DeviceOauth>>.Failure($"Query failed: {ex.Message}");
        }
    }

    public async Task<Result<DeviceOauth>> UpdateAsync(DeviceOauth device)
    {
        try
        {
            var sql = $"""
				UPDATE devices_oauth
				SET device_name = @deviceName, device_type = @deviceType, platform = @platform,
					bios_id = @biosId, device_fingerprint = @deviceFingerprint, is_active = @isTrusted,
					updated_at = CURRENT_TIMESTAMP
				WHERE id = @id
				RETURNING {DeviceColumns}
				""";

            using var conn = _db.CreateConnection();
            var updated = await conn.QueryFirstOrDefaultAsync<DeviceOauth>(sql, new
            {
                device.Id,
                device.DeviceName,
                device.DeviceType,
                device.Platform,
                device.BiosId,
                device.DeviceFingerprint,
                isTrusted = device.IsTrusted
            });

            return updated != null
                ? Result<DeviceOauth>.Success(updated)
                : Result<DeviceOauth>.NotFound("Device not found");
        }
        catch (Exception ex)
        {
            return Result<DeviceOauth>.Failure($"Update failed: {ex.Message}");
        }
    }

    public async Task<Result<DeviceOauth>> UpsertAsync(Guid userId, UpsertDeviceRequest request)
    {
        try
        {
            var sql = $"""
				INSERT INTO devices_oauth (id, user_id, device_name, device_type, platform, bios_id, device_fingerprint, is_active)
				VALUES (@id, @userId, @deviceName, @deviceType, @platform, @biosId, @deviceFingerprint, true)
				ON CONFLICT (user_id, device_fingerprint) DO UPDATE
				SET device_name = @deviceName, device_type = @deviceType, platform = @platform,
					updated_at = CURRENT_TIMESTAMP, is_active = true, is_revoked = false
				RETURNING {DeviceColumns}
				""";

            using var conn = _db.CreateConnection();
            var deviceId = Guid.NewGuid();
            var device = await conn.QueryFirstOrDefaultAsync<DeviceOauth>(sql, new
            {
                id = deviceId,
                userId,
                deviceName = request.DeviceName,
                deviceType = request.DeviceType,
                platform = request.Platform,
                biosId = request.BiosId,
                deviceFingerprint = request.DeviceFingerprint
            });

            return device != null
                ? Result<DeviceOauth>.Success(device)
                : Result<DeviceOauth>.Failure("Failed to upsert device");
        }
        catch (Exception ex)
        {
            return Result<DeviceOauth>.Failure($"Upsert failed: {ex.Message}");
        }
    }

    public async Task<Result<bool>> RevokeAsync(Guid deviceId)
    {
        try
        {
            const string sql = """
				UPDATE devices_oauth
				SET is_active = false, is_revoked = true, updated_at = CURRENT_TIMESTAMP
				WHERE id = @deviceId
				""";

            using var conn = _db.CreateConnection();
            var rowsAffected = await conn.ExecuteAsync(sql, new { deviceId });

            return rowsAffected > 0
                ? Result<bool>.Success(true)
                : Result<bool>.NotFound("Device not found");
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure($"Revoke failed: {ex.Message}");
        }
    }

    public async Task<Result<bool>> DeleteAsync(Guid deviceId)
    {
        try
        {
            const string sql = """
				DELETE FROM devices_oauth
				WHERE id = @deviceId
				""";

            using var conn = _db.CreateConnection();
            var rowsAffected = await conn.ExecuteAsync(sql, new { deviceId });

            return rowsAffected > 0
                ? Result<bool>.Success(true)
                : Result<bool>.NotFound("Device not found");
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure($"Delete failed: {ex.Message}");
        }
    }

    public async Task<Result<bool>> TrustAsync(Guid deviceId, int trustDaysOrNull = 30)
    {
        try
        {
            const string sql = """
				UPDATE devices_oauth
				SET trusted_until = CURRENT_TIMESTAMP + INTERVAL '1 day' * @trustDays,
					updated_at = CURRENT_TIMESTAMP
				WHERE id = @deviceId
				""";

            using var conn = _db.CreateConnection();
            var rowsAffected = await conn.ExecuteAsync(sql, new { deviceId, trustDays = trustDaysOrNull });

            return rowsAffected > 0
                ? Result<bool>.Success(true)
                : Result<bool>.NotFound("Device not found");
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure($"Trust failed: {ex.Message}");
        }
    }

    public async Task<Result<string>> IssueChallengeAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        try
        {
            var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            const string sql = """
				UPDATE devices_oauth
				SET challenge_nonce = @nonce,
				    challenge_expires_at = CURRENT_TIMESTAMP + INTERVAL '60 seconds',
				    updated_at = CURRENT_TIMESTAMP
				WHERE id = @deviceId AND is_active = true AND is_revoked = false
				""";

            using var conn = _db.CreateConnection();
            var rows = await conn.ExecuteAsync(sql, new { deviceId, nonce });
            return rows > 0
                ? Result<string>.Success(nonce)
                : Result<string>.NotFound("Device not found");
        }
        catch (Exception ex)
        {
            return Result<string>.Failure($"Issue challenge failed: {ex.Message}");
        }
    }

    public async Task<Result<bool>> VerifyChallengeAsync(
        Guid deviceId,
        string nonce,
        string signatureBase64,
        string? publicKeyPem,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var selectSql = $"""
				SELECT {DeviceColumns}
				FROM devices_oauth
				WHERE id = @deviceId
				""";

            using var conn = _db.CreateConnection();
            var device = await conn.QueryFirstOrDefaultAsync<DeviceOauth>(selectSql, new { deviceId });
            if (device is null)
                return Result<bool>.NotFound("Device not found");

            if (string.IsNullOrWhiteSpace(device.ChallengeNonce)
                || device.ChallengeNonce != nonce
                || device.ChallengeExpiresAt is null
                || device.ChallengeExpiresAt <= DateTime.UtcNow)
            {
                return Result<bool>.Failure("Challenge expired or invalid.");
            }

            var keyPem = !string.IsNullOrWhiteSpace(publicKeyPem) ? publicKeyPem : device.PublicKey;
            if (string.IsNullOrWhiteSpace(keyPem))
                return Result<bool>.Failure("Device public key is required.");

            if (!DeviceCrypto.VerifySignature(keyPem, nonce, signatureBase64))
                return Result<bool>.Failure("Invalid device signature.");

            const string updateSql = """
				UPDATE devices_oauth
				SET public_key = @publicKey,
				    is_active = true,
				    trusted_until = CURRENT_TIMESTAMP + INTERVAL '30 days',
				    challenge_nonce = NULL,
				    challenge_expires_at = NULL,
				    updated_at = CURRENT_TIMESTAMP
				WHERE id = @deviceId
				""";

            var rows = await conn.ExecuteAsync(updateSql, new { deviceId, publicKey = keyPem });
            return rows > 0
                ? Result<bool>.Success(true)
                : Result<bool>.Failure("Failed to mark device trusted.");
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure($"Verify challenge failed: {ex.Message}");
        }
    }

    public async Task<Result<List<DeviceOauth>>> GetActiveDevicesAsync(Guid userId)
    {
        try
        {
            var sql = $"""
				SELECT {DeviceColumns}
				FROM devices_oauth
				WHERE user_id = @userId AND is_active = true AND is_revoked = false
				ORDER BY created_at DESC
				""";

            using var conn = _db.CreateConnection();
            var devices = (await conn.QueryAsync<DeviceOauth>(sql, new { userId })).ToList();

            return Result<List<DeviceOauth>>.Success(devices);
        }
        catch (Exception ex)
        {
            return Result<List<DeviceOauth>>.Failure($"Query failed: {ex.Message}");
        }
    }
}
