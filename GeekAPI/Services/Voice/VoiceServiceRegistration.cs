using GeekAPI.HttpClients;
using GeekAPI.Services.Voice.Billing;
using GeekAPI.Services.Voice.Calls;
using GeekAPI.Services.Voice.Email;
using GeekAPI.Services.Voice.Mcp;
using GeekAPI.Services.Voice.Storage;
using GeekAPI.Services.Voice.Onboarding;
using GeekAPI.Services.Voice.OwnerAuth;
using GeekAPI.Services.Voice.Telephony;
using GeekApplication.Interfaces.Receptionist;
using GeekApplication.Interfaces.Voice;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection.Extensions;

using GeekAPI.Services.Receptionist.Agents;
using GeekAPI.Services.Receptionist.Knowledge;

namespace GeekAPI.Services.Voice;

public static class VoiceServiceRegistration
{
    /// <summary>
    /// Geek-Receptionist's persistence and its onboarding gate: the store over /internal/v1/*, which uses the
    /// "GeekRepositoryInternal" client Program.cs builds with X-Internal-Api-Key, and the eligibility gate.
    /// </summary>
    public static IServiceCollection AddVoiceOnboarding(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IVoiceStore>(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("GeekRepositoryInternal");
            var logger = sp.GetRequiredService<ILogger<HttpVoiceRepository>>();
            return new HttpVoiceRepository(httpClient, logger);
        });
        services.AddScoped<IVoiceSignupAnswerStore>(sp =>
            new HttpVoiceRepository(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("GeekRepositoryInternal"),
                sp.GetRequiredService<ILogger<HttpVoiceRepository>>()));
        services.AddScoped<OnboardingEligibility>();
        services.AddScoped<IndustryRefusals>();
        // The only writer of a whole receptionist config: the signup form's steps, and the owner's own changes afterwards.
        services.AddScoped<ReceptionistConfigWriter>();
        services.AddSingleton<VoiceCredentialProtector>();
        services.AddSingleton(PayPalOptions.FromEnvironment());
        services.AddHttpClient<PayPalBillingVerifier>(client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddHttpClient<PayPalCycleBilling>(client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddSingleton(PayPalPlans.FromEnvironment());
        services.AddHttpClient<PayPalSignup>(client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddScoped<SignupPayments>();
        services.AddScoped<IVoiceBillingStore>(sp =>
            new HttpVoiceBillingRepository(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("GeekRepositoryInternal"),
                sp.GetRequiredService<ILogger<HttpVoiceBillingRepository>>()));
        services.AddScoped<TenantStanding>();
        services.AddScoped<OverageBilling>();
        services.AddScoped<OverageCollection>();
        services.AddHostedService<OverageBillingHostedService>();
        services.AddSingleton<CallOutcomes>();
        services.AddScoped<TwilioCallStatusRecorder>();
        services.AddScoped<TenantTwilioWebhook>();
        services.AddSingleton(TwilioOptions.FromEnvironment());
        services.AddHttpClient<TwilioProvisioner>(client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddSingleton(EmailOptions.FromEnvironment());
        services.AddHttpClient<IEmailSender, ResendEmailSender>(client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddScoped<ICallSummaryDelivery, EmailCallSummaryDelivery>();
        services.AddScoped<SubscriptionActivation>();
        services.AddSingleton(GoogleCalendarOptions.FromEnvironment());
        services.AddSingleton<McpOAuthState>();
        services.AddHttpClient<GoogleCalendarOAuth>(client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddScoped<McpConnectionService>();
        services.AddSingleton(McpGatewayOptions.FromEnvironment());
        services.AddHttpClient<McpGatewayClient>(client => client.Timeout = TimeSpan.FromSeconds(45));
        services.AddScoped<GoogleAccessTokens>();
        services.AddHttpClient(GoogleDriveCallRecordStorage.HttpClientName);
        services.AddHttpClient(S3CompatibleCallRecordStorage.HttpClientName);
        services.AddScoped<GoogleDriveCallRecordStorage>();
        services.AddScoped<S3CompatibleCallRecordStorage>();
        services.AddScoped<CallRecordStorages>();
        services.AddScoped<McpTools>();

        // Answering a caller from the business's own information, as one coordinator over workers that each do one job and decide
        // nothing (Services/Receptionist/Knowledge/KnowledgeOrchestrator.cs).
        services.AddScoped<QuestionFormulator>();
        services.AddScoped<KnowledgePassageJudge>();
        services.AddScoped<AnswerComposer>();
        services.AddScoped<AnswerGrounding>();
        services.AddScoped<KnowledgeOrchestrator>();

        // Who takes the caller's next turn. One thing decides, every turn, so two roles cannot pass a caller back and forth
        // (Services/Receptionist/Agents/ReceptionistDispatcher.cs).
        services.AddScoped<ReceptionistDispatcher>();
        services.AddSingleton<McpToolCatalog>();
        services.AddScoped<IVoiceOwnerStore>(sp =>
            new HttpVoiceOwnerRepository(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("GeekRepositoryInternal"),
                sp.GetRequiredService<ILogger<HttpVoiceOwnerRepository>>()));
        services.AddSingleton(ReceptionistTokenOptions.FromEnvironment());
        services.AddSingleton<ReceptionistTokens>();
        // Made here, at startup, so the decoy hash exists before the first sign-in and the first unknown email takes no longer than the rest.
        services.AddSingleton(new OwnerPasswords());
        services.AddSingleton<OwnerSignInGuard>();
        services.AddScoped<OwnerAuthService>();
        services.AddRateLimiter(options =>
        {
            options.AddPolicy(OnboardingRateLimit.PolicyName, OnboardingRateLimit.Partition);
            options.AddPolicy(OnboardingRateLimit.SignInPolicyName, OnboardingRateLimit.SignInPartition);
            options.OnRejected = (rejected, ct) => OnboardingRateLimit.WriteRejection(rejected.HttpContext, ct);
        });
        return services;
    }
}
