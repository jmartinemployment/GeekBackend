using System.Security.Cryptography;
using GeekAPI.Services.Voice;
using GeekAPI.Services.Voice.Mcp;
using GeekAPI.Services.Voice.Onboarding;
using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeekBackend.Tests.Voice;

/// <summary>
/// Connecting during signup is for a business waiting for payment and for nothing else, so it can never replace a paying business's
/// connection; a bucket's keys are sealed before they are stored. The refusals happen before any connection service is touched.
/// </summary>
public sealed class SignupConnectionsTests
{
    private static readonly Guid Tenant = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 22, 15, 0, 0, TimeSpan.Zero);
    }

    private sealed class Tenants(string status) : IVoiceStore
    {
        public List<SaveVoiceStorageConnectionCommand> SavedBuckets { get; } = new();

        public Task<Result<VoiceTenant>> GetTenantAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(tenantId == Tenant
                ? Result<VoiceTenant>.Success(new VoiceTenant(Tenant, "Plumbing", "Plumbing", DateTimeOffset.UnixEpoch, "US", "FL", "", "", status, DateTimeOffset.UnixEpoch))
                : Result<VoiceTenant>.Failure("No such tenant.", VoiceFailureCodes.NotFound));

        public Task<Result<VoiceStorageConnection>> SaveStorageConnectionAsync(Guid tenantId, SaveVoiceStorageConnectionCommand command, CancellationToken ct = default)
        {
            SavedBuckets.Add(command);
            return Task.FromResult(Result<VoiceStorageConnection>.Success(
                new VoiceStorageConnection(tenantId, command.Provider, command.AccessKeyIdEncrypted, command.SecretAccessKeyEncrypted, command.Bucket, command.KeyPrefix, command.Region, true)));
        }

        public Task<Result<VoiceTenant>> CreateTenantAsync(CreateVoiceTenantCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceTenant>> ActivateTenantAsync(Guid tenantId, ActivateVoiceTenantCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceTelephony>> GetTelephonyAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceReceptionistConfig>> GetConfigAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceReceptionistConfig>> SaveConfigAsync(Guid tenantId, VoiceReceptionistConfig config, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceStorageConnection>> GetStorageConnectionAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceMcpConnection>> GetMcpConnectionAsync(Guid tenantId, string provider, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceMcpConnection>> SaveMcpConnectionAsync(Guid tenantId, string provider, SaveVoiceMcpConnectionCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceCallStored>> UpsertCallAsync(Guid tenantId, UpsertVoiceCallCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceAppointmentCreated>> CreateAppointmentAsync(Guid tenantId, CreateVoiceAppointmentCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceConnectionRemoved>> DisconnectMcpAsync(Guid tenantId, string provider, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceConnectionRemoved>> DisconnectStorageAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static readonly string Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static (SignupConnections Service, Tenants Store) Build(string status = VoiceTenantStatuses.PendingPayment)
    {
        var store = new Tenants(status);
        // The connection services are reached only after a business has been shown to be waiting for payment; these tests stop earlier.
        var service = new SignupConnections(store, null!, null!, null!, null!, new VoiceCredentialProtector(() => Key), new Clock(), NullLogger<SignupConnections>.Instance);
        return (service, store);
    }

    private static SaveSignupBucketRequest Bucket(Guid? tenant = null, string? provider = VoiceStorageProviders.AwsS3, string? region = "us-east-1", string? secret = "secret-key") =>
        new(tenant ?? Tenant, provider, "my-bucket", region, "AKIA-ACCESS", secret, "records/");

    [Theory]
    [InlineData(VoiceTenantStatuses.Active)]
    [InlineData(VoiceTenantStatuses.Suspended)]
    public async Task A_business_that_is_not_waiting_for_payment_cannot_connect_anything_during_signup(string status)
    {
        var (service, store) = Build(status);

        var begin = await service.BeginAsync(new BeginSignupConnectionRequest(Tenant, GoogleCalendarOptions.Provider));
        var complete = await service.CompleteAsync(new CompleteSignupConnectionRequest(Tenant, GoogleCalendarOptions.Provider, "code", "state", null));
        var check = await service.CheckAsync(Tenant, GoogleCalendarOptions.Provider);
        var bucket = await service.SaveBucketAsync(Bucket());

        foreach (var result in new[] { begin.ErrorCode, complete.ErrorCode, check.ErrorCode, bucket.ErrorCode })
            Assert.Equal(VoiceFailureCodes.Conflict, result);
        Assert.Empty(store.SavedBuckets);
    }

    [Fact]
    public async Task A_missing_or_unknown_business_is_refused()
    {
        var (service, _) = Build();

        var missing = await service.BeginAsync(new BeginSignupConnectionRequest(null, GoogleCalendarOptions.Provider));
        var unknown = await service.BeginAsync(new BeginSignupConnectionRequest(Guid.NewGuid(), GoogleCalendarOptions.Provider));

        Assert.Equal(VoiceFailureCodes.InvalidRequest, missing.ErrorCode);
        Assert.Equal(VoiceFailureCodes.NotFound, unknown.ErrorCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("aws_s3")]
    [InlineData("microsoft_calendar")]
    public async Task Only_the_two_Google_connections_go_through_the_approval_screen(string? provider)
    {
        var (service, _) = Build();

        var result = await service.BeginAsync(new BeginSignupConnectionRequest(Tenant, provider));

        Assert.Equal(McpFailureCodes.ProviderNotSupported, result.ErrorCode);
    }

    [Fact]
    public async Task A_bucket_is_stored_with_its_keys_sealed()
    {
        var (service, store) = Build();

        var result = await service.SaveBucketAsync(Bucket());

        Assert.True(result.IsSuccess, result.Error);
        var saved = Assert.Single(store.SavedBuckets);
        Assert.Equal("my-bucket", saved.Bucket);
        Assert.Equal("records", saved.KeyPrefix);
        Assert.Equal("us-east-1", saved.Region);
        Assert.DoesNotContain("AKIA-ACCESS", saved.AccessKeyIdEncrypted, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-key", saved.SecretAccessKeyEncrypted, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("google_cloud_storage", "us-east-1", "secret-key")]
    [InlineData("aws_s3", "", "secret-key")]
    [InlineData("aws_s3", "us-east-1", "")]
    public async Task A_bucket_with_a_missing_part_is_refused_and_nothing_is_stored(string provider, string region, string secret)
    {
        var (service, store) = Build();

        var result = await service.SaveBucketAsync(Bucket(provider: provider, region: region, secret: secret));

        Assert.Equal(VoiceFailureCodes.InvalidRequest, result.ErrorCode);
        Assert.Empty(store.SavedBuckets);
    }
}
