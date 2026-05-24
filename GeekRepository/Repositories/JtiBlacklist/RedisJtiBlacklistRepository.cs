using StackExchange.Redis;
using GeekApplication.Entities;
using GeekApplication.Interfaces;
using GeekApplication.Results;

namespace GeekRepository.Repositories.JtiBlacklist;

public sealed class RedisJtiBlacklistRepository : IJtiBlacklist
{
    private readonly IConnectionMultiplexer _redis;
    private const string Prefix = "jti:";

    public RedisJtiBlacklistRepository(IConnectionMultiplexer redis) => _redis = redis;

    public async Task<Result<GeekApplication.Entities.JtiBlacklist>> AddAsync(string jti, DateTime expiresAt)
    {
        try
        {
            var db = _redis.GetDatabase();
            var ttl = expiresAt - DateTime.UtcNow;

            if (ttl <= TimeSpan.Zero)
                return Result<GeekApplication.Entities.JtiBlacklist>.Failure("TTL is expired");

            var key = $"{Prefix}{jti}";
            await db.StringSetAsync(key, "1", ttl, When.NotExists);

            var entry = new GeekApplication.Entities.JtiBlacklist
            {
                Jti = jti,
                ExpiresAt = expiresAt,
                BlacklistedAt = DateTime.UtcNow
            };

            return Result<GeekApplication.Entities.JtiBlacklist>.Success(entry);
        }
        catch (Exception ex)
        {
            return Result<GeekApplication.Entities.JtiBlacklist>.Failure($"Add jti failed: {ex.Message}");
        }
    }

    public async Task<Result<bool>> IsRevokedAsync(string jti)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = $"{Prefix}{jti}";
            var exists = await db.KeyExistsAsync(key);

            return Result<bool>.Success(exists);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure($"Check revoked failed: {ex.Message}");
        }
    }

    public async Task<Result<GeekApplication.Entities.JtiBlacklist>> FindByJtiAsync(string jti)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = $"{Prefix}{jti}";
            var exists = await db.KeyExistsAsync(key);

            if (!exists)
                return Result<GeekApplication.Entities.JtiBlacklist>.NotFound("JTI not found");

            var ttl = await db.KeyTimeToLiveAsync(key);
            var expiresAt = ttl.HasValue
                ? DateTime.UtcNow.Add(ttl.Value)
                : DateTime.UtcNow.AddHours(1);

            var entry = new GeekApplication.Entities.JtiBlacklist
            {
                Jti = jti,
                ExpiresAt = expiresAt,
                BlacklistedAt = DateTime.UtcNow
            };

            return Result<GeekApplication.Entities.JtiBlacklist>.Success(entry);
        }
        catch (Exception ex)
        {
            return Result<GeekApplication.Entities.JtiBlacklist>.Failure($"Query failed: {ex.Message}");
        }
    }

    public async Task<Result<bool>> DeleteAsync(string jti)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = $"{Prefix}{jti}";
            var deleted = await db.KeyDeleteAsync(key);

            return deleted
                ? Result<bool>.Success(true)
                : Result<bool>.NotFound("JTI not found");
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure($"Delete failed: {ex.Message}");
        }
    }

    public async Task<Result<bool>> CleanupExpiredAsync()
    {
        try
        {
            var server = _redis.GetServer(_redis.GetEndPoints().First());
            var keys = server.Keys(pattern: $"{Prefix}*").ToList();

            if (keys.Count == 0)
                return Result<bool>.Success(false);

            var db = _redis.GetDatabase();
            var deleted = 0L;

            foreach (var key in keys)
            {
                if (await db.KeyDeleteAsync(key))
                    deleted++;
            }

            return Result<bool>.Success(deleted > 0);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure($"Cleanup expired failed: {ex.Message}");
        }
    }
}
