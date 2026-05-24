// OPENIDDICT_VERSION: 7.0.0
// Duende IdentityServer is NOT used — OpenIddict only (.NET-only auth platform).

using System.Security.Claims;
using GeekAPI.Auth;
using GeekAPI.HttpClients.OpenIddict;
using GeekApplication.Entities.OpenIddict;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenIddict.Abstractions;
using OpenIddict.Core;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;

namespace GeekAPI.Extensions;

public static class OpenIddictExtensions
{
    public const string OpenIddictVersion = "7.0.0";

    public static IServiceCollection AddGeekOpenIddict(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddScoped<IOpenIddictApplicationStore<GeekOpenIddictApplication>, HttpApplicationStore>();
        services.TryAddScoped<IOpenIddictScopeStore<GeekOpenIddictScope>, HttpScopeStore>();
        services.TryAddScoped<IOpenIddictAuthorizationStore<GeekOpenIddictAuthorization>, HttpAuthorizationStore>();
        services.TryAddScoped<IOpenIddictTokenStore<GeekOpenIddictToken>, HttpTokenStore>();

        services.AddAuthentication(options =>
        {
            options.DefaultScheme = GeekAuthConstants.CookieScheme;
            options.DefaultChallengeScheme = GeekAuthConstants.CookieScheme;
        })
        .AddCookie(GeekAuthConstants.CookieScheme, options =>
        {
            options.LoginPath = "/Account/Login";
            options.LogoutPath = "/Account/Logout";
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
        });

        services.AddOpenIddict()
            .AddCore(options =>
            {
                options.ReplaceApplicationStore<GeekOpenIddictApplication, HttpApplicationStore>()
                    .ReplaceApplicationManager<GeekOpenIddictApplication, OpenIddictApplicationManager<GeekOpenIddictApplication>>()
                    .ReplaceScopeStore<GeekOpenIddictScope, HttpScopeStore>()
                    .ReplaceScopeManager<GeekOpenIddictScope, OpenIddictScopeManager<GeekOpenIddictScope>>()
                    .ReplaceAuthorizationStore<GeekOpenIddictAuthorization, HttpAuthorizationStore>()
                    .ReplaceAuthorizationManager<GeekOpenIddictAuthorization, OpenIddictAuthorizationManager<GeekOpenIddictAuthorization>>()
                    .ReplaceTokenStore<GeekOpenIddictToken, HttpTokenStore>()
                    .ReplaceTokenManager<GeekOpenIddictToken, OpenIddictTokenManager<GeekOpenIddictToken>>();
            })
            .AddServer(options =>
            {
                var issuer = configuration["AUTH_SERVER_URL"]
                    ?? Environment.GetEnvironmentVariable("AUTH_SERVER_URL");
                if (!string.IsNullOrWhiteSpace(issuer))
                    options.SetIssuer(new Uri(issuer.TrimEnd('/')));

                options.SetAuthorizationEndpointUris("/connect/authorize")
                    .SetTokenEndpointUris("/connect/token")
                    .SetUserInfoEndpointUris("/connect/userinfo")
                    .SetRevocationEndpointUris("/connect/revoke")
                    .SetIntrospectionEndpointUris("/connect/introspect")
                    .SetEndSessionEndpointUris("/connect/logout")
                    .SetConfigurationEndpointUris("/.well-known/openid-configuration");

                options.AllowAuthorizationCodeFlow()
                    .AllowRefreshTokenFlow()
                    .AllowClientCredentialsFlow()
                    .RequireProofKeyForCodeExchange();

                options.UseReferenceRefreshTokens();
                options.SetRefreshTokenReuseLeeway(TimeSpan.FromSeconds(30));
                options.SetAccessTokenLifetime(TimeSpan.FromMinutes(15));
                options.SetRefreshTokenLifetime(TimeSpan.FromDays(30));
                options.SetIdentityTokenLifetime(TimeSpan.FromMinutes(15));

                options.RegisterScopes(
                    OpenIddictConstants.Scopes.OpenId,
                    OpenIddictConstants.Scopes.Profile,
                    OpenIddictConstants.Scopes.Email,
                    OpenIddictConstants.Scopes.OfflineAccess,
                    "devices.manage",
                    "sync.read",
                    "mcp.tools");

                var signingCert = configuration["OPENIDDICT_SIGNING_CERT"];
                if (!string.IsNullOrWhiteSpace(signingCert))
                {
                    // Production: load X509 from env (see Step 18 hardening).
                }
                else
                {
                    options.AddDevelopmentEncryptionCertificate()
                        .AddDevelopmentSigningCertificate();
                }

                options.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()
                    .EnableUserInfoEndpointPassthrough()
                    .EnableEndSessionEndpointPassthrough()
                    .EnableStatusCodePagesIntegration();

                options.AddEventHandler<OpenIddictServerEvents.ProcessSignInContext>(builder =>
                    builder.UseInlineHandler(context =>
                    {
                        if (context.Principal?.Identity is not ClaimsIdentity identity)
                            return default;

                        if (!identity.HasClaim(c => c.Type == OpenIddictConstants.Claims.JwtId))
                        {
                            identity.AddClaim(new Claim(
                                OpenIddictConstants.Claims.JwtId,
                                Guid.NewGuid().ToString(),
                                ClaimValueTypes.String,
                                OpenIddictConstants.Claims.Issuer));
                        }

                        return default;
                    }));
            })
            .AddValidation(options =>
            {
                options.UseLocalServer();
                options.UseAspNetCore();
            });

        // OpenIddict 7 registers untyped IOpenIddict*Manager facades that require EF/Mongo unless
        // we forward them to the typed managers backed by our HTTP stores.
        services.AddScoped<IOpenIddictApplicationManager>(static provider =>
            provider.GetRequiredService<OpenIddictApplicationManager<GeekOpenIddictApplication>>());
        services.AddScoped<IOpenIddictAuthorizationManager>(static provider =>
            provider.GetRequiredService<OpenIddictAuthorizationManager<GeekOpenIddictAuthorization>>());
        services.AddScoped<IOpenIddictScopeManager>(static provider =>
            provider.GetRequiredService<OpenIddictScopeManager<GeekOpenIddictScope>>());
        services.AddScoped<IOpenIddictTokenManager>(static provider =>
            provider.GetRequiredService<OpenIddictTokenManager<GeekOpenIddictToken>>());

        return services;
    }
}
