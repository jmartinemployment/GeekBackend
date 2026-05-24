namespace GeekRepository.Dtos;

public sealed record RevokeTokenRequest(string Reason);

public sealed record BlacklistTokenRequest(string Jti, Guid UserId, DateTime ExpiresAt, string Reason);
