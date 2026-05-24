namespace GeekBackend.IntegrationTests;

internal static class IntegrationTestConfig
{
    public static string? DatabaseUrl => Environment.GetEnvironmentVariable("TEST_DATABASE_URL");

    public static bool IsDatabaseConfigured => !string.IsNullOrWhiteSpace(DatabaseUrl);
}
