using GeekApplication.Entities;

namespace GeekApplication.Interfaces;

public interface IOidcStorageRepository
{
    Task<Result<bool>> UpsertAsync(OidcStorage storage);
    Task<Result<OidcStorage?>> FindAsync(string kind, string key);
    Task<Result<OidcStorage?>> FindByUidAsync(string uid);
    Task<Result<OidcStorage?>> FindByUserCodeAsync(string userCode);
    Task<Result<bool>> DestroyAsync(string key);
    Task<Result<bool>> ConsumeAsync(string key);
    Task<Result<bool>> RevokeByGrantIdAsync(string grantId);

    Task<Result<bool>> StoreNonceAsync(string nonce, string clientId, DateTime expiresAt);
    Task<Result<bool>> ValidateNonceAsync(string nonce, string clientId);
    Task<Result<bool>> RevokeNonceAsync(string nonce);

    Task<Result<bool>> StoreAuthorizationCodeAsync(string code, Guid userId, string clientId, List<string> scopes, DateTime expiresAt);
    Task<Result<(Guid UserId, string ClientId, List<string> Scopes)?>> ValidateAuthorizationCodeAsync(string code);
    Task<Result<bool>> RevokeAuthorizationCodeAsync(string code);

    Task<Result<bool>> CleanupExpiredAsync();
}
