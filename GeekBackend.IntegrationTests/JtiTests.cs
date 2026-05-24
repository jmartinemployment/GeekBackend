extern alias GeekRepo;

using GeekRepo::GeekRepository.Infrastructure;
using GeekRepo::GeekRepository.Repositories.JtiBlacklist;

namespace GeekBackend.IntegrationTests;

public sealed class JtiTests
{
    [Fact]
    public async Task JtiBlacklist_RevokedJti_IsDetected()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured)
            return;

        await SqlMigrationTestHelper.ApplyAsync();
        var factory = new NpgsqlConnectionFactory(IntegrationTestConfig.DatabaseUrl!);
        var repo = new PostgresJtiBlacklistRepository(factory);
        var jti = Guid.NewGuid().ToString("N");

        var added = await repo.AddAsync(jti, DateTime.UtcNow.AddMinutes(10));
        Assert.True(added.IsSuccess, added.Error);

        var revoked = await repo.IsRevokedAsync(jti);
        Assert.True(revoked.IsSuccess && revoked.Value, revoked.Error);

        var deleted = await repo.DeleteAsync(jti);
        Assert.True(deleted.IsSuccess);
    }
}
