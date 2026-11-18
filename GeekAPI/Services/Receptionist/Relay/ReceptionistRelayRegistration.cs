using GeekApplication.Interfaces.Receptionist;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GeekAPI.Services.Receptionist.Relay;

public static class ReceptionistRelayRegistration
{
    /// <summary>
    /// The receptionist's model and the relay's tickets. <see cref="IReceptionistModel"/> is <see cref="LlmReceptionistModel"/> over
    /// GeekAPI's configured default LLM provider; another implementation replaces it by registration. Unconfigured, it is still
    /// registered and reports <see cref="IReceptionistModel.IsConfigured"/> false, so a caller who presses 1 hears the closing message.
    /// </summary>
    public static IServiceCollection AddReceptionistRelay(this IServiceCollection services) =>
        services.AddReceptionistRelay(Environment.GetEnvironmentVariable);

    internal static IServiceCollection AddReceptionistRelay(this IServiceCollection services, Func<string, string?> readEnvironment)
    {
        services.AddSingleton(ReceptionistModelSettings.From(readEnvironment));
        services.AddHttpClient<IReceptionistModel, LlmReceptionistModel>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<RelayTickets>();
        return services;
    }

    /// <summary>Says once, at startup, whether calls can be handed to the AI, and if not, which setting is missing.</summary>
    public static void LogReceptionistModel(this WebApplication app)
    {
        var settings = app.Services.GetRequiredService<ReceptionistModelSettings>();
        var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("GeekAPI.Receptionist");
        if (settings.IsConfigured)
            log.LogInformation("Geek-Receptionist's AI is configured: provider {Provider}, model {Model}.", settings.Model!.Provider, settings.Model.Model);
        else
            log.LogError("Geek-Receptionist's AI is not configured, so a caller who consents hears the closing message. {Reason}", settings.Reason);
    }
}
