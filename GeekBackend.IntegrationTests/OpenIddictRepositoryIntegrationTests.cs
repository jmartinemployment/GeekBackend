extern alias GeekRepo;

using GeekApplication.Entities.OpenIddict;
using GeekRepo::GeekRepository.Infrastructure;
using GeekRepo::GeekRepository.Repositories.OpenIddict;
using Npgsql;

namespace GeekBackend.IntegrationTests;

public sealed class OpenIddictRepositoryIntegrationTests
{
    [Fact]
    public async Task ApplicationRepository_CreateFindDelete_RoundTrip()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured)
            return;

        await SqlMigrationTestHelper.ApplyAsync();
        var factory = new NpgsqlConnectionFactory(IntegrationTestConfig.DatabaseUrl!);
        var repo = new DapperApplicationRepository(factory);
        var id = Guid.NewGuid().ToString("N");

        var created = await repo.CreateAsync(new GeekOpenIddictApplication
        {
            Id = id,
            ClientId = $"test-{id[..8]}",
            DisplayName = "Integration Test Client",
            ClientType = "confidential",
            Permissions = "[]"
        });

        Assert.True(created.IsSuccess, created.Error);

        var found = await repo.FindByClientIdAsync(created.Value!.ClientId!);
        Assert.True(found.IsSuccess);
        Assert.NotNull(found.Value);
        Assert.Equal(id, found.Value!.Id);

        var deleted = await repo.DeleteAsync(id);
        Assert.True(deleted.IsSuccess);
        Assert.True(deleted.Value);
    }

}
