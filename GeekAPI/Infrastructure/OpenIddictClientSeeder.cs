using System.Collections.Immutable;
using GeekApplication.Auth;
using OpenIddict.Abstractions;

namespace GeekAPI.Infrastructure;

public sealed class OpenIddictClientSeeder : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<OpenIddictClientSeeder> _logger;
    private readonly IHostEnvironment _environment;
    private readonly IHostApplicationLifetime _lifetime;

    public OpenIddictClientSeeder(
        IServiceProvider services,
        ILogger<OpenIddictClientSeeder> logger,
        IHostEnvironment environment,
        IHostApplicationLifetime lifetime)
    {
        _services = services;
        _logger = logger;
        _environment = environment;
        _lifetime = lifetime;
    }

    public Task StartAsync(CancellationToken cancellationToken) =>
        HostedServiceStartup.RunAfterApplicationStartedAsync(_lifetime, SeedAsync, cancellationToken);

    private async Task SeedAsync(CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        await SeedConfidentialClientAsync(
            manager,
            clientId: GeekOAuthConstants.GeekApiClientId,
            displayName: "Geek API (repository access)",
            clientSecret: RequireSecret("GEEK_API_CLIENT_SECRET"),
            permissions:
            [
                OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddictConstants.Permissions.GrantTypes.ClientCredentials,
                OpenIddictConstants.Permissions.Prefixes.Scope + GeekOAuthConstants.InternalApiScope
            ],
            cancellationToken);

        await SeedConfidentialClientAsync(
            manager,
            clientId: GeekOAuthConstants.GeekSeoBackendClientId,
            displayName: "Geek SEO Backend (repository access)",
            clientSecret: RequireSecret("GEEK_SEO_BACKEND_CLIENT_SECRET"),
            permissions:
            [
                OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddictConstants.Permissions.GrantTypes.ClientCredentials,
                OpenIddictConstants.Permissions.Prefixes.Scope + GeekOAuthConstants.InternalApiScope
            ],
            cancellationToken);

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
                "GEEK_API_CLIENT_SECRET" => "dev-geekapi-secret-change-me",
                "GEEK_SEO_BACKEND_CLIENT_SECRET" => "dev-geekseo-backend-secret-change-me",
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

        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            DisplayName = displayName
        };
        OpenIddictClientPermissionBuilder.ApplyPublicAuthorizationCodeClient(descriptor);
        await manager.CreateAsync(descriptor, cancellationToken);
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
            DisplayName = displayName
        };

        if (permissions.Contains(OpenIddictConstants.Permissions.Endpoints.Introspection))
            OpenIddictClientPermissionBuilder.ApplyConfidentialIntrospection(descriptor);
        else
        {
            var scopes = permissions
                .Where(static p => p.StartsWith(OpenIddictConstants.Permissions.Prefixes.Scope, StringComparison.Ordinal))
                .Select(static p => p[OpenIddictConstants.Permissions.Prefixes.Scope.Length..])
                .ToArray();
            OpenIddictClientPermissionBuilder.ApplyConfidentialClientCredentials(descriptor, scopes);
        }

        await manager.CreateAsync(descriptor, cancellationToken);
    }
}
