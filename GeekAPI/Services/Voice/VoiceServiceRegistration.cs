using GeekAPI.HttpClients;
using GeekAPI.Services.Voice.Billing;
using GeekAPI.Services.Voice.Calls;
using GeekAPI.Services.Voice.Email;
using GeekAPI.Services.Voice.Mcp;
using GeekAPI.Services.Voice.Onboarding;
using GeekAPI.Services.Voice.OwnerAuth;
using GeekAPI.Services.Voice.Telephony;
using GeekApplication.Interfaces.Voice;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        services.AddScoped<IVoiceBillingExemption>(sp =>
            new HttpVoiceRepository(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("GeekRepositoryInternal"),
                sp.GetRequiredService<ILogger<HttpVoiceRepository>>()));
        services.AddSingleton(BillingExemptionOptions.FromEnvironment());
        services.AddScoped<BillingExemption>();
        services.AddScoped<OnboardingEligibility>();
        services.AddScoped<IndustryRefusals>();
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
        services.AddScoped<TwilioCallStatusRecorder>();
        services.AddScoped<TenantTwilioWebhook>();
        services.AddSingleton(TwilioOptions.FromEnvironment());
        services.AddHttpClient<TwilioProvisioner>(client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddSingleton(EmailOptions.FromEnvironment());
        services.AddHttpClient<IEmailSender, ResendEmailSender>(client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddScoped<SubscriptionActivation>();
        services.AddSingleton(GoogleCalendarOptions.FromEnvironment());
        services.AddSingleton<McpOAuthState>();
        services.AddHttpClient<GoogleCalendarOAuth>(client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddScoped<McpConnectionService>();
        services.AddSingleton(McpGatewayOptions.FromEnvironment());
        services.AddHttpClient<McpGatewayClient>(client => client.Timeout = TimeSpan.FromSeconds(45));
        services.AddScoped<CalendarTools>();
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
