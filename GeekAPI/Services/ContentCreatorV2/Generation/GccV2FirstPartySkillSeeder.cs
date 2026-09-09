using System.Security.Cryptography;
using System.Text;
using GeekAPI.HttpClients;

namespace GeekAPI.Services.ContentCreatorV2.Generation;

public sealed class GccV2FirstPartySkillSeeder(
    IServiceScopeFactory scopes,
    IHostEnvironment environment,
    ILogger<GccV2FirstPartySkillSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (environment.IsEnvironment("Testing"))
            return;
        using var scope = scopes.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<HttpGccV2Repository>();
        try
        {
            var existing = await repo.ListSkillsAsync(ct: cancellationToken);
            foreach (var skill in GccV2SkillCatalog.Definitions.OrderBy(x => x.Order))
            {
                if (existing.Any(x => x.Slug == skill.Id
                    && x.Versions.Any(v => v.SemanticVersion == skill.Version)))
                    continue;
                var markdown = Markdown(skill);
                var fileDigest = Hash(markdown);
                var command = new ImportGccV2SkillCommand(
                    skill.Id, DisplayName(skill.Id), skill.OutputRequirements,
                    "GeekBackend", $"first-party-skills/{skill.Id}", "Content Platform",
                    skill.Version, $"first-party:{GccV2SkillCatalog.CurrentVersion}",
                    skill.Sha256, fileDigest, skill.License, "gcc-skill-envelope.v1+v2",
                    true, false,
                    [new("SKILL.md", "text/markdown", Encoding.UTF8.GetByteCount(markdown), fileDigest, markdown)],
                    (from stage in skill.SupportedStages from contentType in skill.SupportedContentTypes
                        select new ImportGccV2SkillApplicability(
                            stage, contentType, skill.Order, "[]", "[]", "automatic")).ToList(),
                    [], "system:first-party-seed", null, "startup-seed");
                var package = await repo.ImportSkillAsync(command, cancellationToken);
                var version = (await repo.GetSkillAsync(package.Id, cancellationToken))!.Versions
                    .Single(x => x.SemanticVersion == skill.Version);
                await repo.ReviewSkillAsync(version.Id,
                    new ReviewGccV2SkillCommand(true, "Migrated from the reviewed v1 catalog.", [],
                        "system:first-party-seed", null, "startup-seed"), cancellationToken);
                await repo.PublishSkillAsync(version.Id,
                    new TransitionGccV2SkillCommand("system:first-party-seed", null, "startup-seed"),
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "First-party governed skill seed failed.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static string Markdown(GccV2SkillDefinition skill) => $"""
        ---
        name: {skill.Id}
        description: {skill.OutputRequirements}
        license: {skill.License}
        compatibility: gcc-skill-envelope.v1+v2
        ---
        # {DisplayName(skill.Id)}

        ## Instructions
        {skill.PromptInstructions}

        ## Retrieval
        {skill.RetrievalHints}

        ## Output requirements
        {skill.OutputRequirements}

        ## Validation
        {skill.ValidationChecks}
        """;

    private static string DisplayName(string id) => string.Join(' ', id.Split('-')
        .Select(x => char.ToUpperInvariant(x[0]) + x[1..]));
    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
