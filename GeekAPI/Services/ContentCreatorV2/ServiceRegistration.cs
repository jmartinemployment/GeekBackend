using GeekAPI.Controllers.ContentCreatorV2.Hubs;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreator;
using GeekApplication.Models.ContentCreator;
using GeekAPI.Services.ContentCreatorV2.Adapters;
using GeekAPI.Services.ContentCreatorV2.BrandKit;
using GeekAPI.Services.ContentCreatorV2.Geo;
using GeekAPI.Services.ContentCreatorV2.Jobs;
using GeekAPI.Services.ContentCreatorV2.Guardrail;
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
        services.AddHttpClient<GccPartnerUrlResearchService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(GccPartnerResearchCaps.FetchTimeoutSeconds);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "GeekContentCreatorPartnerResearch/1.0 (+https://geekatyourspot.com)");
        });
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
