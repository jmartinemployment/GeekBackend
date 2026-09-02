using GeekAPI.HttpClients;
using GeekAPI.Services.GeekCrawler;
using GeekAPI.Services.GeekCrawler.Polite;
using Microsoft.Extensions.Hosting;

namespace GeekAPI.Services.GeekCrawler;

public static class GeekCrawlerServiceRegistration
{
    public static IServiceCollection AddGeekCrawler(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment? environment = null)
    {
        var options = GeekCrawlerOptions.FromConfiguration(configuration, environment);
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

        services.AddHttpClient<GeekCrawlerSitemapSeeder>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddSingleton<GeekCrawlerPlaywrightHolder>();
        services.AddHostedService<GeekCrawlerPlaywrightStartupHostedService>();
        services.AddSingleton<GeekCrawlerHostRegistry>();
        services.AddSingleton<GeekCrawlerRunCoordinator>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<GeekCrawlerWake>();
        services.AddSingleton<GeekCrawlerProgressNotifier>();
        services.AddScoped<GeekCrawlerPageBatchWriter>();
        services.AddScoped<GeekCrawlerLinkRebuilder>();
        services.AddScoped<MobilePageFetcher>();
        services.AddScoped<SameOriginBfsCrawler>();
        services.AddScoped<GeekCrawlerSitemapSeeder>();
        services.AddScoped<GeekCrawlerService>();

        RegisterWorkers(services, options.WorkerCount);

        services.AddHostedService<GeekCrawlerConfigLogger>();
        services.AddHostedService<GeekCrawlerStallRecoveryHostedService>();
        services.AddHostedService<GeekCrawlerScheduleHostedService>();

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
                sp.GetRequiredService<GeekCrawlerRunCoordinator>(),
                sp.GetRequiredService<ILogger<GeekCrawlerWorker>>(),
                workerIndex));
        }
    }
}
