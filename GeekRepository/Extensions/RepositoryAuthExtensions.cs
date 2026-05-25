using GeekRepository.Auth;
using GeekRepository.Middleware;
using Microsoft.AspNetCore.Authorization;

namespace GeekRepository.Extensions;

public static class RepositoryAuthExtensions
{
    public static IServiceCollection AddGeekRepositoryAuth(this IServiceCollection services)
    {
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = RepoApiKeyAuthenticationHandler.SchemeName;
            options.DefaultChallengeScheme = RepoApiKeyAuthenticationHandler.SchemeName;
        })
        .AddScheme<RepoApiKeyAuthenticationOptions, RepoApiKeyAuthenticationHandler>(
            RepoApiKeyAuthenticationHandler.SchemeName,
            _ => { });

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
