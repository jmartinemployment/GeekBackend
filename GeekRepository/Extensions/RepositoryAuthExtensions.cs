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

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = RepoApiKeyAuthenticationHandler.SchemeName;
            options.DefaultChallengeScheme = RepoApiKeyAuthenticationHandler.SchemeName;
        })
        .AddScheme<RepoApiKeyAuthenticationOptions, RepoApiKeyAuthenticationHandler>(
            RepoApiKeyAuthenticationHandler.SchemeName,
            options => options.ExpectedKey = expectedKey);

        services.AddAuthorizationBuilder()
            .AddPolicy(RepositoryAuthConstants.InternalServicePolicy, policy =>
            {
                policy.AddAuthenticationSchemes(RepoApiKeyAuthenticationHandler.SchemeName);
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
