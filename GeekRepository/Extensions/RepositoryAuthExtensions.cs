using System.Security.Cryptography;
using GeekApplication.Security;
using GeekRepository.Auth;
using GeekRepository.Middleware;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace GeekRepository.Extensions;

public static class RepositoryAuthExtensions
{
    public static IServiceCollection AddGeekRepositoryAuth(this IServiceCollection services)
    {
        // REPO_API_KEY is the only credential in front of these tables, so an unset one is a
        // misconfiguration this service must not run with. Resolving it here — once, at startup —
        // also means the request path has no branch for a missing key and nothing to fall back to.
        var expectedKey = Environment.GetEnvironmentVariable("REPO_API_KEY");
        if (string.IsNullOrWhiteSpace(expectedKey))
        {
            throw new InvalidOperationException(
                "REPO_API_KEY is not set. GeekRepository holds the credentials for these tables "
                + "and will not start without the key that guards them.");
        }

        // GEEK_REPOSITORY_API_KEY guards /internal/v1/* (docs/Geek-Receptionist.md). It is its own credential, resolved the same way:
        // once, at startup, with no request-time path on which it is missing.
        var internalKey = Environment.GetEnvironmentVariable("GEEK_REPOSITORY_API_KEY");
        if (string.IsNullOrWhiteSpace(internalKey))
        {
            throw new InvalidOperationException(
                "GEEK_REPOSITORY_API_KEY is not set. It guards /internal/v1/*, the receptionist's tenant data, "
                + "and GeekRepository will not start without it.");
        }

        // GeekAPI's public key, the only thing that can say a request came from GeekAPI. Resolved once, at startup, and the service
        // refuses to run without it or with a value that is not a P-256 public key.
        var publicKeyText = Environment.GetEnvironmentVariable(GeekRepositoryToken.PublicKeyVariable);
        if (string.IsNullOrWhiteSpace(publicKeyText))
        {
            throw new InvalidOperationException(
                $"{GeekRepositoryToken.PublicKeyVariable} is not set. It is GeekAPI's public key, the proof that a request came from "
                + "GeekAPI, and GeekRepository will not start without it.");
        }

        ECDsa publicKey;
        try
        {
            publicKey = ECDsa.Create();
            publicKey.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyText.Trim()), out _);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            throw new InvalidOperationException(
                $"{GeekRepositoryToken.PublicKeyVariable} is not a base64 SubjectPublicKeyInfo public key ({ex.GetType().Name}).", ex);
        }

        if (publicKey.KeySize != 256)
        {
            throw new InvalidOperationException(
                $"{GeekRepositoryToken.PublicKeyVariable} is a {publicKey.KeySize}-bit key; GeekAPI signs with P-256 (ES256) only.");
        }

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<TokenReplayGuard>();
        services.AddSingleton(sp => new GeekApiTokenValidator(
            new ECDsaSecurityKey(publicKey), sp.GetRequiredService<TokenReplayGuard>(), sp.GetRequiredService<TimeProvider>()));

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = RepositoryAuthConstants.KeySelectorScheme;
            options.DefaultChallengeScheme = RepositoryAuthConstants.KeySelectorScheme;
        })
        .AddPolicyScheme(RepositoryAuthConstants.KeySelectorScheme, displayName: null, options =>
        {
            // A request carrying Authorization is judged by the token alone, and a bad token is a failure, never a fall back to a key.
            // Without it, during the migration only, one policy and two keys, chosen by path: /internal/v1/* is judged by
            // X-Internal-Api-Key and everything else by X-Repo-Key. A request cannot pick its own credential.
            options.ForwardDefaultSelector = context =>
                context.Request.Headers.ContainsKey("Authorization")
                    ? GeekApiTokenAuthenticationHandler.SchemeName
                    : context.Request.Path.StartsWithSegments(RepositoryAuthConstants.InternalRoutePrefix, StringComparison.OrdinalIgnoreCase)
                        ? InternalApiKeyAuthenticationHandler.SchemeName
                        : RepoApiKeyAuthenticationHandler.SchemeName;
        })
        .AddScheme<GeekApiTokenAuthenticationOptions, GeekApiTokenAuthenticationHandler>(GeekApiTokenAuthenticationHandler.SchemeName, _ => { })
        .AddScheme<RepoApiKeyAuthenticationOptions, RepoApiKeyAuthenticationHandler>(
            RepoApiKeyAuthenticationHandler.SchemeName,
            options => options.ExpectedKey = expectedKey)
        .AddScheme<InternalApiKeyAuthenticationOptions, InternalApiKeyAuthenticationHandler>(
            InternalApiKeyAuthenticationHandler.SchemeName,
            options => options.ExpectedKey = internalKey);

        services.AddAuthorizationBuilder()
            .AddPolicy(RepositoryAuthConstants.InternalServicePolicy, policy =>
            {
                policy.AddAuthenticationSchemes(RepositoryAuthConstants.KeySelectorScheme);
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context =>
                    context.User.Claims.Any(static claim =>
                        (claim.Type is "scope" or "scp")
                        && claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                            .Contains(RepositoryAuthConstants.InternalApiScope, StringComparer.Ordinal)));
            });

        return services;
    }

    public static IApplicationBuilder UseGeekRepositoryAuth(this IApplicationBuilder app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }
}
