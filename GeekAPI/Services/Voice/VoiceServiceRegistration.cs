using GeekAPI.HttpClients;
using GeekAPI.Services.Voice.Billing;
using GeekAPI.Services.Voice.Onboarding;
using GeekAPI.Services.Voice.Telephony;
using GeekApplication.Interfaces.Voice;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GeekAPI.Services.Voice;

public static class VoiceServiceRegistration
{
    /// <summary>
    /// Geek-Receptionist's persistence and its onboarding gate: the store over /internal/v1/*, which uses the
    /// "GeekRepositoryInternal" client Program.cs builds with X-Internal-Api-Key, and the eligibility gate. The addendum flag is read
    /// here, once: a value that is not true or false stops the service from starting.
    /// </summary>
    public static IServiceCollection AddVoiceOnboarding(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(OnboardingOptions.From(configuration));
        services.AddScoped<IVoiceStore>(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("GeekRepositoryInternal");
            var logger = sp.GetRequiredService<ILogger<HttpVoiceRepository>>();
            return new HttpVoiceRepository(httpClient, logger);
        });
        services.AddScoped<OnboardingEligibility>();
        services.AddSingleton<VoiceCredentialProtector>();
        services.AddSingleton(PayPalOptions.FromEnvironment());
        services.AddHttpClient<PayPalBillingVerifier>(client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddSingleton(TwilioOptions.FromEnvironment());
        services.AddHttpClient<TwilioProvisioner>(client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddSingleton(RetellOptions.FromEnvironment());
        services.AddHttpClient<RetellProvisioner>(client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddScoped<SubscriptionActivation>();
        services.AddRateLimiter(options =>
        {
            options.AddPolicy(OnboardingRateLimit.PolicyName, OnboardingRateLimit.Partition);
            options.OnRejected = (rejected, ct) => OnboardingRateLimit.WriteRejection(rejected.HttpContext, ct);
        });
        return services;
    }
}
