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

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserSecretsRepository, UserSecretsRepository>();
        services.AddScoped<IUserClaimsRepository, UserClaimsRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IDeviceOauthRepository, DeviceOauthRepository>();
        services.AddScoped<ITwoFactorRepository, TwoFactorRepository>();
        services.AddScoped<IDeviceReregistrationRepository, DeviceReregistrationRepository>();
        services.AddScoped<ISyncRepository, SyncRepository>();
        services.AddScoped<IAuditRepository, AuditRepository>();
        services.AddScoped<ISecurityIncidentRepository, SecurityIncidentRepository>();
        services.AddScoped<IPendingVerificationRepository, PendingVerificationRepository>();

        services.AddScoped<ICaseStudyRepository, CaseStudyRepository>();
        services.AddScoped<IDepartmentRepository, DepartmentRepository>();
        services.AddScoped<IUseCaseRepository, UseCaseRepository>();

        return services;
    }
}
