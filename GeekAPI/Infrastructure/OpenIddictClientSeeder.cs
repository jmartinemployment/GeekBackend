using System.Collections.Immutable;
using OpenIddict.Abstractions;

namespace GeekAPI.Infrastructure;

public sealed class OpenIddictClientSeeder : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<OpenIddictClientSeeder> _logger;

    public OpenIddictClientSeeder(IServiceProvider services, ILogger<OpenIddictClientSeeder> logger)
    {
        _services = services;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        await SeedPublicClientAsync(
            manager,
            clientId: "geek-seo-electron",
            displayName: "Geek SEO Electron",
            redirectUris: ["http://127.0.0.1/callback", "geek://callback"],
            cancellationToken);

        await SeedConfidentialClientAsync(
            manager,
            clientId: "geekatyourspot-website",
            displayName: "Geek At Your Spot Website",
            clientSecret: Environment.GetEnvironmentVariable("GEEK_WEBSITE_CLIENT_SECRET") ?? "dev-website-secret-change-me",
            permissions:
            [
                OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddictConstants.Permissions.GrantTypes.ClientCredentials,
                OpenIddictConstants.Permissions.Prefixes.Scope + "mcp.tools"
            ],
            cancellationToken);

        await SeedConfidentialClientAsync(
            manager,
            clientId: "geek-resource-server",
            displayName: "Geek Resource Server Introspection",
            clientSecret: Environment.GetEnvironmentVariable("GEEK_RESOURCE_SERVER_SECRET") ?? "dev-resource-server-secret-change-me",
            permissions:
            [
                OpenIddictConstants.Permissions.Endpoints.Introspection,
                OpenIddictConstants.Permissions.GrantTypes.ClientCredentials
            ],
            cancellationToken);

        _logger.LogInformation("OpenIddict first-party clients seeded.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task SeedPublicClientAsync(
        IOpenIddictApplicationManager manager,
        string clientId,
        string displayName,
        IReadOnlyList<string> redirectUris,
        CancellationToken cancellationToken)
    {
        if (await manager.FindByClientIdAsync(clientId, cancellationToken) is not null)
            return;

        await manager.CreateAsync(new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            DisplayName = displayName,
            ClientType = OpenIddictConstants.ClientTypes.Public,
            ConsentType = OpenIddictConstants.ConsentTypes.Implicit,
            RedirectUris = { new Uri("http://127.0.0.1/callback", UriKind.Absolute), new Uri("geek://callback", UriKind.Absolute) },
            Permissions =
            {
                OpenIddictConstants.Permissions.Endpoints.Authorization,
                OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
                OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
                OpenIddictConstants.Permissions.ResponseTypes.Code,
                OpenIddictConstants.Permissions.Scopes.Email,
                OpenIddictConstants.Permissions.Scopes.Profile,
                OpenIddictConstants.Permissions.Prefixes.Scope + OpenIddictConstants.Scopes.OfflineAccess,
                OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange
            }
        }, cancellationToken);
    }

    private static async Task SeedConfidentialClientAsync(
        IOpenIddictApplicationManager manager,
        string clientId,
        string displayName,
        string clientSecret,
        ImmutableArray<string> permissions,
        CancellationToken cancellationToken)
    {
        if (await manager.FindByClientIdAsync(clientId, cancellationToken) is not null)
            return;

        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientSecret = clientSecret,
            DisplayName = displayName,
            ClientType = OpenIddictConstants.ClientTypes.Confidential
        };

        foreach (var permission in permissions)
            descriptor.Permissions.Add(permission);

        await manager.CreateAsync(descriptor, cancellationToken);
    }
}
