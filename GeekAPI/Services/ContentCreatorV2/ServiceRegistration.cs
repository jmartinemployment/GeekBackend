using GeekAPI.Controllers.ContentCreatorV2.Hubs;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.ContentCreator.Polite;
using GeekApplication.Models.ContentCreator;
using GeekAPI.Services.ContentCreatorV2.Adapters;
using GeekAPI.Services.ContentCreatorV2.BrandKit;
using GeekAPI.Services.ContentCreatorV2.Geo;
using GeekAPI.Services.ContentCreatorV2.Jobs;
using GeekAPI.Services.ContentCreatorV2.Guardrail;
using GeekAPI.Services.ContentCreatorV2.Hierarchy;
using GeekAPI.Services.ContentCreatorV2.Plan;
using GeekAPI.Services.ContentCreatorV2.Publish;
using GeekAPI.Services.ContentCreatorV2.Transforms;
using GeekAPI.Services.ContentCreatorV2.Validate;
using GeekAPI.Services.ContentCreatorV2.Write;
using Microsoft.AspNetCore.SignalR;

namespace GeekAPI.Services.ContentCreatorV2;

/// <summary>
/// Additive DI for Content Creator v2. Never replaces v1 GCC registrations.
/// No hosted poll worker — Phase 3 wakes on NOTIFY/Channel only.
/// </summary>
public static class ContentCreatorV2ServiceRegistration
{
    public static IServiceCollection AddContentCreatorV2(this IServiceCollection services)
    {
        services.AddScoped(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient("GeekRepository");
            var logger = sp.GetRequiredService<ILogger<HttpGccV2Repository>>();
            return new HttpGccV2Repository(httpClient, logger);
        });

        services.AddSingleton<GccV2JobWake>();
        services.AddScoped<GccV2ProgressNotifier>();
        services.AddScoped<GccV2JobEventWriter>();
        services.AddScoped<GccV2BrandKitBuilder>();
        services.AddSingleton<GccV2PlaywrightBrowserHolder>();
        services.AddHostedService<GccV2PlaywrightStartupHostedService>();
        services.AddScoped<GccV2PageFetcher>();
        services.AddScoped<GccV2SiteHierarchyService>();
        services.AddSingleton<GccPoliteHostRegistry>();
        services.AddSingleton(TimeProvider.System);
        services.AddHttpClient<IGccPoliteCrawler, GccPoliteCrawler>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(GccPartnerResearchCaps.FetchTimeoutSeconds);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(GccPartnerResearchCaps.UserAgent);
            client.DefaultRequestHeaders.Accept.ParseAdd(
                "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.5");
        })
        .SetHandlerLifetime(TimeSpan.FromMinutes(5));
        services.AddScoped<GccPartnerUrlResearchService>();
        services.AddScoped<GccV2ContextAdapter>();
        services.AddScoped<GccV2PlanService>();
        services.AddScoped<GccV2ReviewAdapter>();
        services.AddScoped<GccV2GuardrailService>();
        services.AddScoped<GuardrailGateService>();
        services.AddScoped<GccV2RestructurePassService>();
        services.AddScoped<GccV2WriteService>();
        services.AddScoped<GccV2ValidateService>();
        services.AddScoped<GccV2RepurposeTransformService>();
        services.AddScoped<GccV2CmsPublishService>();
        services.AddScoped<GccV2AiVisibilityService>();
        services.AddMemoryCache();
        services.AddHostedService<GccV2JobWorker>();
        services.AddHostedService<GccV2JobListenService>();

        services.AddSignalR();
        services.AddSingleton<IUserIdProvider, GccV2SubUserIdProvider>();

        return services;
    }
}
