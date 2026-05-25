using System.Collections.Immutable;
using OpenIddict.Abstractions;

namespace GeekAPI.Infrastructure;

/// <summary>
/// Standard OpenIddict permission sets for first-party and admin-created OAuth clients.
/// </summary>
public static class OpenIddictClientPermissionBuilder
{
    public static void ApplyPublicAuthorizationCodeClient(
        OpenIddictApplicationDescriptor descriptor,
        IEnumerable<string>? redirectUris = null,
        IEnumerable<string>? additionalScopes = null)
    {
        descriptor.ClientType = OpenIddictConstants.ClientTypes.Public;
        descriptor.ConsentType = OpenIddictConstants.ConsentTypes.Implicit;

        foreach (var uri in redirectUris ?? DefaultPublicRedirectUris)
            descriptor.RedirectUris.Add(new Uri(uri, UriKind.Absolute));

        foreach (var permission in PublicAuthorizationCodePermissions)
            descriptor.Permissions.Add(permission);

        if (additionalScopes is null)
            return;

        foreach (var scope in additionalScopes)
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.Scope + scope);
    }

    public static void ApplyConfidentialClientCredentials(
        OpenIddictApplicationDescriptor descriptor,
        IEnumerable<string> scopes)
    {
        descriptor.ClientType = OpenIddictConstants.ClientTypes.Confidential;

        descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Token);
        descriptor.Permissions.Add(OpenIddictConstants.Permissions.GrantTypes.ClientCredentials);

        foreach (var scope in scopes)
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.Scope + scope);
    }

    public static void ApplyConfidentialIntrospection(OpenIddictApplicationDescriptor descriptor)
    {
        descriptor.ClientType = OpenIddictConstants.ClientTypes.Confidential;
        descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Introspection);
        descriptor.Permissions.Add(OpenIddictConstants.Permissions.GrantTypes.ClientCredentials);
    }

    public static readonly ImmutableArray<string> DefaultPublicRedirectUris =
    [
        "http://127.0.0.1/callback",
        "http://localhost/callback",
        "geek://callback"
    ];

    private static readonly ImmutableArray<string> PublicAuthorizationCodePermissions =
    [
        OpenIddictConstants.Permissions.Endpoints.Authorization,
        OpenIddictConstants.Permissions.Endpoints.Token,
        OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
        OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
        OpenIddictConstants.Permissions.ResponseTypes.Code,
        OpenIddictConstants.Permissions.Scopes.Email,
        OpenIddictConstants.Permissions.Scopes.Profile,
        OpenIddictConstants.Permissions.Prefixes.Scope + OpenIddictConstants.Scopes.OfflineAccess,
        OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange
    ];
}
