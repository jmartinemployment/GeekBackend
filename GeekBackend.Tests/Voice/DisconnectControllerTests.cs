using System.Security.Claims;
using System.Text.Json;
using GeekAPI.Controllers.Voice;
using GeekAPI.Services.Voice.Mcp;
using GeekAPI.Services.Voice.OwnerAuth;
using GeekAPI.Services.Voice.Storage;
using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace GeekBackend.Tests.Voice;

/// <summary>
/// The owner withdrawing a connection: disconnecting a calendar takes booking off with it and in that order, disconnecting the
/// Drive folder leaves booking alone, withdrawing what is already gone is a success and not a 404, another tenant's id is
/// refused, a provider the product does not support is refused before the store is asked, and every store failure comes back as
/// its own status and code rather than a clean-looking answer. No log line carries a key, a token or a bucket.
/// </summary>
public sealed class DisconnectControllerTests
{
    private static readonly Guid Tenant = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid OtherTenant = Guid.Parse("99999999-8888-7777-6666-555555555555");

    private const string Bucket = "acme-call-records-bucket";
    private const string KeyId = "AKIA-SEALED-KEY-ID";
    private const string Secret = "SEALED-SECRET-ACCESS-KEY";

    // ----- The rig -----

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Lines { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception) + (exception?.ToString() ?? string.Empty));
    }

    private sealed class Store : IVoiceStore
    {
        public Result<VoiceReceptionistConfig> Config { get; set; } = Result<VoiceReceptionistConfig>.Success(ConfigWith(booking: true));
        public Result<VoiceReceptionistConfig>? SaveConfigResult { get; set; }
        public List<VoiceReceptionistConfig> SavedConfigs { get; } = new();

        public Result<VoiceConnectionRemoved> McpResult { get; set; } = Result<VoiceConnectionRemoved>.Success(new VoiceConnectionRemoved(true));
        public Result<VoiceConnectionRemoved> StorageResult { get; set; } = Result<VoiceConnectionRemoved>.Success(new VoiceConnectionRemoved(true));

        public List<(Guid TenantId, string Provider)> McpDisconnects { get; } = new();
        public List<Guid> StorageDisconnects { get; } = new();

        public Task<Result<VoiceReceptionistConfig>> GetConfigAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(Config);

        public Task<Result<VoiceReceptionistConfig>> SaveConfigAsync(Guid tenantId, VoiceReceptionistConfig config, CancellationToken ct = default)
        {
            SavedConfigs.Add(config);
            return Task.FromResult(SaveConfigResult ?? Result<VoiceReceptionistConfig>.Success(config));
        }

        public Task<Result<VoiceConnectionRemoved>> DisconnectMcpAsync(Guid tenantId, string provider, CancellationToken ct = default)
        {
            McpDisconnects.Add((tenantId, provider));
            return Task.FromResult(McpResult);
        }

        public Task<Result<VoiceConnectionRemoved>> DisconnectStorageAsync(Guid tenantId, CancellationToken ct = default)
        {
            StorageDisconnects.Add(tenantId);
            return Task.FromResult(StorageResult);
        }

        // A bucket with its sealed keys, so a change that started reading or logging the connection would be caught by the test
        // that reads the log lines. Nothing in this controller asks for it.
        public Task<Result<VoiceStorageConnection>> GetStorageConnectionAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(Result<VoiceStorageConnection>.Success(
                new VoiceStorageConnection(tenantId, VoiceStorageProviders.AwsS3, KeyId, Secret, Bucket, "calls", "us-east-1", true)));

        public Task<Result<VoiceTenant>> CreateTenantAsync(CreateVoiceTenantCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceTenant>> GetTenantAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceTenant>> ActivateTenantAsync(Guid tenantId, ActivateVoiceTenantCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceTelephony>> GetTelephonyAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceMcpConnection>> GetMcpConnectionAsync(Guid tenantId, string provider, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceMcpConnection>> SaveMcpConnectionAsync(Guid tenantId, string provider, SaveVoiceMcpConnectionCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceStorageConnection>> SaveStorageConnectionAsync(Guid tenantId, SaveVoiceStorageConnectionCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceCallStored>> UpsertCallAsync(Guid tenantId, UpsertVoiceCallCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceAppointmentCreated>> CreateAppointmentAsync(Guid tenantId, CreateVoiceAppointmentCommand command, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static VoiceReceptionistConfig ConfigWith(bool booking)
    {
        using var empty = JsonDocument.Parse("{}");
        var element = empty.RootElement.Clone();
        return new VoiceReceptionistConfig(
            "Thanks for calling Acme.",
            "You are the receptionist for Acme.",
            element,
            element,
            VoiceTransferModes.WarmTransfer,
            "+15618650000",
            VoiceTransferCallerIdModes.OriginalCaller,
            ["fire", "ambulance"],
            RecordingDisclosureEnabled: true,
            BookingEnabled: booking);
    }

    private static DisconnectController ControllerFor(Store store, CapturingLogger<DisconnectController> log, Guid? signedInAs = null) =>
        new(store, log)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(
                        new ClaimsIdentity([new Claim(ReceptionistAuth.TenantClaim, (signedInAs ?? Tenant).ToString())], "test")),
                },
            },
        };

    private static string Json(object? value) => JsonSerializer.Serialize(value);

    // ----- Disconnecting a calendar -----

    [Fact]
    public async Task A_calendar_the_owner_disconnects_is_removed_and_booking_goes_off_with_it()
    {
        var store = new Store { Config = Result<VoiceReceptionistConfig>.Success(ConfigWith(booking: true)) };
        var log = new CapturingLogger<DisconnectController>();

        var ok = Assert.IsType<OkObjectResult>(
            await ControllerFor(store, log).DisconnectMcp(Tenant, VoiceMcpProviders.GoogleCalendar, CancellationToken.None));

        Assert.False(Assert.Single(store.SavedConfigs).BookingEnabled);
        Assert.Equal((Tenant, VoiceMcpProviders.GoogleCalendar), Assert.Single(store.McpDisconnects));

        var json = Json(ok.Value);
        Assert.Contains("\"removed\":true", json, StringComparison.Ordinal);
        Assert.Contains("\"bookingDisabled\":true", json, StringComparison.Ordinal);
        Assert.Contains(log.Lines, l => l.Contains("booking turned off", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Booking_that_was_already_off_is_not_saved_again_and_the_calendar_still_goes()
    {
        var store = new Store { Config = Result<VoiceReceptionistConfig>.Success(ConfigWith(booking: false)) };

        var ok = Assert.IsType<OkObjectResult>(
            await ControllerFor(store, new CapturingLogger<DisconnectController>())
                .DisconnectMcp(Tenant, VoiceMcpProviders.GoogleCalendar, CancellationToken.None));

        Assert.Empty(store.SavedConfigs);
        Assert.Single(store.McpDisconnects);
        Assert.Contains("\"bookingDisabled\":false", Json(ok.Value), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_calendar_that_is_not_connected_answers_removed_false_and_is_not_an_error()
    {
        var store = new Store
        {
            Config = Result<VoiceReceptionistConfig>.Success(ConfigWith(booking: false)),
            McpResult = Result<VoiceConnectionRemoved>.Success(new VoiceConnectionRemoved(false)),
        };

        var result = await ControllerFor(store, new CapturingLogger<DisconnectController>())
            .DisconnectMcp(Tenant, VoiceMcpProviders.GoogleCalendar, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Contains("\"removed\":false", Json(ok.Value), StringComparison.Ordinal);
        Assert.DoesNotContain("error", Json(ok.Value), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Disconnecting_Google_Drive_leaves_booking_alone()
    {
        var store = new Store { Config = Result<VoiceReceptionistConfig>.Success(ConfigWith(booking: true)) };

        var ok = Assert.IsType<OkObjectResult>(
            await ControllerFor(store, new CapturingLogger<DisconnectController>())
                .DisconnectMcp(Tenant, VoiceMcpProviders.GoogleDrive, CancellationToken.None));

        Assert.Empty(store.SavedConfigs);
        Assert.Equal((Tenant, VoiceMcpProviders.GoogleDrive), Assert.Single(store.McpDisconnects));
        Assert.Contains("\"bookingDisabled\":false", Json(ok.Value), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("dropbox")]
    [InlineData("Google_Calendar")]
    public async Task A_provider_the_product_does_not_support_is_refused_before_anything_is_removed(string provider)
    {
        var store = new Store();

        var bad = Assert.IsType<BadRequestObjectResult>(
            await ControllerFor(store, new CapturingLogger<DisconnectController>()).DisconnectMcp(Tenant, provider, CancellationToken.None));

        Assert.Contains($"\"code\":\"{VoiceFailureCodes.InvalidRequest}\"", Json(bad.Value), StringComparison.Ordinal);
        Assert.Empty(store.McpDisconnects);
        Assert.Empty(store.SavedConfigs);
    }

    // ----- The order the calendar's two steps happen in -----

    [Fact]
    public async Task A_booking_setting_that_will_not_save_aborts_the_disconnect()
    {
        var store = new Store
        {
            Config = Result<VoiceReceptionistConfig>.Success(ConfigWith(booking: true)),
            SaveConfigResult = Result<VoiceReceptionistConfig>.Failure("The store is unavailable.", VoiceFailureCodes.StoreUnavailable),
        };
        var log = new CapturingLogger<DisconnectController>();

        var refused = Assert.IsType<ObjectResult>(
            await ControllerFor(store, log).DisconnectMcp(Tenant, VoiceMcpProviders.GoogleCalendar, CancellationToken.None));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, refused.StatusCode);
        Assert.Contains($"\"code\":\"{VoiceFailureCodes.StoreUnavailable}\"", Json(refused.Value), StringComparison.Ordinal);

        // The calendar stays, because booking is still on and a calendar-less booking setting is the state this exists to prevent.
        Assert.Empty(store.McpDisconnects);
        Assert.Contains(log.Lines, l => l.Contains("left in place", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Settings_that_cannot_be_read_abort_the_disconnect()
    {
        var store = new Store { Config = Result<VoiceReceptionistConfig>.Failure("The store is unavailable.", VoiceFailureCodes.StoreUnavailable) };

        var refused = Assert.IsType<ObjectResult>(
            await ControllerFor(store, new CapturingLogger<DisconnectController>())
                .DisconnectMcp(Tenant, VoiceMcpProviders.GoogleCalendar, CancellationToken.None));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, refused.StatusCode);
        Assert.Empty(store.McpDisconnects);
        Assert.Empty(store.SavedConfigs);
    }

    // ----- Store failures on the removal itself -----

    [Theory]
    [InlineData(VoiceFailureCodes.InvalidRequest, StatusCodes.Status400BadRequest)]
    [InlineData(VoiceFailureCodes.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(VoiceFailureCodes.Conflict, StatusCodes.Status409Conflict)]
    [InlineData(VoiceFailureCodes.StoreUnavailable, StatusCodes.Status503ServiceUnavailable)]
    public async Task A_store_that_cannot_remove_the_connection_is_answered_with_its_status_and_its_code(string code, int status)
    {
        var store = new Store
        {
            Config = Result<VoiceReceptionistConfig>.Success(ConfigWith(booking: false)),
            McpResult = Result<VoiceConnectionRemoved>.Failure("The connection could not be removed.", code),
        };

        var refused = Assert.IsType<ObjectResult>(
            await ControllerFor(store, new CapturingLogger<DisconnectController>())
                .DisconnectMcp(Tenant, VoiceMcpProviders.GoogleCalendar, CancellationToken.None));

        Assert.Equal(status, refused.StatusCode);
        Assert.Contains($"\"code\":\"{code}\"", Json(refused.Value), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(VoiceFailureCodes.InvalidRequest, StatusCodes.Status400BadRequest)]
    [InlineData(VoiceFailureCodes.StoreUnavailable, StatusCodes.Status503ServiceUnavailable)]
    public async Task A_store_that_cannot_remove_the_bucket_is_answered_with_its_status_and_its_code(string code, int status)
    {
        var store = new Store { StorageResult = Result<VoiceConnectionRemoved>.Failure("The bucket could not be removed.", code) };

        var refused = Assert.IsType<ObjectResult>(
            await ControllerFor(store, new CapturingLogger<DisconnectController>()).DisconnectStorage(Tenant, CancellationToken.None));

        Assert.Equal(status, refused.StatusCode);
        Assert.Contains($"\"code\":\"{code}\"", Json(refused.Value), StringComparison.Ordinal);
    }

    // ----- Disconnecting the bucket -----

    [Fact]
    public async Task The_bucket_is_disconnected_and_nothing_of_it_comes_back()
    {
        var store = new Store();

        var ok = Assert.IsType<OkObjectResult>(
            await ControllerFor(store, new CapturingLogger<DisconnectController>()).DisconnectStorage(Tenant, CancellationToken.None));

        Assert.Equal(Tenant, Assert.Single(store.StorageDisconnects));

        var json = Json(ok.Value);
        Assert.Contains("\"removed\":true", json, StringComparison.Ordinal);
        Assert.DoesNotContain(Bucket, json, StringComparison.Ordinal);
        Assert.DoesNotContain(KeyId, json, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_bucket_that_is_not_connected_answers_removed_false_and_is_not_an_error()
    {
        var store = new Store { StorageResult = Result<VoiceConnectionRemoved>.Success(new VoiceConnectionRemoved(false)) };

        var ok = Assert.IsType<OkObjectResult>(
            await ControllerFor(store, new CapturingLogger<DisconnectController>()).DisconnectStorage(Tenant, CancellationToken.None));

        Assert.Contains("\"removed\":false", Json(ok.Value), StringComparison.Ordinal);
        Assert.Single(store.StorageDisconnects);
    }

    // ----- Whose tenant it is -----

    [Fact]
    public async Task Another_tenants_id_is_refused_on_both_routes_and_nothing_is_removed()
    {
        var store = new Store();
        var controller = ControllerFor(store, new CapturingLogger<DisconnectController>(), signedInAs: OtherTenant);

        var mcp = Assert.IsType<ObjectResult>(await controller.DisconnectMcp(Tenant, VoiceMcpProviders.GoogleCalendar, CancellationToken.None));
        var bucket = Assert.IsType<ObjectResult>(await controller.DisconnectStorage(Tenant, CancellationToken.None));

        Assert.Equal(StatusCodes.Status403Forbidden, mcp.StatusCode);
        Assert.Equal(StatusCodes.Status403Forbidden, bucket.StatusCode);
        Assert.Contains(McpFailureCodes.TenantForbidden, Json(mcp.Value), StringComparison.Ordinal);
        Assert.Empty(store.McpDisconnects);
        Assert.Empty(store.StorageDisconnects);
        Assert.Empty(store.SavedConfigs);
    }

    [Fact]
    public async Task A_sign_in_carrying_no_tenant_is_refused()
    {
        var store = new Store();
        var controller = new DisconnectController(store, new CapturingLogger<DisconnectController>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([], "test")) },
            },
        };

        var refused = Assert.IsType<ObjectResult>(await controller.DisconnectStorage(Tenant, CancellationToken.None));

        Assert.Equal(StatusCodes.Status403Forbidden, refused.StatusCode);
        Assert.Contains(McpFailureCodes.TenantUnverified, Json(refused.Value), StringComparison.Ordinal);
        Assert.Empty(store.StorageDisconnects);
    }

    // ----- Zero Data Retention -----

    [Fact]
    public async Task No_log_line_carries_a_key_a_token_or_a_bucket()
    {
        var store = new Store { Config = Result<VoiceReceptionistConfig>.Success(ConfigWith(booking: true)) };
        var log = new CapturingLogger<DisconnectController>();
        var controller = ControllerFor(store, log);

        await controller.DisconnectMcp(Tenant, VoiceMcpProviders.GoogleCalendar, CancellationToken.None);
        await controller.DisconnectMcp(Tenant, VoiceMcpProviders.GoogleDrive, CancellationToken.None);
        await controller.DisconnectStorage(Tenant, CancellationToken.None);

        Assert.NotEmpty(log.Lines);
        foreach (var line in log.Lines)
        {
            Assert.DoesNotContain(Bucket, line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(KeyId, line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(Secret, line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(GoogleDriveOptions.FolderName, line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("+15618650000", line, StringComparison.Ordinal);
            Assert.DoesNotContain("Thanks for calling", line, StringComparison.Ordinal);
        }

        // What an operator does get: which tenant, which connection, and what happened to it.
        Assert.Contains(log.Lines, l => l.Contains(Tenant.ToString(), StringComparison.Ordinal) && l.Contains("disconnected", StringComparison.Ordinal));
    }
}
