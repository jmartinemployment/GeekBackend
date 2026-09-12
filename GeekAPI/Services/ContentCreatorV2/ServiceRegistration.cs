using GeekAPI.Controllers.ContentCreatorV2.Hubs;
using GeekAPI.HttpClients;
using GeekApplication.Models.ContentCreator;
using GeekAPI.Services.ContentCreatorV2.Adapters;
using GeekAPI.Services.ContentCreatorV2.AgentTests;
using GeekAPI.Services.ContentCreatorV2.BrandKit;
using GeekAPI.Services.ContentCreatorV2.Carousel;
using GeekAPI.Services.ContentCreatorV2.Context;
using GeekAPI.Services.ContentCreatorV2.Geo;
using GeekAPI.Services.ContentCreatorV2.Jobs;
using GeekAPI.Services.ContentCreatorV2.Generation;
using GeekAPI.Services.ContentCreatorV2.Guardrail;
using GeekAPI.Services.ContentCreatorV2.Hierarchy;
using GeekAPI.Services.ContentCreatorV2.GeekCrawler;
using GeekAPI.Services.ContentCreatorV2.ProjectSite;
using GeekAPI.Services.ContentCreatorV2.Plan;
using GeekAPI.Services.ContentCreatorV2.Publish;
using GeekAPI.Services.ContentCreatorV2.ToolPages;
using GeekAPI.Services.ContentCreatorV2.TaskAgents;
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
    public static IServiceCollection AddContentCreatorV2(this IServiceCollection services, IConfiguration configuration)
    {
        var projectSiteOptions = GccV2ProjectSiteCrawlOptions.FromConfiguration(configuration);
        services.AddSingleton(projectSiteOptions);

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
        services.AddScoped<GccV2ImagePromptSpawnService>();
        services.AddScoped<GccV2ToolPagePromptBuilder>();
        services.AddScoped<GccV2ToolResearchExtractor>();
        services.AddScoped<GccV2PartnerToolWriteService>();
        services.AddScoped<GccV2ToolOverviewWriteService>();
        services.AddScoped<GccV2ToolPageSpawnService>();
        services.AddScoped<IGccV2GeekCrawlerReadRepository>(sp =>
        {
            var inner = sp.GetRequiredService<HttpGeekCrawlerRepository>();
            return new GccV2GeekCrawlerReadRepository(inner);
        });
        services.AddScoped<IGccV2ProjectSitePageReader, GccV2ProjectSitePageReader>();
        services.AddScoped<GccV2GeekCrawlerResearchResolver>();
        services.AddSingleton<GccV2ProjectSiteCrawlWake>();
        services.AddSingleton<GccV2ProjectSiteCrawlRunCoordinator>();
        services.AddScoped<GccV2ProjectSiteCrawlProgressNotifier>();
        services.AddScoped<GccV2ProjectSiteBfsCrawler>();
        services.AddScoped<GccV2ProjectSiteCrawlService>();
        services.AddHostedService<GccV2ProjectSiteCrawlWorker>();
        services.AddHostedService<GccV2ProjectSiteStallRecoveryHostedService>();
        services.AddScoped<GccV2BrandKitBuilder>();
        services.AddSingleton<GccV2PlaywrightBrowserHolder>();
        services.AddHostedService<GccV2PlaywrightStartupHostedService>();
        services.AddScoped<GccV2PageFetcher>();
        services.AddScoped<IGccV2RenderedHtmlSource, GccV2PlaywrightRenderedHtmlSource>();
        services.AddScoped<GccV2SiteHierarchyService>();
        services.AddScoped<GccV2ContextAdapter>();
        services.AddScoped<GccV2ProjectSiteKnowledgeService>();
        services.AddHostedService<GccV2DiagnosticTaskAgentSeeder>();
        services.AddHostedService<GccV2ContentTaskAgentSeeder>();
        services.AddHostedService<GccV2RoiTaskAgentSeeder>();
        services.AddHostedService<GccV2TaskRunWorker>();
        services.AddSingleton<GccV2ContextManifestSigner>();
        services.AddScoped<GccV2ContextResolver>();
        services.AddHttpClient<IGccV2ContextObjectStore, GccV2S3ContextObjectStore>();
        services.AddSingleton<IGccV2MalwareScanner, GccV2ClamAvMalwareScanner>();
        services.AddSingleton<IGccV2LocalOcrEngine>(_ =>
            GccV2LocalOcrEnv.IsConfigured
                ? new GccV2TesseractCliOcrEngine()
                : new GccV2DisabledLocalOcrEngine());
        services.AddSingleton<GccV2DocumentExtractor>();
        services.AddSingleton<IGccV2ContextConnector, GccV2UrlContextConnector>();
        services.AddSingleton<IGccV2ContextConnector, GccV2GscContextConnector>();
        services.AddSingleton<IGccV2ContextConnector, GccV2DriveContextConnector>();
        services.AddSingleton<IGccV2ContextConnector, GccV2SharePointContextConnector>();
        services.AddSingleton<GccV2ContextConnectorRegistry>();
        services.AddScoped<GccV2UrlKnowledgeService>();
        services.AddScoped<GccV2UrlAttachmentService>();
        services.AddScoped<GccV2GscKnowledgeService>();
        services.AddScoped<GccV2DriveKnowledgeService>();
        services.AddScoped<GccV2SharePointKnowledgeService>();
        services.AddHttpClient<IGccV2KnowledgeIndexer, GccV2HttpKnowledgeIndexer>(client =>
        {
            var baseUrl = (Environment.GetEnvironmentVariable("GEEK_CRAWLER_RAG_URL") ?? "").Trim().TrimEnd('/');
            if (!string.IsNullOrWhiteSpace(baseUrl)) client.BaseAddress = new Uri(baseUrl + "/");
            var apiKey = (Environment.GetEnvironmentVariable("GEEK_CRAWLER_RAG_API_KEY") ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(apiKey))
                client.DefaultRequestHeaders.TryAddWithoutValidation("X-Api-Key", apiKey);
            client.Timeout = TimeSpan.FromMinutes(5);
        });
        services.AddSingleton<GccV2ContextIngestionWake>();
        services.AddScoped<GccV2ContextIngestionNotifier>();
        services.AddHostedService<GccV2ContextIngestionWorker>();
        services.AddHostedService<GccV2ContextIngestionListenService>();
        services.AddHostedService<GccV2ContextRetentionWorker>();
        services.AddSingleton<ContentModelPolicy>();
        services.AddSingleton<GccV2SkillAdminPolicy>();
        services.AddScoped<GccV2AgenticSkillsResolver>();
        services.AddSingleton<GccV2SkillSnapshotSigner>();
        services.AddScoped<GccV2SkillSnapshotRegistry>();
        services.AddSingleton<GccV2AgentTeamSigner>();
        services.AddScoped<GccV2AgentTeamResolver>();
        services.AddScoped<GccV2AgentExecutionFactory>();
        services.AddScoped<GccV2SpecialistCoordinator>();
        services.AddSingleton<GccV2AgentTestWake>();
        services.AddScoped<GccV2AgentTestProgressNotifier>();
        services.AddScoped<GccV2AgentRagSmokeExecutor>();
        services.AddScoped<GccV2GitHubSkillImporter>();
        services.AddHttpClient(nameof(GccV2GitHubSkillImporter), client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("GeekContentCreator-SkillQuarantine/1.0");
        });
        services.AddHttpClient<GccV2TaskAgentPageHydrator>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(GccPartnerResearchCaps.FetchTimeoutSeconds);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.HttpClientHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = System.Net.DecompressionMethods.All,
            });
        services.AddScoped<GccV2JobModelPolicyOverrideStore>();
        services.AddScoped<GccV2PlanService>();
        services.AddScoped<GccV2ReviewAdapter>();
        services.AddScoped<GccV2GuardrailService>();
        services.AddScoped<GuardrailGateService>();
        services.AddScoped<GccV2RestructurePassService>();
        services.AddScoped<GccV2WriteService>();
        services.AddScoped<GccV2ValidateService>();
        services.AddScoped<GccV2RepurposeTransformService>();
        services.AddScoped<GccV2LinkedInCarouselService>();
        services.AddScoped<GccV2LinkedInCarouselSpawnService>();
        services.AddScoped<GccV2CmsPublishService>();
        services.AddScoped<GccV2JsonLdBuilder>();
        services.AddScoped<GccV2HtmlExportService>();
        services.AddScoped<GccV2AiVisibilityService>();
        services.AddMemoryCache();
        services.AddHostedService<GccV2FirstPartySkillSeeder>();
        services.AddHostedService<GccV2FirstPartyAgentSeeder>();
        services.AddHostedService<GccV2AgentTestWorker>();
        services.AddHostedService<GccV2JobWorker>();
        services.AddHostedService<GccV2JobListenService>();

        services.AddSignalR();
        services.AddSingleton<IUserIdProvider, GccV2SubUserIdProvider>();

        return services;
    }
}
