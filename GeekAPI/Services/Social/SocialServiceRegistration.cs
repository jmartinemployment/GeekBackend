using GeekAPI.HttpClients;
using GeekAPI.Services.Social.Connector;
using GeekAPI.Services.Social.Drafting;
using GeekAPI.Services.Social.LanguageModel;
using GeekAPI.Services.Social.Listener;
using GeekAPI.Services.Social.Meta;
using GeekAPI.Services.Social.Orchestration;
using GeekAPI.Services.Social.Publishing;
using GeekAPI.Services.Social.Realtime;
using GeekAPI.Services.Social.Review;
using GeekAPI.Services.Social.Sending;
using GeekAPI.Services.Social.Triage;
using GeekApplication.Interfaces.Social;
using GeekApplication.Models.Social;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GeekAPI.Services.Social;

public static class SocialServiceRegistration
{
    /// <summary>
    /// GeekSocial's persistence: the store over repo/social/* and the protector that encrypts
    /// tokens before they reach it. The protector needs GEEK_SOCIAL_ENCRYPTION_KEY only when a
    /// token is stored or read, so a missing key does not stop the service from booting; the
    /// first operation that needs it fails with encryption_unavailable.
    /// </summary>
    public static IServiceCollection AddSocialPersistence(this IServiceCollection services)
    {
        services.AddScoped<ISocialStore>(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("GeekRepository");
            var logger = sp.GetRequiredService<ILogger<HttpSocialRepository>>();
            return new HttpSocialRepository(httpClient, logger);
        });

        services.AddSingleton<SocialCredentialProtector>();
        services.AddScoped<ISocialTokenProvider, SocialTokenProvider>();
        return services;
    }

    /// <summary>
    /// The agents. The Reviewer is plain code with no state, so one instance serves every caller.
    /// Triage, the drafter and the post writer reach the language model through
    /// <see cref="ISocialModelClient"/>; the one client registered is the provider
    /// <c>LlmProviders__DefaultProvider</c> names (<see cref="SocialModelSettings"/>), or
    /// <see cref="UnconfiguredSocialModelClient"/> when it names none. Every client uses the global
    /// <see cref="Workflow.Providers.LlmConcurrencyGate"/> that AddWorkflow registers. The settings and
    /// the clearance are read here, once: a provider or a platform name that does not exist stops the
    /// service from starting, and an empty clearance clears nothing.
    /// </summary>
    public static IServiceCollection AddSocialAgents(this IServiceCollection services) =>
        services.AddSocialAgents(Environment.GetEnvironmentVariable);

    /// <summary>The agents, with GeekAPI's settings read through <paramref name="readEnvironment"/>, so a test chooses the provider.</summary>
    internal static IServiceCollection AddSocialAgents(this IServiceCollection services, Func<string, string?> readEnvironment)
    {
        var settings = SocialModelSettings.From(readEnvironment);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IOutboundTextReviewer, OutboundTextReviewer>();
        services.AddSingleton(settings);
        services.AddSingleton(SocialAiClearance.From(readEnvironment));
        switch (settings.Provider)
        {
            case SocialModelProviders.Anthropic:
                services.AddHttpClient<ISocialModelClient, AnthropicSocialModelClient>();
                break;
            case SocialModelProviders.OpenAi:
                services.AddHttpClient<ISocialModelClient, OpenAiSocialModelClient>();
                break;
            case SocialModelProviders.Gemini:
                services.AddHttpClient<ISocialModelClient, GeminiSocialModelClient>();
                break;
            case SocialModelProviders.Perplexity:
                services.AddHttpClient<ISocialModelClient, PerplexitySocialModelClient>();
                break;
            case SocialModelProviders.Groq:
                services.AddHttpClient<ISocialModelClient, GroqSocialModelClient>();
                break;
            case null:
                services.AddSingleton<ISocialModelClient, UnconfiguredSocialModelClient>();
                break;
            default:
                throw new InvalidOperationException($"No GeekSocial model client is written for the provider '{settings.Provider}'.");
        }

        services.AddScoped<IEngagementTriage, EngagementTriage>();
        services.AddScoped<IReplyDrafter, ReplyDrafter>();
        services.AddScoped<IPostWriter, PostWriter>();
        return services;
    }

    /// <summary>
    /// The Listener's Facebook reader and the orchestrator's intake. The Meta settings are read once
    /// here and a missing one fails the first request that needs it (not_configured), so the service
    /// still boots without them. The reader is registered under the platform string, because every
    /// platform is active at once and each has its own.
    /// </summary>
    public static IServiceCollection AddSocialIntake(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(MetaOptions.FromEnvironment());
        // The default HttpClient request log writes each address at Information. Meta's addresses carry
        // ids and, for debug_token, the token being checked, so this client logs nothing itself; every
        // failure is logged by the caller with its code.
        services.AddHttpClient<MetaGraphClient>(client =>
        {
            client.BaseAddress = new Uri("https://graph.facebook.com/");
            client.Timeout = TimeSpan.FromSeconds(20);
        }).RemoveAllLoggers();
        services.AddKeyedScoped<ISocialEngagementReader>(
            SocialPlatforms.Facebook,
            (sp, _) => new MetaEngagementReader(
                sp.GetRequiredService<MetaOptions>(),
                sp.GetRequiredService<MetaGraphClient>(),
                sp.GetRequiredService<TimeProvider>()));

        services.AddSingleton<SocialWake>();
        services.AddSingleton<ISocialRealtime, SocialRealtimeNotifier>();
        services.AddScoped<SocialResync>();

        // One orchestrator: the hosted service and the instance the operator routes call are the same.
        services.AddSingleton<SocialOrchestrator>();
        services.AddHostedService(sp => sp.GetRequiredService<SocialOrchestrator>());
        return services;
    }

    /// <summary>
    /// The Connector, the Sender and the send step, and the operator's actions. The Connector, the
    /// publisher and the writer are registered under the platform string, as the reader is. Only
    /// Facebook has them today; a platform without one answers not_supported or not_configured.
    /// </summary>
    public static IServiceCollection AddSocialSending(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddDataProtection();
        services.AddKeyedScoped<ISocialAccountConnector>(
            SocialPlatforms.Facebook,
            (sp, _) => new MetaAccountConnector(
                sp.GetRequiredService<MetaOptions>(),
                sp.GetRequiredService<MetaGraphClient>(),
                sp.GetRequiredService<TimeProvider>()));
        services.AddKeyedScoped<ISocialPublisher>(
            SocialPlatforms.Facebook,
            (sp, _) => new MetaPublisher(sp.GetRequiredService<MetaGraphClient>(), sp.GetRequiredService<TimeProvider>()));
        services.AddKeyedScoped<ISocialEngagementWriter>(
            SocialPlatforms.Facebook,
            (sp, _) => new MetaEngagementWriter(sp.GetRequiredService<MetaGraphClient>()));

        services.AddScoped<SocialSender>();
        services.AddScoped<ISocialSendStep, SocialSendStep>();
        services.AddScoped<SocialOperatorActions>();
        services.AddScoped<SocialConnectService>();
        return services;
    }
}
