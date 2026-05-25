using GeekApplication.Auth;
using GeekRepository.Middleware;
using Microsoft.AspNetCore.Authorization;
using OpenIddict.Validation.AspNetCore;

namespace GeekRepository.Extensions;

public static class RepositoryAuthExtensions
{
    public static IServiceCollection AddGeekRepositoryAuth(
        this IServiceCollection services,
        IHostEnvironment environment)
    {
        var issuer = Environment.GetEnvironmentVariable("AUTH_SERVER_URL");
        if (string.IsNullOrWhiteSpace(issuer) && environment.IsProduction())
        {
            throw new InvalidOperationException(
                "AUTH_SERVER_URL must be set in Production so GeekRepository can validate GeekAPI OAuth tokens.");
        }

        var defaultScheme = string.IsNullOrWhiteSpace(issuer)
            ? RepoApiKeyAuthenticationHandler.SchemeName
            : OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;

        var authentication = services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = defaultScheme;
            options.DefaultChallengeScheme = defaultScheme;
        });

        authentication.AddScheme<RepoApiKeyAuthenticationOptions, RepoApiKeyAuthenticationHandler>(
            RepoApiKeyAuthenticationHandler.SchemeName,
            _ => { });

        if (!string.IsNullOrWhiteSpace(issuer))
        {
            services.AddOpenIddict()
                .AddValidation(options =>
                {
                    options.SetIssuer(new Uri(issuer.TrimEnd('/')));
                    options.AddAudiences(GeekOAuthConstants.GeekRepositoryAudience);
                    options.UseSystemNetHttp();
                    options.UseAspNetCore();
                });
        }

        services.AddAuthorizationBuilder()
            .AddPolicy(GeekOAuthConstants.InternalServicePolicy, policy =>
            {
                if (!string.IsNullOrWhiteSpace(issuer))
                {
                    policy.AddAuthenticationSchemes(
                        OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme,
                        RepoApiKeyAuthenticationHandler.SchemeName);
                }
                else
                {
                    policy.AddAuthenticationSchemes(RepoApiKeyAuthenticationHandler.SchemeName);
                }

                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context =>
                    context.User.Claims.Any(static claim =>
                        (claim.Type is "scope" or "scp")
                        && claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                            .Contains(GeekOAuthConstants.InternalApiScope, StringComparer.Ordinal)));
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
