using Microsoft.Extensions.DependencyInjection;
using GeekApplication.Interfaces;
using GeekRepository.Infrastructure;
using GeekRepository.Repositories;
using GeekRepository.Repositories.Content;

namespace GeekRepository;

public static class ServiceRegistration
{
    public static IServiceCollection AddGeekRepository(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddSingleton<IDbConnectionFactory>(
            _ => new NpgsqlConnectionFactory(connectionString));

        services.AddScoped<AmbientDbContext>();
        services.AddScoped<IAmbientDbContext>(sp => sp.GetRequiredService<AmbientDbContext>());
        services.AddScoped<IUnitOfWork, SqlUnitOfWork>();

        services.AddScoped<IWebPostRepository, WebPostRepository>();

        services.AddGeekSeoData();

        return services;
    }
}
