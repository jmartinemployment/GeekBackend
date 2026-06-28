using GeekSa2Read;
using Microsoft.Extensions.DependencyInjection;

namespace GeekSa2Read.DependencyInjection;

public static class GeekSa2ReadServiceCollectionExtensions
{
    public static IServiceCollection AddGeekSa2Read(this IServiceCollection services)
    {
        services.AddScoped<Sa2ContentWriterBundleReader>();
        services.AddScoped<Sa2ContentWriterExportReader>();
        return services;
    }
}
