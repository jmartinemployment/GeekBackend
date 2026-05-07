using GeekBackend.Data.Models;
using GeekBackend.Data.Results;

namespace GeekRepository.Repositories;

public interface IPendingVerificationRepository
{
    Task<Result<PendingVerification>> FindByEmailAsync(string email);
    Task<Result<PendingVerification>> UpsertAsync(UpsertPendingVerificationRequest request);
    Task<Result<PendingVerification>> IncrementAttemptsAsync(string id);
    Task<Result<Unit>> DeleteAsync(string id);
}

public class UpsertPendingVerificationRequest
{
    public string Email { get; set; } = null!;
    public string OtpHash { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
}
