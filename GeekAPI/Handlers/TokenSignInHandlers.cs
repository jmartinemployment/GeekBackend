using System.Security.Claims;
using GeekApplication.Interfaces;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;

namespace GeekAPI.Handlers;

public sealed class AttachClaimsHandler : IOpenIddictServerHandler<OpenIddictServerEvents.ProcessSignInContext>
{
    public static OpenIddictServerHandlerDescriptor Descriptor { get; }
        = OpenIddictServerHandlerDescriptor.CreateBuilder<OpenIddictServerEvents.ProcessSignInContext>()
            .UseSingletonHandler<AttachClaimsHandler>()
            .SetOrder(int.MinValue + 50_000)
            .SetType(OpenIddictServerHandlerType.BuiltIn)
            .Build();

    public ValueTask HandleAsync(OpenIddictServerEvents.ProcessSignInContext context)
    {
        if (context.Principal?.Identity is not ClaimsIdentity identity)
            return default;

        if (!identity.HasClaim(c => c.Type == OpenIddictConstants.Claims.JwtId))
        {
            identity.AddClaim(new Claim(
                OpenIddictConstants.Claims.JwtId,
                Guid.NewGuid().ToString("N"),
                ClaimValueTypes.String,
                OpenIddictConstants.Claims.Issuer));
        }

        return default;
    }
}

public sealed class DeviceTrustHandler : IOpenIddictServerHandler<OpenIddictServerEvents.ProcessSignInContext>
{
    private readonly IServiceProvider _services;

    public DeviceTrustHandler(IServiceProvider services) => _services = services;

    public static OpenIddictServerHandlerDescriptor Descriptor { get; }
        = OpenIddictServerHandlerDescriptor.CreateBuilder<OpenIddictServerEvents.ProcessSignInContext>()
            .UseScopedHandler<DeviceTrustHandler>()
            .SetOrder(int.MinValue + 51_000)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

    public async ValueTask HandleAsync(OpenIddictServerEvents.ProcessSignInContext context)
    {
        if (context.Principal?.Identity is not ClaimsIdentity identity)
            return;

        var deviceIdValue = identity.FindFirst("device_id")?.Value;
        if (string.IsNullOrWhiteSpace(deviceIdValue) || !Guid.TryParse(deviceIdValue, out var deviceId))
            return;

        using var scope = _services.CreateScope();
        var devices = scope.ServiceProvider.GetRequiredService<IDeviceOauthRepository>();
        var deviceResult = await devices.FindByIdAsync(deviceId);
        if (!deviceResult.IsSuccess || deviceResult.Value is null || deviceResult.Value.IsRevoked)
            return;

        var device = deviceResult.Value;
        identity.SetClaim("device_id", device.Id.ToString());
        identity.SetClaim("device_fingerprint", device.DeviceFingerprint);

        var isTrusted = device.TrustedUntil is not null && device.TrustedUntil > DateTime.UtcNow;
        if (isTrusted || device.IsTrusted)
            identity.SetClaim("device_trusted", "true");
    }
}
