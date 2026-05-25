using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace GeekAPI.Handlers;

/// <summary>
/// Short-lived access tokens for machine (client_credentials) callers per platform plan (2–5 minutes).
/// </summary>
public sealed class ServiceTokenLifetimeHandler : IOpenIddictServerHandler<OpenIddictServerEvents.ProcessSignInContext>
{
    private static readonly TimeSpan ServiceAccessTokenLifetime = TimeSpan.FromMinutes(5);

    public static OpenIddictServerHandlerDescriptor Descriptor { get; }
        = OpenIddictServerHandlerDescriptor.CreateBuilder<OpenIddictServerEvents.ProcessSignInContext>()
            .UseSingletonHandler<ServiceTokenLifetimeHandler>()
            .SetOrder(100_000)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

    public ValueTask HandleAsync(OpenIddictServerEvents.ProcessSignInContext context)
    {
        var principal = context.Principal;
        if (principal is null)
            return default;

        var subject = principal.GetClaim(Claims.Subject);
        var clientId = principal.GetClaim(Claims.ClientId);
        if (string.IsNullOrEmpty(subject)
            || string.IsNullOrEmpty(clientId)
            || !string.Equals(subject, clientId, StringComparison.Ordinal))
        {
            return default;
        }

        foreach (var identity in principal.Identities)
            identity.SetAccessTokenLifetime(ServiceAccessTokenLifetime);

        return default;
    }
}
