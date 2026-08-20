using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services;
using GeekAPI.Services.Workflow.Services.Export;
using GeekAPI.Services.Workflow.Services.JsonLd;
using GeekAPI.Services.Workflow.Services.PromptBuilders;
using GeekAPI.Services.Workflow.Services.Review;
using GeekAPI.Services.Workflow.Services.SchemaBuilders;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.Infrastructure;
using GeekAPI.Services.Workflow.Infrastructure.InMemory;

namespace GeekAPI.Services.Workflow.Hosting;

public static class WorkflowServiceRegistration
{
    /// <summary>
    /// <paramref name="persistenceStoreFactory"/> lets a host that already owns a trusted
    /// GeekRepository connection (i.e. GeekAPI, post-merge) supply an <see cref="IPersistenceStore"/>
    /// backed by it, reusing that host's own credential — this project never constructs its own
    /// GeekRepository client or holds that credential (see AGENTS.md "Persistence and target
    /// architecture"). Defaults to the local filesystem when omitted (standalone/dev use).
    /// </summary>
    public static IServiceCollection AddWorkflow(
        this IServiceCollection services,
        IConfiguration configuration,
        Func<IServiceProvider, IPersistenceStore>? persistenceStoreFactory = null)
    {
        var dataDirectory = configuration["ContentWriter:DataDirectory"] ?? "./data";
        services.AddSingleton<IPersistenceStore>(sp =>
            persistenceStoreFactory?.Invoke(sp)
            ?? new FileSystemPersistenceStore(dataDirectory, sp.GetRequiredService<ILogger<FileSystemPersistenceStore>>()));

        // Project/Client stores with durable backing
        services.AddSingleton<IProjectStore>(sp =>
            new PersistentProjectStore(
                sp.GetRequiredService<IPersistenceStore>(),
                sp.GetRequiredService<IClientStore>(),
                sp.GetRequiredService<ILogger<PersistentProjectStore>>()));

        services.AddSingleton<IClientStore>(sp =>
            new PersistentClientStore(
                sp.GetRequiredService<IPersistenceStore>(),
                sp.GetRequiredService<ILogger<PersistentClientStore>>()));

        services.Configure<LlmProvidersOptions>(configuration.GetSection(LlmProvidersOptions.SectionName));
        services.Configure<CompanyProfileOptions>(configuration.GetSection(CompanyProfileOptions.SectionName));

        services.AddHttpClient<LmStudioProvider>();
        services.AddHttpClient<OpenAiProvider>();
        services.AddHttpClient<AnthropicProvider>();
        services.AddHttpClient<GroqProvider>();

        var maxConcurrentLlmCalls = configuration.GetValue<int?>("LlmProviders:MaxConcurrentCalls") ?? 4;
        services.AddSingleton(new LlmConcurrencyGate(maxConcurrentLlmCalls));

        services.AddKeyedTransient<IContentGenerationProvider>(LlmProviderType.LmStudio,
            (sp, _) => new ConcurrencyLimitingContentGenerationProvider(
                sp.GetRequiredService<LmStudioProvider>(), sp.GetRequiredService<LlmConcurrencyGate>()));
        services.AddKeyedTransient<IContentGenerationProvider>(LlmProviderType.OpenAi,
            (sp, _) => new ConcurrencyLimitingContentGenerationProvider(
                sp.GetRequiredService<OpenAiProvider>(), sp.GetRequiredService<LlmConcurrencyGate>()));
        services.AddKeyedTransient<IContentGenerationProvider>(LlmProviderType.Anthropic,
            (sp, _) => new ConcurrencyLimitingContentGenerationProvider(
                sp.GetRequiredService<AnthropicProvider>(), sp.GetRequiredService<LlmConcurrencyGate>()));
        services.AddKeyedTransient<IContentGenerationProvider>(LlmProviderType.Groq,
            (sp, _) => new ConcurrencyLimitingContentGenerationProvider(
                sp.GetRequiredService<GroqProvider>(), sp.GetRequiredService<LlmConcurrencyGate>()));

        services.AddScoped<IContentProviderFactory, ContentProviderFactory>();
        services.AddHttpClient<ISiteCrawlerService, SiteCrawlerService>();
        services.AddScoped<IKeywordHtmlParserService, KeywordHtmlParserService>();
        services.AddScoped<IContentPromptBuilder, ContentPromptBuilder>();
        services.AddScoped<ISoftwareApplicationSchemaBuilder, SoftwareApplicationSchemaBuilder>();
        services.AddScoped<ITechnicalArticleSchemaBuilder, TechnicalArticleSchemaBuilder>();
        services.AddScoped<IBlogPostingSchemaBuilder, BlogPostingSchemaBuilder>();
        services.AddScoped<WorkflowSeoBearerContext>();
        services.AddScoped<IToolPageGenerator, ToolPageGenerator>();
        services.AddScoped<IContentGenerationOrchestrator, ContentGenerationOrchestrator>();
        services.AddSingleton<ToolsGenerationJobStore>();
        services.AddSingleton<ToolsGenerationJobRunner>();
        services.AddScoped<IHtmlExportService, HtmlExportService>();
        services.AddHttpClient("GitHub");
        services.AddScoped<IGeekatyourspotCommitService, GeekatyourspotCommitService>();
        services.AddSingleton<IJsonLdParserService, JsonLdParserService>();

        services.AddScoped<IEditorialReviewService, EditorialReviewService>();
        services.AddScoped<IReviewLoopService, ReviewLoopService>();

        return services;
    }

    /// <summary>Hydrates all persisted clients and projects from storage at startup.</summary>
    public static async Task HydrateWorkflowAsync(
        this WebApplication app,
        CancellationToken cancellationToken = default)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Workflow.Startup");

        await using var scope = app.Services.CreateAsyncScope();
        var clientStore = scope.ServiceProvider.GetRequiredService<IClientStore>();
        var projectStore = scope.ServiceProvider.GetRequiredService<IProjectStore>();

        const string DefaultClientName = "Geek At Your Spot";

        // Hydrate persisted clients first
        if (clientStore is PersistentClientStore persistentClientStore)
        {
            await persistentClientStore.HydrateAsync(cancellationToken);
        }

        // Then hydrate persisted projects (which rehydrate their Client refs)
        if (projectStore is PersistentProjectStore persistentProjectStore)
        {
            await persistentProjectStore.HydrateAsync(cancellationToken);
        }

        // If no clients exist (fresh start), seed the default
        if (!await clientStore.AnyAsync(cancellationToken))
        {
            var client = new Client { Name = DefaultClientName };
            client.PublishTarget = new PublishTarget
            {
                ClientId = client.Id,
                GeekBackendApiBaseUrl = "https://api.geekatyourspot.com",
                OAuthTokenEndpoint = "api/oauth/token",
                ClientIdEnvVar = "GEEKATYOURSPOT_OAUTH_CLIENT_ID",
                ClientSecretEnvVar = "GEEKATYOURSPOT_OAUTH_CLIENT_SECRET",
                CategoryStrategy = CategoryStrategy.DepartmentBased,
            };

            await clientStore.AddAsync(client, cancellationToken);
            logger.LogInformation("Seeded default client '{ClientName}' ({ClientId}).", DefaultClientName, client.Id);
        }

        logger.LogInformation("Workflow initialization complete.");
    }
}
