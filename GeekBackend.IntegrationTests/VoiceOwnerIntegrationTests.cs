extern alias GeekRepo;

using GeekApplication.Models.Voice;
using GeekRepo::GeekRepository.Infrastructure;
using GeekRepo::GeekRepository.Repositories.Voice;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeekBackend.IntegrationTests;

/// <summary>
/// geek_voice.owner_logins and owner_refresh_tokens against a real Postgres: one login per tenant and per email (compared lower-cased),
/// a renewal that spends a token and stores its replacement in one step, renewals racing within the reuse grace all staying signed in,
/// a spent token presented after the grace or a revoked one ending its whole family, a sign-out racing a renewal never leaving a live
/// token behind, and an expired token refused. Runs only with TEST_DATABASE_URL set.
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
            "Boca Cool HVAC " + Unique()[..6], "Plumbing, heating, & air-conditioning contractors", false, null, null, Now, "US", "FL", "dispatch@example.test", "+15615550101") { IndustryAnswers = IndustryAnswersFixture.AllNo(Now) });
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

    private static RotateVoiceRefreshTokenCommand Renewal(string old, string replacement) =>
        new(old, replacement, DateTimeOffset.UtcNow.AddDays(30));

    /// <summary>Moves a token's spend back past the reuse grace, as if it had been spent that long ago.</summary>
    private static async Task SpentLongAgoAsync(string tokenHash)
    {
        await using var conn = new Npgsql.NpgsqlConnection(IntegrationTestConfig.DatabaseUrl!);
        await conn.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand(
            "UPDATE geek_voice.owner_refresh_tokens SET used_at = NOW() - @Age WHERE token_hash = @Hash;", conn);
        cmd.Parameters.AddWithValue("Age", VoiceOwnerRefreshTokens.ReuseGrace + TimeSpan.FromMinutes(1));
        cmd.Parameters.AddWithValue("Hash", tokenHash);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    private static async Task<long> LiveTokensInFamilyAsync(Guid familyId)
    {
        await using var conn = new Npgsql.NpgsqlConnection(IntegrationTestConfig.DatabaseUrl!);
        await conn.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand(
            "SELECT COUNT(*) FROM geek_voice.owner_refresh_tokens WHERE family_id = @Family AND revoked_at IS NULL;", conn);
        cmd.Parameters.AddWithValue("Family", familyId);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task A_password_hash_is_replaced_and_an_unknown_login_is_not_found()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured) return;
        await SqlMigrationTestHelper.ApplyAsync();
        var owners = Owners();
        var (_, login) = await NewLoginAsync(owners);
        var replacement = "hash-" + Unique();

        Assert.True((await owners.UpdateOwnerPasswordHashAsync(new(login.Id, replacement))).IsSuccess);
        Assert.Equal(replacement, (await owners.FindOwnerLoginByEmailAsync(login.Email)).Value!.PasswordHash);
        Assert.Equal(VoiceFailureCodes.NotFound, (await owners.UpdateOwnerPasswordHashAsync(new(Guid.NewGuid(), replacement))).ErrorCode);
    }

    [Fact]
    public async Task A_renewal_stores_the_replacement_in_the_family_and_a_replay_after_the_grace_ends_it()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured) return;
        await SqlMigrationTestHelper.ApplyAsync();
        var owners = Owners();
        var (tenant, login) = await NewLoginAsync(owners);
        var family = Guid.NewGuid();
        var first = "hash-" + Unique();
        var second = "hash-" + Unique();
        Assert.True((await owners.StoreRefreshTokenAsync(new(login.Id, family, first, DateTimeOffset.UtcNow.AddDays(30)))).IsSuccess);

        var renewed = await owners.RotateRefreshTokenAsync(Renewal(first, second));
        Assert.True(renewed.IsSuccess, renewed.Error);
        Assert.Equal(tenant, renewed.Value!.TenantId);
        Assert.Equal(login.Id, renewed.Value.OwnerLoginId);
        Assert.Equal(family, renewed.Value.FamilyId);
        Assert.Equal(login.Email, renewed.Value.Email);

        await SpentLongAgoAsync(first);
        Assert.Equal(VoiceOwnerFailureCodes.RefreshTokenReused, (await owners.RotateRefreshTokenAsync(Renewal(first, "hash-" + Unique()))).ErrorCode);

        // the replay revoked the family, so the replacement no longer works either
        Assert.Equal(VoiceOwnerFailureCodes.RefreshTokenReused, (await owners.RotateRefreshTokenAsync(Renewal(second, "hash-" + Unique()))).ErrorCode);
        Assert.Equal(0, await LiveTokensInFamilyAsync(family));
    }

    [Fact]
    public async Task Renewals_racing_with_one_token_within_the_grace_all_stay_in_the_sign_in()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured) return;
        await SqlMigrationTestHelper.ApplyAsync();
        var owners = Owners();
        var (_, login) = await NewLoginAsync(owners);
        var family = Guid.NewGuid();
        var hash = "hash-" + Unique();
        Assert.True((await owners.StoreRefreshTokenAsync(new(login.Id, family, hash, DateTimeOffset.UtcNow.AddDays(30)))).IsSuccess);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Owners().RotateRefreshTokenAsync(Renewal(hash, "hash-" + Unique()))));

        Assert.All(results, r => Assert.True(r.IsSuccess, r.Error));
        Assert.All(results, r => Assert.Equal(family, r.Value!.FamilyId));
        Assert.Equal(9, await LiveTokensInFamilyAsync(family));
    }

    [Fact]
    public async Task A_sign_out_racing_a_renewal_never_leaves_a_live_token_in_the_family()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured) return;
        await SqlMigrationTestHelper.ApplyAsync();
        var owners = Owners();
        var (_, login) = await NewLoginAsync(owners);

        for (var round = 0; round < 20; round++)
        {
            var family = Guid.NewGuid();
            var hash = "hash-" + Unique();
            Assert.True((await owners.StoreRefreshTokenAsync(new(login.Id, family, hash, DateTimeOffset.UtcNow.AddDays(30)))).IsSuccess);

            var renewal = Owners().RotateRefreshTokenAsync(Renewal(hash, "hash-" + Unique()));
            var signOut = Owners().RevokeRefreshTokenFamilyAsync(hash);
            await Task.WhenAll(renewal, signOut);

            Assert.True(signOut.Result.IsSuccess, signOut.Result.Error);
            Assert.Equal(0, await LiveTokensInFamilyAsync(family));
        }
    }

    [Fact]
    public async Task An_unknown_hash_is_not_found_and_an_expired_token_is_not_renewed()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured) return;
        await SqlMigrationTestHelper.ApplyAsync();
        var owners = Owners();
        var (_, login) = await NewLoginAsync(owners);

        Assert.Equal(VoiceFailureCodes.NotFound, (await owners.RotateRefreshTokenAsync(Renewal("hash-" + Unique(), "hash-" + Unique()))).ErrorCode);

        var shortLived = "hash-" + Unique();
        Assert.True((await owners.StoreRefreshTokenAsync(new(login.Id, Guid.NewGuid(), shortLived, DateTimeOffset.UtcNow.AddSeconds(2)))).IsSuccess);
        await Task.Delay(TimeSpan.FromSeconds(3));
        Assert.Equal(VoiceFailureCodes.NotFound, (await owners.RotateRefreshTokenAsync(Renewal(shortLived, "hash-" + Unique()))).ErrorCode);
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
        Assert.Equal(VoiceOwnerFailureCodes.RefreshTokenReused, (await owners.RotateRefreshTokenAsync(Renewal(b, "hash-" + Unique()))).ErrorCode);

        Assert.True((await owners.RevokeRefreshTokenFamilyAsync("hash-" + Unique())).IsSuccess);
    }
}
