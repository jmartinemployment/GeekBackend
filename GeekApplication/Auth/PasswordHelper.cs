namespace GeekApplication.Auth;

public static class PasswordHelper
{
    private static int WorkFactor =>
        int.TryParse(Environment.GetEnvironmentVariable("BCRYPT_WORK_FACTOR"), out var factor) && factor is >= 10 and <= 16
            ? factor
            : 12;

    public static string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, workFactor: WorkFactor);

    public static bool Verify(string password, string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(passwordHash))
            return false;

        try
        {
            return BCrypt.Net.BCrypt.Verify(password, passwordHash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;
        }
    }
}
