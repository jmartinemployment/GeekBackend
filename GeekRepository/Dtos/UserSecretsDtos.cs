namespace GeekRepository.Dtos;

public sealed record SetTotpSecretRequest(string Secret);

public sealed record SetRecoveryCodesRequest(IReadOnlyList<string> Hashes);
