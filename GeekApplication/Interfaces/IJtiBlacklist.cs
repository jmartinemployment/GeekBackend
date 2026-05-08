namespace GeekApplication.Interfaces;

public interface IJtiBlacklist
{
    Task<Result<JtiBlacklist>> AddAsync(string jti, DateTime expiresAt);
    Task<Result<bool>> IsRevokedAsync(string jti);
    Task<Result<JtiBlacklist>> FindByJtiAsync(string jti);
    Task<Result<bool>> DeleteAsync(string jti);
    Task<Result<bool>> CleanupExpiredAsync();
}
