using GeekRepository.Auth;
using GeekRepository.Middleware;
using Microsoft.AspNetCore.Authorization;

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

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = RepositoryAuthConstants.KeySelectorScheme;
            options.DefaultChallengeScheme = RepositoryAuthConstants.KeySelectorScheme;
        })
        .AddPolicyScheme(RepositoryAuthConstants.KeySelectorScheme, displayName: null, options =>
        {
            // One policy, two credentials, chosen by path: /internal/v1/* is judged by X-Internal-Api-Key and everything else by
            // X-Repo-Key. A request cannot pick its own credential.
            options.ForwardDefaultSelector = context =>
                context.Request.Path.StartsWithSegments(RepositoryAuthConstants.InternalRoutePrefix, StringComparison.OrdinalIgnoreCase)
                    ? InternalApiKeyAuthenticationHandler.SchemeName
                    : RepoApiKeyAuthenticationHandler.SchemeName;
        })
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
