using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.Workflow.Services;
using GeekApplication.Models.ContentCreator;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreator;
using GeekRepository.Repositories.ContentCreator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using ApiProjectsController = GeekAPI.Controllers.ContentCreator.GccProjectsController;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// The project's drafts, listed and exported by project. The page asks for a project's drafts; which
/// create each is stored under is not part of the question -- a draft is the project's by its own
/// project key (GR4).
/// </summary>
public sealed class GccProjectDraftsTests
{
    private static readonly Guid Owner = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task A_projects_drafts_are_the_drafts_keyed_to_it_newest_first_and_no_one_elses()
    {
        var options = new DbContextOptionsBuilder<ContentCreatorDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        var project = Guid.NewGuid();
        var other = Guid.NewGuid();
        Guid older, newer, elsewhere, loose;
        await using (var db = new ContentCreatorDbContext(options))
        {
            var first = Create(project);
            var second = Create(project);
            var theirs = Create(other);
            var unassigned = Create(null);
            db.AddRange(first, second, theirs, unassigned);
            older = Add(db, first, "Pillar", new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
            newer = Add(db, second, "Blog", new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc));
            elsewhere = Add(db, theirs, "Theirs", new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc));
            loose = Add(db, unassigned, "Loose", new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc));
            await db.SaveChangesAsync();
        }

        await using var read = new ContentCreatorDbContext(options);
        var drafts = await new GccArtifactRepository(read).GetByProjectIdAsync(project);

        Assert.Equal([newer, older], drafts.Select(d => d.Id));
        Assert.All(drafts, d => Assert.Equal(project, d.ProjectId));
        Assert.DoesNotContain(drafts, d => d.Id == elsewhere || d.Id == loose);
        Assert.Empty(await new GccArtifactRepository(read).GetByProjectIdAsync(Guid.NewGuid()));
    }

    /// <summary>
    /// A page made one at a time -- Repurpose, a tool job -- is keyed to its create's project, read off the
    /// create and never supplied; under an unassigned create it is keyed to none.
    /// </summary>
    [Fact]
    public async Task A_page_created_on_its_own_takes_its_creates_project()
    {
        var options = new DbContextOptionsBuilder<ContentCreatorDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        var project = Guid.NewGuid();
        var onProject = Create(project);
        var unassigned = Create(null);
        await using (var seed = new ContentCreatorDbContext(options))
        {
            seed.AddRange(onProject, unassigned);
            await seed.SaveChangesAsync();
        }

        await using var db = new ContentCreatorDbContext(options);
        var repo = new GccArtifactRepository(db);
        var keyed = await repo.CreateAsync(new CreateGccArtifactCommand(onProject.Id, "social", "From the pillar"));
        var loose = await repo.CreateAsync(new CreateGccArtifactCommand(unassigned.Id, "social", "Loose"));

        Assert.Equal(project, keyed.ProjectId);
        Assert.Null(loose.ProjectId);
        Assert.Equal(project, (await db.GccArtifacts.SingleAsync(a => a.Id == keyed.Id)).ProjectId);
        Assert.Equal([keyed.Id], (await repo.GetByProjectIdAsync(project)).Select(d => d.Id));
    }

    [Fact]
    public void Both_routes_are_under_the_manage_policy()
    {
        Assert.Equal(
            ContentCreatorAuthConstants.ManagePolicy,
            typeof(ApiProjectsController).GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.Equal(
            "{id:guid}/artifacts",
            typeof(ApiProjectsController).GetMethod(nameof(ApiProjectsController.ListArtifacts))!
                .GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal(
            "{id:guid}/export/html",
            typeof(ApiProjectsController).GetMethod(nameof(ApiProjectsController.ExportHtml))!
                .GetCustomAttribute<HttpGetAttribute>()!.Template);
    }

    /// <summary>
    /// Two pages whose titles reduce to one file name are both exported. A zip cannot hold two files
    /// of one name, and keeping one would drop a page without a word. (A project no longer holds two
    /// drafts of one page -- see GccProjectPageRepositoryTests -- so this is two pages, not two runs.)
    /// </summary>
    [Fact]
    public async Task The_export_holds_every_draft_and_two_with_one_title_do_not_collide()
    {
        var projectId = Guid.NewGuid();
        var createA = Guid.NewGuid();
        var createB = Guid.NewGuid();
        var newest = Artifact(createB, "pillar", "Accounts Payable", new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc));
        var oldest = Artifact(createA, "pillar", "Accounts Payable", new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc));
        var blog = Artifact(createA, "blog", "A Blog", new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc));
        var repo = new HttpGccRepository(
            new HttpClient(new Repository(projectId, [oldest, blog, newest])) { BaseAddress = new Uri("http://repo.test/") },
            NullLogger<HttpGccRepository>.Instance);
        var export = new GccArtifactExportService(
            repo, Options.Create(new CompanyProfileOptions()), NullLogger<GccArtifactExportService>.Instance);

        var documents = await export.ExportProjectAsync(projectId, CancellationToken.None);

        // The bodies here are not documents, so each is exported as it is, under its type's folder.
        Assert.Equal(
            ["use-cases/accounts-payable.json", "blog/a-blog.json", "use-cases/accounts-payable-2.json"],
            documents.Select(d => d.FileName));
        Assert.Equal(newest.Id.ToString(), documents[0].Content);
        Assert.Equal(oldest.Id.ToString(), documents[2].Content);

        // And they go into one archive without a duplicate entry.
        var zip = await GccExportZip.WriteAsync(documents, CancellationToken.None);
        using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(zip));
        Assert.Equal(3, archive.Entries.Select(e => e.FullName).Distinct().Count());
    }

    private sealed class Repository(Guid projectId, IReadOnlyList<GccArtifactDto> artifacts) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.PathAndQuery;
            object? body =
                path == $"/repo/content-creator/projects/{projectId}/artifacts" ? artifacts
                : path.StartsWith("/repo/content-creator/creates/") ? new GccCreateDto(
                    Guid.Parse(path.Split('/')[^1]), Guid.NewGuid(), Owner, "pillar", "Accounts Payable", null, null,
                    null, null, null, "draft", DateTime.UtcNow, DateTime.UtcNow, "marketing", projectId)
                : path.StartsWith("/repo/content-creator/versions?artifactId=") ? new[]
                {
                    // The artifact's own id as its body: not a document, so it is exported verbatim.
                    new GccArtifactVersionDto(Guid.NewGuid(), Guid.Parse(path.Split('=')[^1]), 1, path.Split('=')[^1], null, 0, DateTime.UtcNow),
                }
                : null;
            return Task.FromResult(body is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(body, Web), Encoding.UTF8, "application/json"),
                });
        }
    }

    private static GccArtifactDto Artifact(Guid createId, string type, string name, DateTime createdAt) =>
        new(Guid.NewGuid(), createId, Guid.NewGuid(), null, type, name, "draft", createdAt, createdAt);

    private static GccCreate Create(Guid? projectId) => new()
    {
        ClientId = Guid.NewGuid(), ProjectId = projectId, OwnerUserId = Owner,
        StartingContentType = "pillar", Topic = "Accounts Payable",
    };

    private static Guid Add(ContentCreatorDbContext db, GccCreate create, string name, DateTime createdAt)
    {
        var artifact = new GccArtifact
        {
            ProjectId = create.ProjectId, CreateId = create.Id, Type = "pillar", Name = name, CreatedAtUtc = createdAt,
        };
        db.Add(artifact);
        return artifact.Id;
    }
}
