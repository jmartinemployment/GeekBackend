using System.Text;
using GeekAPI.Auth;
using GeekAPI.HttpClients.Seo;
using GeekApplication.Interfaces.Seo;
using GeekApplication.Services.Seo;
using SubscriptionService = GeekApplication.Services.Seo.SubscriptionService;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace GeekAPI.Extensions;

public static class SeoApiExtensions
{
    public static IServiceCollection AddGeekSeoApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserContext, CurrentUserContext>();

        services.AddScoped<IProjectRepository, HttpProjectRepository>();
        services.AddScoped<IContentDocumentRepository, HttpContentDocumentRepository>();
        services.AddScoped<IBackgroundJobRepository, HttpBackgroundJobRepository>();
        services.AddScoped<IProjectService, ProjectService>();
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddScoped<IBackgroundJobService, BackgroundJobService>();
        services.AddScoped<IContentScoringService, HttpContentScoringService>();
        services.AddScoped<ISubscriptionRepository, HttpSubscriptionRepository>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();
        services.AddScoped<IUsageMeteringRepository, HttpUsageMeteringRepository>();
        services.AddScoped<IUsageMeteringService, UsageMeteringService>();
        services.AddScoped<ICompetitorInsightsService, HttpCompetitorInsightsService>();
        services.AddScoped<IAIWritingService, HttpAIWritingService>();
        services.AddScoped<IContentBriefService, HttpContentBriefService>();
        services.AddScoped<IKeywordResearchService, HttpKeywordResearchService>();
        services.AddScoped<IWordPressPublishService, HttpWordPressPublishService>();
        services.AddSignalR();

        var authServerUrl = Environment.GetEnvironmentVariable("AUTH_SERVER_URL");
        var jwtKey = Environment.GetEnvironmentVariable("JWT_KEY");

        if (!string.IsNullOrWhiteSpace(authServerUrl))
        {
            var authority = authServerUrl.TrimEnd('/');
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.Authority = authority;
                    options.RequireHttpsMetadata = !authority.Contains("localhost", StringComparison.OrdinalIgnoreCase);
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = false,
                        ValidateLifetime = true,
                        NameClaimType = "sub",
                        ClockSkew = TimeSpan.FromMinutes(1),
                    };
                    options.Events = JwtBearerEvents();
                });
            services.AddAuthorization();
        }
        else if (!string.IsNullOrWhiteSpace(jwtKey))
        {
            var keyBytes = Convert.FromBase64String(jwtKey);
            var issuer = configuration["Jwt:Authority"] ?? Environment.GetEnvironmentVariable("JWT_ISSUER") ?? "https://api.geekatyourspot.com";
            var audience = configuration["Jwt:Audience"] ?? Environment.GetEnvironmentVariable("JWT_AUDIENCE") ?? "geekseo";

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        ValidIssuer = issuer,
                        ValidAudience = audience,
                        IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
                        ClockSkew = TimeSpan.FromMinutes(1),
                    };
                    options.Events = JwtBearerEvents();
                });
            services.AddAuthorization();
        }

        return services;
    }

    private static JwtBearerEvents JwtBearerEvents() => new()
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/seo-scoring"))
                context.Token = accessToken;
            return Task.CompletedTask;
        },
    };
}
