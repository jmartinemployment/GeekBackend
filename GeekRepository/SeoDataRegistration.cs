using GeekApplication.Interfaces.Seo;
using GeekApplication.Services.Seo;
using GeekRepository.Repositories.Seo;
using Microsoft.Extensions.DependencyInjection;

namespace GeekRepository;

/// <summary>
/// Geek SEO persistence only — no external SERP/AI/Playwright providers on this host.
/// </summary>
public static class SeoDataRegistration
{
    public static IServiceCollection AddGeekSeoData(this IServiceCollection services)
    {
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<IContentDocumentRepository, ContentDocumentRepository>();
        services.AddScoped<IBackgroundJobRepository, BackgroundJobRepository>();
        services.AddScoped<ISerpCacheRepository, SerpCacheRepository>();
        services.AddScoped<ICompetitorPageRepository, CompetitorPageRepository>();
        services.AddScoped<ISubscriptionRepository, SubscriptionRepository>();
        services.AddScoped<IUsageMeteringRepository, UsageMeteringRepository>();
        services.AddScoped<IKeywordRepository, KeywordRepository>();
        services.AddScoped<IWordPressConnectionRepository, WordPressConnectionRepository>();
        services.AddScoped<IWordPressPublishRepository, WordPressPublishRepository>();
        services.AddScoped<IBrandVoiceRepository, BrandVoiceRepository>();

        services.AddScoped<IProjectService, ProjectService>();
        services.AddScoped<IContentDocumentService, ContentDocumentService>();
        services.AddScoped<IBackgroundJobService, BackgroundJobService>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();
        services.AddScoped<IUsageMeteringService, UsageMeteringService>();

        return services;
    }
}
