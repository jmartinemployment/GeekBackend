namespace GeekApplication.Interfaces;

public interface IUserClaimsRepository
{
    Task<Result<UserClaim>> CreateAsync(Guid userId, string claimType, string claimValue);
    Task<Result<UserClaim>> FindByIdAsync(Guid claimId);
    Task<Result<List<UserClaim>>> GetByUserIdAsync(Guid userId);
    Task<Result<List<UserClaim>>> GetByUserIdAndTypeAsync(Guid userId, string claimType);
    Task<Result<bool>> DeleteAsync(Guid claimId);
    Task<Result<bool>> DeleteByUserAsync(Guid userId, string claimType, string claimValue);
}
