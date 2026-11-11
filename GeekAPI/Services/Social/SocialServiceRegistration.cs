using GeekAPI.HttpClients;
using GeekAPI.Services.Social.Connector;
using GeekAPI.Services.Social.Drafting;
using GeekAPI.Services.Social.LanguageModel;
using GeekAPI.Services.Social.Meta;
using GeekAPI.Services.Social.Orchestration;
using GeekAPI.Services.Social.Review;
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
    /// Triage and the drafter reach the language model through <see cref="ISocialModelClient"/>, whose
    /// default is Claude (<see cref="ClaudeSocialModelClient"/>); it uses the Anthropic settings and the
    /// global <see cref="Workflow.Providers.LlmConcurrencyGate"/> that AddWorkflow registers. The
    /// clearance is read here, once: a platform name in it that does not exist stops the service from
    /// starting, and an empty one clears nothing.
    /// </summary>
    public static IServiceCollection AddSocialAgents(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IOutboundTextReviewer, OutboundTextReviewer>();
        services.AddSingleton(SocialClaudeClearance.FromEnvironment());
        services.AddHttpClient<ISocialModelClient, ClaudeSocialModelClient>();
        services.AddScoped<IEngagementTriage, EngagementTriage>();
        services.AddScoped<IReplyDrafter, ReplyDrafter>();
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
        services.AddHttpClient<MetaGraphClient>(client =>
        {
            client.BaseAddress = new Uri("https://graph.facebook.com/");
            client.Timeout = TimeSpan.FromSeconds(20);
        });
        services.AddKeyedScoped<ISocialEngagementReader>(
            SocialPlatforms.Facebook,
            (sp, _) => new MetaEngagementReader(
                sp.GetRequiredService<MetaOptions>(),
                sp.GetRequiredService<MetaGraphClient>(),
                sp.GetRequiredService<TimeProvider>()));

        services.AddSingleton<SocialWake>();
        services.AddSingleton<SocialOrchestratorStatus>();
        services.AddHostedService<SocialOrchestrator>();
        return services;
    }
}
