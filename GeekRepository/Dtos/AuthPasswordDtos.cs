namespace GeekRepository.Dtos;

public sealed record VerifyPasswordRequest(string Password);

public sealed record UpdatePasswordRequest(string Password);
