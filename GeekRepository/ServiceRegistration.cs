using Microsoft.Extensions.DependencyInjection;
using GeekApplication.Interfaces;
using GeekApplication.Interfaces.Seo;
using GeekApplication.Services.Seo;
using GeekRepository.Infrastructure;
using GeekRepository.Repositories;
using GeekRepository.Repositories.JtiBlacklist;
using GeekRepository.Providers.Seo;
using GeekRepository.Repositories.OpenIddict;
using GeekRepository.Repositories.Seo;
using GeekApplication.Interfaces.OpenIddict;

namespace GeekRepository;

public static class ServiceRegistration
{
    public static IServiceCollection AddGeekRepository(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddSingleton<IDbConnectionFactory>(
            _ => new NpgsqlConnectionFactory(connectionString));

        // Auth repositories
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserSecretsRepository, UserSecretsRepository>();
        services.AddScoped<IUserClaimsRepository, UserClaimsRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IDeviceOauthRepository, DeviceOauthRepository>();
        services.AddScoped<ITwoFactorRepository, TwoFactorRepository>();
        services.AddScoped<IDeviceReregistrationRepository, DeviceReregistrationRepository>();
        services.AddScoped<ISyncRepository, SyncRepository>();
        services.AddScoped<IAuditRepository, AuditRepository>();
        services.AddScoped<ISecurityIncidentRepository, SecurityIncidentRepository>();
        services.AddScoped<IOAuthClientRepository, OAuthClientRepository>();
        services.AddScoped<IOidcStorageRepository, OidcStorageRepository>();
        services.AddScoped<IOAuthTokenRepository, OAuthTokenRepository>();
        services.AddScoped<IPendingVerificationRepository, PendingVerificationRepository>();

        services.AddScoped<IOpenIddictApplicationRepository, DapperApplicationRepository>();
        services.AddScoped<IOpenIddictScopeRepository, DapperScopeRepository>();
        services.AddScoped<IOpenIddictAuthorizationRepository, DapperAuthorizationRepository>();
        services.AddScoped<IOpenIddictTokenRepository, DapperTokenRepository>();

        services.AddSingleton<IJtiBlacklist, PostgresJtiBlacklistRepository>();

        // Content repositories
        services.AddScoped<ICaseStudyRepository, CaseStudyRepository>();
        services.AddScoped<IDepartmentRepository, DepartmentRepository>();
        services.AddScoped<IUseCaseRepository, UseCaseRepository>();

        // Geek SEO
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<IContentDocumentRepository, ContentDocumentRepository>();
        services.AddScoped<IBackgroundJobRepository, BackgroundJobRepository>();
        services.AddScoped<IProjectService, ProjectService>();
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddScoped<IBackgroundJobService, BackgroundJobService>();
        services.AddScoped<ISerpCacheRepository, SerpCacheRepository>();
        services.AddScoped<IRichTextProvider, HtmlRichTextProvider>();
        services.AddHttpClient("DataForSEO", client =>
        {
            client.BaseAddress = new Uri("https://api.dataforseo.com");
            client.Timeout = TimeSpan.FromSeconds(60);
        });
        services.AddScoped<ISerpProvider, DataForSEOSerpProvider>();
        services.AddScoped<ICompetitorPageRepository, CompetitorPageRepository>();
        services.AddScoped<CompetitorCrawlService>();
        services.AddScoped<IContentScoringService, ContentScoringService>();
        services.AddScoped<ISubscriptionRepository, SubscriptionRepository>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();
        services.AddScoped<IUsageMeteringRepository, UsageMeteringRepository>();
        services.AddScoped<IUsageMeteringService, UsageMeteringService>();
        services.AddScoped<IAIWritingService, AIWritingService>();
        services.AddScoped<IContentBriefService, ContentBriefService>();
        services.AddScoped<ICompetitorInsightsService, CompetitorInsightsService>();
        services.AddScoped<IAIProvider, ClaudeProvider>();
        services.AddHttpClient("Anthropic", c => c.BaseAddress = new Uri("https://api.anthropic.com"));
        services.AddHostedService<Workers.FullArticleJobWorker>();
        services.AddScoped<IKeywordProvider, DataForSEOKeywordProvider>();
        services.AddScoped<IKeywordRepository, KeywordRepository>();
        services.AddScoped<IKeywordResearchService, KeywordResearchService>();
        services.AddScoped<IWordPressProvider, WordPressRestProvider>();
        services.AddScoped<IWordPressConnectionRepository, WordPressConnectionRepository>();
        services.AddScoped<IWordPressPublishRepository, WordPressPublishRepository>();
        services.AddScoped<IWordPressPublishService, WordPressPublishService>();
        services.AddHttpClient("WordPress");

        return services;
    }
}
