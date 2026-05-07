using GeekBackend.Data.Models;
using GeekBackend.Data.Results;

namespace GeekRepository.Repositories;

public interface IOAuthTokenRepository
{
    Task<Result<OauthToken>> FindByAccessTokenAsync(string accessToken);
    Task<Result<OauthToken>> FindByRefreshTokenAsync(string refreshToken);
    Task<Result<OauthToken>> CreateAsync(CreateOAuthTokenRequest request);
    Task<Result<OauthToken>> UpdateAsync(string id, UpdateOAuthTokenRequest request);
    Task<Result<Unit>> DeleteAsync(string id);
}

public class CreateOAuthTokenRequest
{
    public string AccessToken { get; set; } = null!;
    public DateTime AccessTokenExpiresAt { get; set; }
    public string RefreshToken { get; set; } = null!;
    public DateTime? RefreshTokenExpiresAt { get; set; }
    public string UserId { get; set; } = null!;
    public string ClientId { get; set; } = null!;
    public string? Scope { get; set; }
    public string? BiosId { get; set; }
}

public class UpdateOAuthTokenRequest
{
    public bool? IsActive { get; set; }
    public DateTime? AccessTokenExpiresAt { get; set; }
    public DateTime? RefreshTokenExpiresAt { get; set; }
}
