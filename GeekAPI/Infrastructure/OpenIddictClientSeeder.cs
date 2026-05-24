using System.Collections.Immutable;
using OpenIddict.Abstractions;

namespace GeekAPI.Infrastructure;

public sealed class OpenIddictClientSeeder : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<OpenIddictClientSeeder> _logger;
    private readonly IHostEnvironment _environment;

    public OpenIddictClientSeeder(
        IServiceProvider services,
        ILogger<OpenIddictClientSeeder> logger,
        IHostEnvironment environment)
    {
        _services = services;
        _logger = logger;
        _environment = environment;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        await SeedPublicClientAsync(
            manager,
            clientId: "geek-seo-electron",
            displayName: "Geek SEO Electron",
            cancellationToken);

        await SeedConfidentialClientAsync(
            manager,
            clientId: "geekatyourspot-website",
            displayName: "Geek At Your Spot Website",
            clientSecret: RequireSecret("GEEK_WEBSITE_CLIENT_SECRET"),
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
            clientSecret: RequireSecret("GEEK_RESOURCE_SERVER_SECRET"),
            permissions:
            [
                OpenIddictConstants.Permissions.Endpoints.Introspection,
                OpenIddictConstants.Permissions.GrantTypes.ClientCredentials
            ],
            cancellationToken);

        _logger.LogInformation("OpenIddict first-party clients seeded.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private string RequireSecret(string variableName)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        if (!string.IsNullOrWhiteSpace(value))
            return value;

        if (_environment.IsDevelopment())
        {
            _logger.LogWarning(
                "{Variable} is not set; using development-only placeholder for local OAuth seeding.",
                variableName);
            return variableName switch
            {
                "GEEK_WEBSITE_CLIENT_SECRET" => "dev-website-secret-change-me",
                "GEEK_RESOURCE_SERVER_SECRET" => "dev-resource-server-secret-change-me",
                _ => throw new InvalidOperationException($"No development placeholder for {variableName}.")
            };
        }

        throw new InvalidOperationException(
            $"{variableName} must be set in production before GeekAPI can start.");
    }

    private static async Task SeedPublicClientAsync(
        IOpenIddictApplicationManager manager,
        string clientId,
        string displayName,
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
            RedirectUris =
            {
                new Uri("http://127.0.0.1/callback", UriKind.Absolute),
                new Uri("geek://callback", UriKind.Absolute)
            },
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
