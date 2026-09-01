using GeekAPI.HttpClients;
using GeekAPI.Services.GeekCrawler;
using GeekAPI.Services.GeekCrawler.Polite;
using Microsoft.Extensions.Hosting;

namespace GeekAPI.Services.GeekCrawler;

public static class GeekCrawlerServiceRegistration
{
    public static IServiceCollection AddGeekCrawler(this IServiceCollection services, IConfiguration configuration)
    {
        var options = GeekCrawlerOptions.FromConfiguration(configuration);
        services.AddSingleton(options);

        services.AddScoped(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient("GeekRepository");
            var logger = sp.GetRequiredService<ILogger<HttpGeekCrawlerRepository>>();
            return new HttpGeekCrawlerRepository(httpClient, logger);
        });

        services.AddHttpClient<GeekCrawlerPoliteGate>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(options.HostDelaySeconds + 5);
        });

        services.AddSingleton<GeekCrawlerPlaywrightHolder>();
        services.AddHostedService<GeekCrawlerPlaywrightStartupHostedService>();
        services.AddSingleton<GeekCrawlerHostRegistry>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<GeekCrawlerWake>();
        services.AddSingleton<GeekCrawlerProgressNotifier>();
        services.AddScoped<GeekCrawlerPageBatchWriter>();
        services.AddScoped<GeekCrawlerLinkRebuilder>();
        services.AddScoped<MobilePageFetcher>();
        services.AddScoped<SameOriginBfsCrawler>();
        services.AddScoped<GeekCrawlerService>();

        RegisterWorkers(services, options.WorkerCount);

        services.AddHostedService<GeekCrawlerConfigLogger>();

        return services;
    }

    internal static void RegisterWorkers(IServiceCollection services, int workerCount)
    {
        for (var i = 0; i < workerCount; i++)
        {
            var workerIndex = i;
            services.AddSingleton<IHostedService>(sp => new GeekCrawlerWorker(
                sp.GetRequiredService<GeekCrawlerWake>(),
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<ILogger<GeekCrawlerWorker>>(),
                workerIndex));
        }
    }
}
