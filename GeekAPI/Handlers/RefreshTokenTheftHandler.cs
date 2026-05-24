using GeekApplication.Interfaces;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace GeekAPI.Handlers;

/// <summary>
/// When OpenIddict rejects a refresh token outside the reuse leeway, revoke all tokens for the subject.
/// </summary>
public sealed class RefreshTokenTheftHandler : IOpenIddictServerHandler<OpenIddictServerEvents.ProcessAuthenticationContext>
{
    private readonly IServiceProvider _services;
    private readonly ILogger<RefreshTokenTheftHandler> _logger;

    public RefreshTokenTheftHandler(IServiceProvider services, ILogger<RefreshTokenTheftHandler> logger)
    {
        _services = services;
        _logger = logger;
    }

    public static OpenIddictServerHandlerDescriptor Descriptor { get; }
        = OpenIddictServerHandlerDescriptor.CreateBuilder<OpenIddictServerEvents.ProcessAuthenticationContext>()
            .UseScopedHandler<RefreshTokenTheftHandler>()
            .SetOrder(int.MinValue + 52_000)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

    public async ValueTask HandleAsync(OpenIddictServerEvents.ProcessAuthenticationContext context)
    {
        if (context is null || !context.RejectRefreshToken)
            return;

        var subject = context.RefreshTokenPrincipal?.FindFirst(Claims.Subject)?.Value;
        if (string.IsNullOrWhiteSpace(subject))
            return;

        using var scope = _services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();
        await tokens.RevokeBySubjectAsync(subject);

        if (Guid.TryParse(subject, out var userId))
        {
            var audit = scope.ServiceProvider.GetService<IAuditRepository>();
            if (audit is not null)
            {
                await audit.LogAsync(
                    userId,
                    deviceId: null,
                    eventType: "RefreshTokenTheftDetected",
                    description: "Refresh token reuse detected; all tokens revoked for subject.",
                    ipAddress: null,
                    userAgent: null,
                    isSuccess: false,
                    errorCode: "token_reuse");
            }
        }

        _logger.LogWarning("Refresh token theft detected for subject {Subject}; tokens revoked.", subject);
    }
}
