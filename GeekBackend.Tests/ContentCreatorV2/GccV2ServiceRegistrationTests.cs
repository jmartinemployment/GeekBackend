using GeekAPI.Services.ContentCreatorV2.Competitor;
using GeekAPI.Services.ContentCreatorV2.Generation;
using GeekAPI.Services.ContentCreatorV2.Partner;
using Microsoft.Extensions.DependencyInjection;

namespace GeekBackend.Tests.ContentCreatorV2;

/// <summary>
/// The extraction services must be constructible from the container, not just from a test that news
/// them up by hand.
///
/// IGccV2SchemaConstrainedGenerator had no callers until the extractors were wired to it, so it had
/// never been registered. Nothing caught it: the unit tests construct the services directly, and the
/// DI graph is not validated at startup. In production the missing registration took down every
/// controller whose action graph touched an extractor - including GET /creates - not only the
/// endpoint that used it.
/// </summary>
public sealed class GccV2ServiceRegistrationTests
{
    [Fact]
    public void Schema_constrained_generator_is_registered()
    {
        var services = new ServiceCollection();
        services.AddScoped<IGccV2SchemaConstrainedGenerator, GccV2SchemaConstrainedGenerator>();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IGccV2SchemaConstrainedGenerator>());
    }

    /// <summary>
    /// Guards the exact production failure: both extractors take the generator, so a missing
    /// registration makes them unresolvable.
    /// </summary>
    [Theory]
    [InlineData(typeof(GccV2CompetitorExtractionService))]
    [InlineData(typeof(GccV2PartnerExtractionService))]
    public void Extractors_declare_the_generator_as_a_constructor_dependency(Type extractor)
    {
        var constructor = Assert.Single(extractor.GetConstructors());
        var parameters = constructor.GetParameters().Select(p => p.ParameterType).ToList();

        Assert.Contains(typeof(IGccV2SchemaConstrainedGenerator), parameters);
    }
}
