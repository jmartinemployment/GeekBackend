using GeekApplication.Dtos;
using GeekApplication.Entities;

namespace GeekApplication.Interfaces;

public interface IOAuthClientRepository
{
    Task<Result<OauthClientEntity>> CreateAsync(CreateOAuthClientRequest request);
    Task<Result<OauthClientEntity>> FindByIdAsync(Guid clientId);
    Task<Result<OauthClientEntity>> FindByClientIdAsync(string clientId);
    Task<Result<List<OauthClientEntity>>> GetAllAsync();
    Task<Result<OauthClientEntity>> UpdateAsync(OauthClientEntity client);
    Task<Result<bool>> DeleteAsync(Guid clientId);
    Task<Result<bool>> ValidateClientSecretAsync(string clientId, string clientSecret);
}
