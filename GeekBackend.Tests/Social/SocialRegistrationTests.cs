using GeekAPI.HttpClients;
using GeekAPI.Services.Social;
using GeekApplication.Interfaces.Social;
using GeekRepository;
using GeekRepository.Auth;
using GeekRepository.Data;
using GeekRepository.Repositories.Social;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GeekBackend.Tests.Social;

/// <summary>
/// The wiring the two services depend on, with no database and no network: the store resolves on
/// each side, and every repo/social/* route is behind the internal service key. The credential
/// route returns token ciphertext, so a controller that lost its attribute would publish it.
/// </summary>
public class SocialRegistrationTests
{
    [Fact]
    public void GeekRepository_resolves_the_store_over_its_own_context()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<SocialDbContext>(o => o.UseNpgsql("Host=localhost;Database=never-opened"));
        services.AddGeekRepository("Host=localhost;Database=never-opened");

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        Assert.IsType<SocialRepository>(scope.ServiceProvider.GetRequiredService<ISocialStore>());
    }

    [Fact]
    public void GeekAPI_resolves_the_store_over_http_and_one_protector()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient("GeekRepository", c => c.BaseAddress = new Uri("http://geek-repository.test/"));
        services.AddSocialPersistence();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        Assert.IsType<HttpSocialRepository>(scope.ServiceProvider.GetRequiredService<ISocialStore>());
        Assert.Same(
            provider.GetRequiredService<SocialCredentialProtector>(),
            scope.ServiceProvider.GetRequiredService<SocialCredentialProtector>());
    }

    [Fact]
    public void GeekAPI_boots_without_the_encryption_key_and_fails_at_the_first_token()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient("GeekRepository", c => c.BaseAddress = new Uri("http://geek-repository.test/"));
        services.AddSocialPersistence();

        using var provider = services.BuildServiceProvider();

        // Resolving it must not throw; using it without a key answers a failed Result (see the protector tests).
        Assert.NotNull(provider.GetRequiredService<SocialCredentialProtector>());
    }

    private static IEnumerable<Type> SocialControllers() =>
        typeof(SocialRepository).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t) && t.Namespace == "GeekRepository.Controllers.Social");

    [Fact]
    public void Every_social_controller_is_behind_the_internal_service_policy_under_repo_social()
    {
        var controllers = SocialControllers().ToList();
        Assert.True(controllers.Count >= 6, "The social controllers were not found.");

        foreach (var controller in controllers)
        {
            var authorize = controller.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).Cast<AuthorizeAttribute>().ToList();
            Assert.True(
                authorize.Any(a => a.Policy == RepositoryAuthConstants.InternalServicePolicy),
                $"{controller.Name} is not behind the internal service policy.");

            var routes = controller.GetCustomAttributes(typeof(RouteAttribute), inherit: false).Cast<RouteAttribute>().ToList();
            var route = Assert.Single(routes);
            Assert.StartsWith("repo/social/", route.Template, StringComparison.Ordinal);

            foreach (var action in controller.GetMethods().Where(m => m.DeclaringType == controller))
            {
                Assert.Empty(action.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true));
            }
        }
    }
}
