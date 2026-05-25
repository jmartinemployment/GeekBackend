using GeekApplication.Auth;
using OpenIddict.Abstractions;

namespace GeekAPI.Infrastructure;

public sealed class OpenIddictScopeSeeder : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<OpenIddictScopeSeeder> _logger;
    private readonly IHostApplicationLifetime _lifetime;

    public OpenIddictScopeSeeder(
        IServiceProvider services,
        ILogger<OpenIddictScopeSeeder> logger,
        IHostApplicationLifetime lifetime)
    {
        _services = services;
        _logger = logger;
        _lifetime = lifetime;
    }

    public Task StartAsync(CancellationToken cancellationToken) =>
        HostedServiceStartup.RunAfterApplicationStartedAsync(_lifetime, SeedAsync, cancellationToken);

    private async Task SeedAsync(CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();

        if (await manager.FindByNameAsync(GeekOAuthConstants.InternalApiScope, cancellationToken) is not null)
            return;

        var descriptor = new OpenIddictScopeDescriptor
        {
            Name = GeekOAuthConstants.InternalApiScope,
            DisplayName = "Internal service API",
            Resources = { GeekOAuthConstants.GeekRepositoryAudience }
        };

        await manager.CreateAsync(descriptor, cancellationToken);
        _logger.LogInformation("OpenIddict scope {Scope} seeded.", GeekOAuthConstants.InternalApiScope);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
