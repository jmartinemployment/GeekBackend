using GeekAPI.HttpClients;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GeekAPI.Services.Visitors;

public static class VisitorServiceRegistration
{
    public static IServiceCollection AddVisitors(this IServiceCollection services)
    {
        // Resolved here so a container without zone data fails at boot, not at 07:00.
        _ = VisitorClock.NewYork;

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<VisitorUserAgentParser>();

        services.AddScoped<IVisitorRepositoryClient>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient("GeekRepository");
            var logger = sp.GetRequiredService<ILogger<HttpVisitorRepository>>();
            return new HttpVisitorRepository(httpClient, logger);
        });

        services.AddScoped<VisitorEventProcessor>();
        services.AddScoped<VisitorDailyReportComposer>();
        services.AddHostedService<VisitorDailyReportHostedService>();
        return services;
    }
}
