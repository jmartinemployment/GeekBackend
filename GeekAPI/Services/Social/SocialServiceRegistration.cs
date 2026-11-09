using GeekAPI.HttpClients;
using GeekApplication.Interfaces.Social;

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
        return services;
    }
}
