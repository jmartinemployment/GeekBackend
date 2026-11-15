extern alias GeekRepo;

using GeekApplication.Models.Voice;
using GeekRepo::GeekRepository.Infrastructure;
using GeekRepo::GeekRepository.Repositories.Voice;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeekBackend.IntegrationTests;

/// <summary>
/// geek_voice.owner_logins and owner_refresh_tokens against a real Postgres: one login per tenant and per email (compared lower-cased),
/// a refresh token spent exactly once even when two requests race for it, a spent or revoked token ending its whole family, and an
/// expired token refused. Runs only with TEST_DATABASE_URL set.
/// </summary>
public sealed class VoiceOwnerIntegrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    private static VoiceRepository Tenants() =>
        new(new NpgsqlConnectionFactory(IntegrationTestConfig.DatabaseUrl!), NullLogger<VoiceRepository>.Instance);

    private static VoiceOwnerRepository Owners() =>
        new(new NpgsqlConnectionFactory(IntegrationTestConfig.DatabaseUrl!), NullLogger<VoiceOwnerRepository>.Instance);

    private static string Unique() => Guid.NewGuid().ToString("N");

    private static async Task<Guid> NewTenantAsync()
    {
        var created = await Tenants().CreateTenantAsync(new(
            "Boca Cool HVAC " + Unique()[..6], "HVAC", false, null, null, Now, "US", "FL", null, "dispatch@example.test", "+15615550101"));
        Assert.True(created.IsSuccess, created.Error);
        return created.Value!.Id;
    }

    private static async Task<(Guid TenantId, VoiceOwnerLogin Login)> NewLoginAsync(VoiceOwnerRepository owners)
    {
        var tenant = await NewTenantAsync();
        var created = await owners.CreateOwnerLoginAsync(new(tenant, $"owner-{Unique()}@example.test", "hash-" + Unique(), "Dana"));
        Assert.True(created.IsSuccess, created.Error);
        return (tenant, created.Value!);
    }

    [Fact]
    public async Task A_login_is_found_by_email_in_any_case_and_is_unique_per_tenant_and_per_email()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured) return;
        await SqlMigrationTestHelper.ApplyAsync();
        var owners = Owners();
        var email = $"Owner-{Unique()}@Example.Test";

        var tenant = await NewTenantAsync();
        var created = await owners.CreateOwnerLoginAsync(new(tenant, email, "hash", "Dana"));
        Assert.True(created.IsSuccess, created.Error);
        Assert.Equal(email.ToLowerInvariant(), created.Value!.Email);

        var found = await owners.FindOwnerLoginByEmailAsync(email.ToUpperInvariant());
        Assert.True(found.IsSuccess, found.Error);
        Assert.Equal(created.Value.Id, found.Value!.Id);
        Assert.Equal(tenant, found.Value.TenantId);

        Assert.Equal(VoiceFailureCodes.Conflict, (await owners.CreateOwnerLoginAsync(new(tenant, $"other-{Unique()}@example.test", "hash", null))).ErrorCode);

        var otherTenant = await NewTenantAsync();
        Assert.Equal(VoiceFailureCodes.Conflict, (await owners.CreateOwnerLoginAsync(new(otherTenant, email.ToUpperInvariant(), "hash", null))).ErrorCode);

        Assert.Equal(VoiceFailureCodes.NotFound, (await owners.CreateOwnerLoginAsync(new(Guid.NewGuid(), $"x-{Unique()}@example.test", "hash", null))).ErrorCode);
        Assert.Equal(VoiceFailureCodes.NotFound, (await owners.FindOwnerLoginByEmailAsync($"nobody-{Unique()}@example.test")).ErrorCode);
    }

    [Fact]
    public async Task A_refresh_token_is_spent_once_and_a_replay_ends_the_whole_family()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured) return;
        await SqlMigrationTestHelper.ApplyAsync();
        var owners = Owners();
        var (tenant, login) = await NewLoginAsync(owners);
        var family = Guid.NewGuid();
        var first = "hash-" + Unique();
        var second = "hash-" + Unique();
        Assert.True((await owners.StoreRefreshTokenAsync(new(login.Id, family, first, DateTimeOffset.UtcNow.AddDays(30)))).IsSuccess);

        var spent = await owners.ConsumeRefreshTokenAsync(first);
        Assert.True(spent.IsSuccess, spent.Error);
        Assert.Equal(tenant, spent.Value!.TenantId);
        Assert.Equal(login.Id, spent.Value.OwnerLoginId);
        Assert.Equal(family, spent.Value.FamilyId);

        // the replacement the rotation hands out, in the same family
        Assert.True((await owners.StoreRefreshTokenAsync(new(login.Id, family, second, DateTimeOffset.UtcNow.AddDays(30)))).IsSuccess);

        var replay = await owners.ConsumeRefreshTokenAsync(first);
        Assert.Equal(VoiceOwnerFailureCodes.RefreshTokenReused, replay.ErrorCode);

        // the replay revoked the family, so the replacement no longer works either
        Assert.Equal(VoiceOwnerFailureCodes.RefreshTokenReused, (await owners.ConsumeRefreshTokenAsync(second)).ErrorCode);
    }

    [Fact]
    public async Task Two_requests_racing_for_one_refresh_token_cannot_both_win()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured) return;
        await SqlMigrationTestHelper.ApplyAsync();
        var owners = Owners();
        var (_, login) = await NewLoginAsync(owners);
        var hash = "hash-" + Unique();
        Assert.True((await owners.StoreRefreshTokenAsync(new(login.Id, Guid.NewGuid(), hash, DateTimeOffset.UtcNow.AddDays(30)))).IsSuccess);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Owners().ConsumeRefreshTokenAsync(hash)));

        Assert.Equal(1, results.Count(r => r.IsSuccess));
    }

    [Fact]
    public async Task An_unknown_hash_is_not_found_and_an_expired_token_is_not_spendable()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured) return;
        await SqlMigrationTestHelper.ApplyAsync();
        var owners = Owners();
        var (_, login) = await NewLoginAsync(owners);

        Assert.Equal(VoiceFailureCodes.NotFound, (await owners.ConsumeRefreshTokenAsync("hash-" + Unique())).ErrorCode);

        var shortLived = "hash-" + Unique();
        Assert.True((await owners.StoreRefreshTokenAsync(new(login.Id, Guid.NewGuid(), shortLived, DateTimeOffset.UtcNow.AddSeconds(2)))).IsSuccess);
        await Task.Delay(TimeSpan.FromSeconds(3));
        Assert.Equal(VoiceFailureCodes.NotFound, (await owners.ConsumeRefreshTokenAsync(shortLived)).ErrorCode);
    }

    [Fact]
    public async Task Revoking_a_family_ends_every_token_in_it_and_an_unknown_hash_is_not_an_error()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured) return;
        await SqlMigrationTestHelper.ApplyAsync();
        var owners = Owners();
        var (_, login) = await NewLoginAsync(owners);
        var family = Guid.NewGuid();
        var a = "hash-" + Unique();
        var b = "hash-" + Unique();
        Assert.True((await owners.StoreRefreshTokenAsync(new(login.Id, family, a, DateTimeOffset.UtcNow.AddDays(30)))).IsSuccess);
        Assert.True((await owners.StoreRefreshTokenAsync(new(login.Id, family, b, DateTimeOffset.UtcNow.AddDays(30)))).IsSuccess);

        Assert.True((await owners.RevokeRefreshTokenFamilyAsync(a)).IsSuccess);
        Assert.Equal(VoiceOwnerFailureCodes.RefreshTokenReused, (await owners.ConsumeRefreshTokenAsync(b)).ErrorCode);

        Assert.True((await owners.RevokeRefreshTokenFamilyAsync("hash-" + Unique())).IsSuccess);
    }
}
