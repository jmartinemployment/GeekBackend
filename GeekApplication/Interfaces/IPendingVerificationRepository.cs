namespace GeekApplication.Interfaces;

public interface IPendingVerificationRepository
{
    Task<Result<bool>> UpsertAsync(string verificationCode, DateTime expiresAt);
    Task<Result<string?>> FindByEmailAsync(string email);
    Task<Result<bool>> IncrementAttemptsAsync(string verificationCode);
    Task<Result<bool>> DeleteAsync(string verificationCode);

    Task<Result<bool>> StorePendingSessionAsync(Guid userId, string sessionCode, string deviceFingerprint, DateTime expiresAt);
    Task<Result<TwoFactorPendingSession?>> GetPendingSessionAsync(string sessionCode);
    Task<Result<bool>> CompletePendingSessionAsync(string sessionCode);
    Task<Result<bool>> ExpirePendingSessionAsync(string sessionCode);
    Task<Result<bool>> IncrementAttemptAsync(string sessionCode);

    Task<Result<bool>> StoreRegistrationCodeAsync(Guid registrationRequestId, string code, DateTime expiresAt);
    Task<Result<string?>> ValidateRegistrationCodeAsync(Guid registrationRequestId);
    Task<Result<bool>> CleanupExpiredAsync();
}
