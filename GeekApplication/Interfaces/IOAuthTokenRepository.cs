using GeekApplication.Dtos;

namespace GeekApplication.Interfaces;

public interface IOAuthTokenRepository
{
    Task<Result<CreateOAuthTokenRequest>> CreateAsync(CreateOAuthTokenRequest request);
    Task<Result<CreateOAuthTokenRequest>> FindByAccessTokenAsync(string accessToken);
    Task<Result<CreateOAuthTokenRequest>> FindByRefreshTokenAsync(string refreshToken);
    Task<Result<CreateOAuthTokenRequest>> UpdateAsync(string tokenId, UpdateOAuthTokenRequest request);
    Task<Result<bool>> DeleteAsync(string tokenId);
    Task<Result<bool>> RevokeTokenAsync(string jti, string reason);
    Task<Result<bool>> IsTokenBlacklistedAsync(string jti);
    Task<Result<bool>> AddToBlacklistAsync(string jti, Guid userId, DateTime expiresAt, string reason);
    Task<Result<List<string>>> GetUserBlacklistedJtisAsync(Guid userId);
    Task<Result<bool>> CleanupExpiredBlacklistEntriesAsync();
}
