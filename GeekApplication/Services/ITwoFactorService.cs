namespace GeekApplication.Services;

public interface ITwoFactorService
{
    Task<Result<(string Secret, List<string> RecoveryCodes)>> GenerateTwoFactorSecretAsync();
    Task<Result<bool>> VerifyTotpAsync(Guid userId, string code);
    Task<Result<bool>> VerifyRecoveryCodeAsync(Guid userId, string code);
    Task<Result<List<string>>> RegenerateTwoFactorAsync(Guid userId);
    Task<Result<bool>> TrustDeviceForTwoFactorAsync(Guid userId, string deviceFingerprint);
    Task<Result<TwoFactorPendingSession>> CreateTwoFactorPendingSessionAsync(Guid userId, string deviceFingerprint);
    Task<Result<bool>> CompleteTwoFactorChallengeAsync(string sessionCode, string totpCode);
    Task<Result<TwoFactorPendingSession>> GetPendingSessionAsync(string sessionCode);
    Task<Result<bool>> RevokeTrustedDeviceAsync(Guid userId, Guid trustedDeviceId);
}
