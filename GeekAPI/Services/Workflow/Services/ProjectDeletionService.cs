using GeekAPI.HttpClients;
using GeekAPI.Services.Workflow.Infrastructure.InMemory;

namespace GeekAPI.Services.Workflow.Services;

/// <summary>
/// Deletes a project and everything downstream of it.
///
/// A project spans two stores, so the cascade cannot live in either one. The project itself —
/// its crawl, keyword sources, generated content and verdicts — is a single snapshot document in
/// GeekRepository's content-writer-v2 blobs. The Content Creator create it links to, with its
/// artifacts, versions and approval events, is relational data in content_creator.gcc_*. Deleting
/// the project row alone leaves the create unreachable: nothing but Project.LinkedCreateId ever
/// pointed at it, and the drafted content lives there, not on the project.
///
/// The create goes first. If it fails, both still exist and the operator can retry; the reverse
/// order would leave a project pointing at a create that is gone, which the workspace cannot open.
/// </summary>
public sealed class ProjectDeletionService
{
    private readonly IProjectStore _projectStore;
    private readonly HttpGccRepository _gccRepository;
    private readonly ILogger<ProjectDeletionService> _logger;

    public ProjectDeletionService(
        IProjectStore projectStore,
        HttpGccRepository gccRepository,
        ILogger<ProjectDeletionService> logger)
    {
        _projectStore = projectStore;
        _gccRepository = gccRepository;
        _logger = logger;
    }

    /// <summary>Delete one project and its linked create. False when no such project exists.</summary>
    public async Task<bool> DeleteAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var project = await _projectStore.GetAsync(projectId, cancellationToken);
        if (project is null) return false;

        if (project.LinkedCreateId is Guid createId && createId != Guid.Empty)
        {
            var createDeleted = await _gccRepository.DeleteCreateAsync(createId, cancellationToken);
            _logger.LogInformation(
                "Project {ProjectId}: linked create {CreateId} {Outcome}",
                projectId,
                createId,
                createDeleted ? "deleted with its artifacts and versions" : "was already gone");
        }

        var deleted = await _projectStore.DeleteAsync(projectId, cancellationToken);
        _logger.LogInformation("Deleted project {ProjectId}", projectId);
        return deleted;
    }

    /// <summary>Delete every project belonging to a client. Returns how many were removed.</summary>
    public async Task<int> DeleteForClientAsync(Guid clientId, CancellationToken cancellationToken = default)
    {
        var projects = await _projectStore.ListAsync(p => p.ClientId == clientId, cancellationToken);

        var deleted = 0;
        foreach (var project in projects)
        {
            if (await DeleteAsync(project.Id, cancellationToken))
            {
                deleted++;
            }
        }

        _logger.LogInformation("Deleted {Count} project(s) for client {ClientId}", deleted, clientId);
        return deleted;
    }
}
