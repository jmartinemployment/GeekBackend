using System.Data;
using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;
using GeekRepository.Controllers.Voice;
using GeekRepository.Infrastructure;
using GeekRepository.Repositories.Voice;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeekBackend.Tests.Voice;

/// <summary>
/// The refusals the owner store makes before it touches the database (a connection factory that throws proves none is opened), the
/// shape an email is stored in, and the status each failure code is answered with. What the database enforces (the unique email and
/// tenant, the single-statement spend of a refresh token, the revoked family) needs a real Postgres and is not run here.
/// </summary>
public sealed class VoiceOwnerStoreTests
{
    private sealed class NoDatabase : IDbConnectionFactory
    {
        public IDbConnection CreateConnection() => throw new InvalidOperationException("This test must be refused before it asks for a connection.");
    }

    private static readonly Guid Tenant = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static VoiceOwnerRepository Store() => new(new NoDatabase(), NullLogger<VoiceOwnerRepository>.Instance);

    private static async Task AssertInvalid<T>(Task<Result<T>> call)
    {
        var result = await call;
        Assert.False(result.IsSuccess);
        Assert.Equal(VoiceFailureCodes.InvalidRequest, result.ErrorCode);
    }

    [Fact]
    public async Task A_login_needs_a_tenant_an_email_and_a_hash()
    {
        await AssertInvalid(Store().CreateOwnerLoginAsync(new CreateVoiceOwnerLoginCommand(Guid.Empty, "a@b.co", "hash", null)));
        await AssertInvalid(Store().CreateOwnerLoginAsync(new CreateVoiceOwnerLoginCommand(Tenant, "not-an-email", "hash", null)));
        await AssertInvalid(Store().CreateOwnerLoginAsync(new CreateVoiceOwnerLoginCommand(Tenant, "a@b.co", "  ", null)));
        await AssertInvalid(Store().CreateOwnerLoginAsync(new CreateVoiceOwnerLoginCommand(Tenant, "a@b.co", "", null)));
        await AssertInvalid(Store().CreateOwnerLoginAsync(new CreateVoiceOwnerLoginCommand(Tenant, "a@b.co", "hash", "Dana\0")));
    }

    [Fact]
    public async Task Replacing_a_password_hash_needs_a_login_and_a_hash()
    {
        await AssertInvalid(Store().UpdateOwnerPasswordHashAsync(new UpdateVoiceOwnerPasswordCommand(Guid.Empty, "hash")));
        await AssertInvalid(Store().UpdateOwnerPasswordHashAsync(new UpdateVoiceOwnerPasswordCommand(Guid.NewGuid(), " ")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("plain")]
    [InlineData("@b.co")]
    [InlineData("a@")]
    [InlineData("a@@b.co")]
    [InlineData("a b@c.co")]
    [InlineData("a\0@b.co")]
    [InlineData("a\u0007@b.co")]
    public async Task An_email_that_cannot_be_one_is_refused_without_a_lookup(string? email) =>
        await AssertInvalid(Store().FindOwnerLoginByEmailAsync(email!));

    [Theory]
    [InlineData("  Owner@Example.COM ", "owner@example.com")]
    [InlineData("a@b.co", "a@b.co")]
    public void An_email_is_stored_and_looked_up_trimmed_and_lower_cased(string given, string stored) =>
        Assert.Equal(stored, VoiceOwnerRepository.NormalizeEmail(given));

    [Fact]
    public void An_email_over_254_characters_is_refused()
    {
        var local = new string('a', 250);
        Assert.Null(VoiceOwnerRepository.NormalizeEmail($"{local}@b.co"));
        Assert.NotNull(VoiceOwnerRepository.NormalizeEmail($"{new string('a', 240)}@b.co"));
    }

    [Fact]
    public async Task A_refresh_token_needs_a_login_a_family_and_a_hash()
    {
        var future = DateTimeOffset.UtcNow.AddDays(30);
        await AssertInvalid(Store().StoreRefreshTokenAsync(new StoreVoiceRefreshTokenCommand(Guid.Empty, Guid.NewGuid(), "h", future)));
        await AssertInvalid(Store().StoreRefreshTokenAsync(new StoreVoiceRefreshTokenCommand(Guid.NewGuid(), Guid.Empty, "h", future)));
        await AssertInvalid(Store().StoreRefreshTokenAsync(new StoreVoiceRefreshTokenCommand(Guid.NewGuid(), Guid.NewGuid(), " ", future)));
    }

    [Fact]
    public async Task Renewing_needs_both_hashes_and_two_different_ones_and_revoking_needs_a_hash()
    {
        var future = DateTimeOffset.UtcNow.AddDays(30);
        await AssertInvalid(Store().RotateRefreshTokenAsync(new RotateVoiceRefreshTokenCommand("", "new", future)));
        await AssertInvalid(Store().RotateRefreshTokenAsync(new RotateVoiceRefreshTokenCommand("old", "  ", future)));
        await AssertInvalid(Store().RotateRefreshTokenAsync(new RotateVoiceRefreshTokenCommand("same", "same", future)));
        await AssertInvalid(Store().RevokeRefreshTokenFamilyAsync(""));
    }

    private sealed class FakeOwners : IVoiceOwnerStore
    {
        public Result<VoiceOwnerLogin> Create { get; set; } = Result<VoiceOwnerLogin>.Success(new VoiceOwnerLogin(Guid.NewGuid(), Tenant, "a@b.co", "hash", null));
        public Result<RotatedVoiceRefreshToken> Rotate { get; set; } = Result<RotatedVoiceRefreshToken>.Failure("x", VoiceFailureCodes.NotFound);

        public Task<Result<VoiceOwnerLogin>> CreateOwnerLoginAsync(CreateVoiceOwnerLoginCommand command, CancellationToken ct = default) => Task.FromResult(Create);
        public Task<Result<VoiceOwnerLogin>> FindOwnerLoginByEmailAsync(string email, CancellationToken ct = default) => Task.FromResult(Create);
        public Task<Result<bool>> UpdateOwnerPasswordHashAsync(UpdateVoiceOwnerPasswordCommand command, CancellationToken ct = default) => Task.FromResult(Result<bool>.Success(true));
        public Task<Result<bool>> StoreRefreshTokenAsync(StoreVoiceRefreshTokenCommand command, CancellationToken ct = default) => Task.FromResult(Result<bool>.Success(true));
        public Task<Result<RotatedVoiceRefreshToken>> RotateRefreshTokenAsync(RotateVoiceRefreshTokenCommand command, CancellationToken ct = default) => Task.FromResult(Rotate);
        public Task<Result<bool>> RevokeRefreshTokenFamilyAsync(string tokenHash, CancellationToken ct = default) => Task.FromResult(Result<bool>.Success(true));
    }

    private static int StatusOf(IActionResult result) => ((ObjectResult)result).StatusCode ?? 200;

    [Fact]
    public async Task A_created_login_is_201_and_a_clash_is_409_and_an_unknown_tenant_is_404()
    {
        var owners = new FakeOwners();
        var controller = new VoiceOwnerController(owners);
        var command = new CreateVoiceOwnerLoginCommand(Tenant, "a@b.co", "hash", null);

        Assert.Equal(201, StatusOf(await controller.Create(command, default)));

        owners.Create = Result<VoiceOwnerLogin>.Failure("taken", VoiceFailureCodes.Conflict);
        Assert.Equal(409, StatusOf(await controller.Create(command, default)));

        owners.Create = Result<VoiceOwnerLogin>.Failure("no tenant", VoiceFailureCodes.NotFound);
        Assert.Equal(404, StatusOf(await controller.Create(command, default)));

        owners.Create = Result<VoiceOwnerLogin>.Failure("bad", VoiceFailureCodes.InvalidRequest);
        Assert.Equal(400, StatusOf(await controller.Create(command, default)));

        owners.Create = Result<VoiceOwnerLogin>.Failure("down", VoiceFailureCodes.StoreUnavailable);
        Assert.Equal(503, StatusOf(await controller.Create(command, default)));
    }

    [Fact]
    public async Task A_replayed_refresh_token_is_409_with_its_own_code_and_an_unknown_one_is_404()
    {
        var owners = new FakeOwners();
        var controller = new VoiceOwnerController(owners);

        var renewal = new RotateVoiceRefreshTokenCommand("old", "new", DateTimeOffset.UtcNow.AddDays(30));
        owners.Rotate = Result<RotatedVoiceRefreshToken>.Failure("used", VoiceOwnerFailureCodes.RefreshTokenReused);
        var reused = Assert.IsAssignableFrom<ObjectResult>(await controller.Rotate(renewal, default));
        Assert.Equal(409, reused.StatusCode);
        Assert.Contains(VoiceOwnerFailureCodes.RefreshTokenReused, System.Text.Json.JsonSerializer.Serialize(reused.Value));

        owners.Rotate = Result<RotatedVoiceRefreshToken>.Failure("none", VoiceFailureCodes.NotFound);
        Assert.Equal(404, StatusOf(await controller.Rotate(renewal, default)));
    }

    [Fact]
    public async Task A_missing_body_is_400_on_every_route()
    {
        var controller = new VoiceOwnerController(new FakeOwners());
        Assert.Equal(400, StatusOf(await controller.Create(null!, default)));
        Assert.Equal(400, StatusOf(await controller.Find(null!, default)));
        Assert.Equal(400, StatusOf(await controller.Store(null!, default)));
        Assert.Equal(400, StatusOf(await controller.UpdatePassword(null!, default)));
        Assert.Equal(400, StatusOf(await controller.Rotate(null!, default)));
        Assert.Equal(400, StatusOf(await controller.Revoke(null!, default)));
    }
}
