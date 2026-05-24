using System.Security.Claims;
using GeekApplication.Entities;
using GeekApplication.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using OpenIddict.Validation.AspNetCore;

namespace GeekAPI.Hubs;

[Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme, Policy = "sync")]
public sealed class SyncHub : Hub
{
    private readonly IDeviceOauthRepository _devices;
    private readonly ISyncRepository _sync;

    public SyncHub(IDeviceOauthRepository devices, ISyncRepository sync)
    {
        _devices = devices;
        _sync = sync;
    }

    public override async Task OnConnectedAsync()
    {
        var sub = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? Context.User?.FindFirst("sub")?.Value;
        var deviceIdClaim = Context.User?.FindFirst("device_id")?.Value;

        if (!string.IsNullOrWhiteSpace(sub))
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{sub}");

        if (Guid.TryParse(deviceIdClaim, out var deviceId))
        {
            var device = await _devices.FindByIdAsync(deviceId);
            if (!device.IsSuccess || device.Value is null || device.Value.IsRevoked)
            {
                Context.Abort();
                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(sub) && Guid.TryParse(sub, out var userId) && Guid.TryParse(deviceIdClaim, out var devId))
        {
            var pending = await _sync.GetPendingAsync(userId, devId);
            if (pending.IsSuccess && pending.Value is not null)
            {
                foreach (var item in pending.Value)
                    await Clients.Caller.SendAsync("syncMessage", item);
            }
        }

        await base.OnConnectedAsync();
    }

    [Authorize(Policy = "sync")]
    public Task Acknowledge(Guid queueId) => _sync.MarkProcessedAsync(queueId);
}
