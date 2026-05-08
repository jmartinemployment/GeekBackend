using Microsoft.Extensions.DependencyInjection;
using GeekApplication.Interfaces;
using GeekRepository.Data;
using GeekRepository.Infrastructure;
using GeekRepository.Repositories;
using GeekRepository.Repositories.JtiBlacklist;

namespace GeekRepository;

public static class ServiceRegistration
{
	public static IServiceCollection AddGeekRepository(
		this IServiceCollection services,
		string connectionString)
	{
		services.AddSingleton<IDbConnectionFactory>(
			_ => new NpgsqlConnectionFactory(connectionString));

		services.AddDbContext<AppDbContext>();

		services.AddScoped<IUserRepository, UserRepository>();
		services.AddScoped<IUserClaimsRepository, UserClaimsRepository>();
		services.AddScoped<IRoleRepository, RoleRepository>();
		services.AddScoped<IDeviceOauthRepository, DeviceOauthRepository>();
		services.AddScoped<ITwoFactorRepository, TwoFactorRepository>();
		services.AddScoped<IDeviceReregistrationRepository, DeviceReregistrationRepository>();
		services.AddScoped<ISyncRepository, SyncRepository>();
		services.AddScoped<IAuditRepository, AuditRepository>();
		services.AddScoped<ISecurityIncidentRepository, SecurityIncidentRepository>();
		services.AddScoped<IOAuthClientRepository, OAuthClientRepository>();
		services.AddScoped<IOidcStorageRepository, OidcStorageRepository>();

		services.AddSingleton<IJtiBlacklist, PostgresJtiBlacklistRepository>();

		return services;
	}
}
