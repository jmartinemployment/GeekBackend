using GeekRepository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// The model snapshot must describe the model the code builds.
/// </summary>
/// <remarks>
/// This is not a style check. <c>ContentCreatorDbContextModelSnapshot</c> silently stopped
/// recording <c>GccClient</c> when 20260808_AddGccClient was written by hand, and nothing noticed
/// for six weeks. The cost lands later and on someone else: the next scaffolded migration diffs
/// against a snapshot missing a table, decides that table needs creating, and emits a CreateTable
/// for something that already exists — a migration that fails on deploy for reasons that have
/// nothing to do with the change being made.
///
/// The comparison is offline. Building a model and diffing it touches no database: Npgsql is
/// configured for its type mappings, and no connection is ever opened.
/// </remarks>
public class ContentCreatorModelSnapshotTests
{
    [Fact]
    public void Snapshot_matches_the_model()
    {
        using var context = BuildContext();

        var differences = Differences(context);

        Assert.True(
            differences.Count == 0,
            "ContentCreatorDbContextModelSnapshot is out of date with ContentCreatorDbContext. "
            + "Operations needed to reconcile it: "
            + string.Join(", ", differences.Select(o => o.GetType().Name))
            + ". Update the snapshot to match the model — a scaffolded migration would otherwise "
            + "re-create objects that already exist.");
    }

    /// <summary>
    /// What a new migration would contain right now. Empty means snapshot and model agree.
    /// </summary>
    private static IReadOnlyList<MigrationOperation> Differences(ContentCreatorDbContext context)
    {
        var services = context.GetInfrastructure();
        var differ = services.GetRequiredService<IMigrationsModelDiffer>();
        var snapshot = services.GetRequiredService<IMigrationsAssembly>().ModelSnapshot;

        Assert.NotNull(snapshot);

        // The snapshot's model is authored, not finalized — it has to be run through the same
        // initializer EF uses before it can be compared with a built one.
        IModel snapshotModel = services.GetRequiredService<IModelRuntimeInitializer>()
            .Initialize(snapshot!.Model, designTime: true, validationLogger: null);

        return differ.GetDifferences(
            snapshotModel.GetRelationalModel(),
            DesignTimeModelOf(context).GetRelationalModel());
    }

    /// <summary>
    /// The design-time model, which is the one the differ can read.
    /// </summary>
    /// <remarks>
    /// The read-optimized model EF exposes as <c>context.Model</c> does not carry everything the
    /// differ needs and throws saying so. The type that does — <c>IDesignTimeModel</c> — is not
    /// visible to this project at compile time, so it is resolved by name at runtime rather than
    /// pulling in an assembly reference for one interface.
    /// </remarks>
    private static IModel DesignTimeModelOf(ContentCreatorDbContext context)
    {
        // Searched across both EF assemblies rather than named against one: which of them declares
        // it has moved between versions, and this test should not break on that.
        var designTimeModelType = new[]
            {
                typeof(DbContext).Assembly,
                typeof(Microsoft.EntityFrameworkCore.RelationalModelExtensions).Assembly,
            }
            .SelectMany(a => a.GetTypes())
            .First(t => t.Name == "IDesignTimeModel" && t.IsInterface);

        var service = context.GetInfrastructure().GetRequiredService(designTimeModelType);
        return (IModel)designTimeModelType.GetProperty("Model")!.GetValue(service)!;
    }

    /// <summary>
    /// A context configured for Npgsql but never connected. The connection string names no
    /// reachable host on purpose: this test must fail on a real difference, never on whether a
    /// database happens to be up.
    /// </summary>
    private static ContentCreatorDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<ContentCreatorDbContext>()
            .UseNpgsql("Host=snapshot.invalid;Database=model_only;Username=none;Password=none")
            .Options;

        return new ContentCreatorDbContext(options);
    }
}
