using GeekAPI.HttpClients;
using GeekAPI.Services.GeekCrawler;
using GeekAPI.Services.GeekCrawler.Polite;

namespace GeekAPI.Services.GeekCrawler;

public static class GeekCrawlerServiceRegistration
{
    public static IServiceCollection AddGeekCrawler(this IServiceCollection services)
    {
        services.AddScoped(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient("GeekRepository");
            var logger = sp.GetRequiredService<ILogger<HttpGeekCrawlerRepository>>();
            return new HttpGeekCrawlerRepository(httpClient, logger);
        });

        services.AddHttpClient<GeekCrawlerPoliteGate>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(GeekApplication.Models.GeekCrawler.GeekCrawlerCaps.DefaultHostDelaySeconds + 5);
        });

        services.AddSingleton<GeekCrawlerWake>();
        services.AddSingleton<GeekCrawlerProgressNotifier>();
        services.AddScoped<MobilePageFetcher>();
        services.AddScoped<SameOriginBfsCrawler>();
        services.AddScoped<GeekCrawlerService>();
        services.AddHostedService<GeekCrawlerWorker>();

        return services;
    }
}
