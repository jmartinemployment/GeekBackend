using GeekApplication.Results;

namespace GeekApplication.Interfaces;

public interface IUserSecretsRepository
{
    Task<Result<string?>> GetTotpSecretAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<Result<bool>> SetTotpSecretAsync(Guid userId, string base32Secret, CancellationToken cancellationToken = default);
    Task<Result<bool>> ClearTotpSecretAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<string>>> GetRecoveryCodeHashesAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<Result<bool>> SetRecoveryCodeHashesAsync(Guid userId, IReadOnlyList<string> hashedCodes, CancellationToken cancellationToken = default);
}
