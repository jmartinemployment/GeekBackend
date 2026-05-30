using Microsoft.Extensions.DependencyInjection;
using GeekApplication.Interfaces;
using GeekRepository.Infrastructure;
using GeekRepository.Repositories;

namespace GeekRepository;

public static class ServiceRegistration
{
    public static IServiceCollection AddGeekRepository(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddSingleton<IDbConnectionFactory>(
            _ => new NpgsqlConnectionFactory(connectionString));

        services.AddScoped<ICaseStudyRepository, CaseStudyRepository>();
        services.AddScoped<IDepartmentRepository, DepartmentRepository>();
        services.AddScoped<IUseCaseRepository, UseCaseRepository>();

        services.AddGeekSeoData();

        return services;
    }
}
