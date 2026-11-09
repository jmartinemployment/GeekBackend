namespace GeekRepository.Repositories.Social;

/// <summary>
/// Npgsql writes a timestamptz only from a UTC DateTime and throws for any other kind. Every
/// timestamp the social contracts carry is UTC (the contract says so, and an instant with a "Z"
/// deserializes as UTC), so a value that arrives with no kind is that UTC instant with its kind
/// unstated; a local value is converted.
/// </summary>
internal static class SocialTime
{
    public static DateTime Normalize(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
