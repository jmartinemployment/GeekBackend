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

        if (!GeekRepositoryToken.IsP256(publicKey))
        {
            throw new InvalidOperationException(
                $"{GeekRepositoryToken.PublicKeyVariable} is not a key on the P-256 curve; GeekAPI signs with P-256 (ES256) only.");
        }

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<TokenReplayGuard>();
        services.AddSingleton(sp => new GeekApiTokenValidator(
            new ECDsaSecurityKey(publicKey), sp.GetRequiredService<TokenReplayGuard>(), sp.GetRequiredService<TimeProvider>()));

        // One credential: the token GeekAPI signs. There is no key header, no second scheme and no path on which anything else
        // authenticates, so a request that cannot show a valid GeekAPI signature is refused.
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = GeekApiTokenAuthenticationHandler.SchemeName;
            options.DefaultChallengeScheme = GeekApiTokenAuthenticationHandler.SchemeName;
        })
        .AddScheme<GeekApiTokenAuthenticationOptions, GeekApiTokenAuthenticationHandler>(GeekApiTokenAuthenticationHandler.SchemeName, _ => { });

        services.AddAuthorizationBuilder()
            .AddPolicy(RepositoryAuthConstants.InternalServicePolicy, policy =>
            {
                policy.AddAuthenticationSchemes(GeekApiTokenAuthenticationHandler.SchemeName);
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
