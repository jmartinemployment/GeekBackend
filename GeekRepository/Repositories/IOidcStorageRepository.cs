using GeekBackend.Data.Models;
using GeekBackend.Data.Results;

namespace GeekRepository.Repositories;

public interface IOidcStorageRepository
{
    Task<Result<OidcStorage>> FindAsync(string id);
    Task<Result<OidcStorage>> FindByUidAsync(string uid);
    Task<Result<OidcStorage>> FindByUserCodeAsync(string userCode);
    Task<Result<OidcStorage>> UpsertAsync(string id, string kind, Dictionary<string, object> payload, int? expiresIn);
    Task<Result<Unit>> DestroyAsync(string id);
    Task<Result<Unit>> ConsumeAsync(string id);
    Task<Result<Unit>> RevokeByGrantIdAsync(string grantId);
}

public struct Unit { }
