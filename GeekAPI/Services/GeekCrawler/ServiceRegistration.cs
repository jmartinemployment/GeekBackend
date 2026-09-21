using System.Net;
using GeekAPI.HttpClients;
using GeekAPI.Services.GeekCrawler;
using GeekAPI.Services.GeekCrawler.Polite;
using Microsoft.Extensions.Hosting;
using GeekApplication.Models.GeekCrawler;

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

        // The same scoped instance, seen through the narrow interface grounding depends on.
        services.AddScoped<GeekAPI.HttpClients.IGccCrawlPageReader>(sp =>
            sp.GetRequiredService<HttpGeekCrawlerRepository>());

        // Reachability probes are HEAD requests to third-party hosts. Short timeout: this runs while
        // the operator waits, and a slow host is itself a useful answer.
        services.AddHttpClient<GeekCrawlerSeedReachability>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(8);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = GeekCrawlerCaps.MaxRedirectsPerNavigation,
        });

        services.AddHttpClient<GeekCrawlerPoliteGate>(client =>
        {
            // Host delay can be 0–1s for local proves; robots/WAF fetches still need a real timeout.
            // A too-short timeout surfaces as OperationCanceledException and looks like a cancel.
            client.Timeout = TimeSpan.FromSeconds(Math.Max(60, options.HostDelaySeconds + 5));
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
        });

        services.AddHttpClient<GeekCrawlerSitemapSeeder>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(60);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
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
                sp.GetRequiredService<GeekCrawlerOptions>(),
                sp.GetRequiredService<ILogger<GeekCrawlerWorker>>(),
                workerIndex));
        }
    }
}
