using GeekAPI.Controllers.Workflow.Contracts;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Infrastructure.InMemory;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.Workflow;

[ApiController]
[Route("api/clients")]
public class ClientsController : ControllerBase
{
    private readonly IClientStore _clientStore;
    private readonly IProjectStore _projectStore;

    public ClientsController(IClientStore clientStore, IProjectStore projectStore)
    {
        _clientStore = clientStore;
        _projectStore = projectStore;
    }

    [HttpGet]
    public async Task<ActionResult<List<ClientResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var clients = await _clientStore.ListAsync(cancellationToken);
        return Ok(clients.Select(ToResponse).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<ClientResponse>> Create([FromBody] CreateClientRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Name is required.");
        }

        var client = new Client { Name = request.Name, Notes = request.Notes };
        await _clientStore.AddAsync(client, cancellationToken);

        return CreatedAtAction(nameof(GetAll), new { }, ToResponse(client));
    }

    /// <summary>
    /// Remove a client that has no projects.
    ///
    /// Refused while projects exist, rather than cascading or orphaning them. A cascade would take
    /// real work with it on the strength of one click, and orphaning leaves projects pointing at a
    /// client that is gone — neither is a trade worth making to tidy up a name. The count is in the
    /// message so the caller knows what is in the way.
    /// </summary>
    [HttpDelete("{clientId:guid}")]
    public async Task<IActionResult> Delete(Guid clientId, CancellationToken cancellationToken)
    {
        var client = await _clientStore.GetAsync(clientId, cancellationToken);
        if (client is null) return NotFound();

        var projects = await _projectStore.ListAsync(p => p.ClientId == clientId, cancellationToken);
        if (projects.Count > 0)
        {
            return Conflict(new
            {
                error = $"“{client.Name}” has {projects.Count} project(s). Delete or move them first.",
                projectCount = projects.Count,
            });
        }

        var deleted = await _clientStore.DeleteAsync(clientId, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }

    [HttpPut("{clientId:guid}/publish-target")]
    public async Task<ActionResult<ClientResponse>> UpsertPublishTarget(
        Guid clientId, [FromBody] CreatePublishTargetRequest request, CancellationToken cancellationToken)
    {
        var client = await _clientStore.GetAsync(clientId, cancellationToken);
        if (client is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.GeekBackendApiBaseUrl)
            || string.IsNullOrWhiteSpace(request.OAuthTokenEndpoint)
            || string.IsNullOrWhiteSpace(request.ClientIdEnvVar)
            || string.IsNullOrWhiteSpace(request.ClientSecretEnvVar))
        {
            return BadRequest("GeekBackendApiBaseUrl, OAuthTokenEndpoint, ClientIdEnvVar, and ClientSecretEnvVar are required.");
        }

        client.PublishTarget ??= new PublishTarget { ClientId = client.Id };

        client.PublishTarget.GeekBackendApiBaseUrl = request.GeekBackendApiBaseUrl;
        client.PublishTarget.OAuthTokenEndpoint = request.OAuthTokenEndpoint;
        client.PublishTarget.ClientIdEnvVar = request.ClientIdEnvVar;
        client.PublishTarget.ClientSecretEnvVar = request.ClientSecretEnvVar;
        client.PublishTarget.DefaultAuthorId = request.DefaultAuthorId;
        client.PublishTarget.CategoryStrategy = request.CategoryStrategy;
        await _clientStore.SaveAsync(client, cancellationToken);

        return Ok(ToResponse(client));
    }

    private static ClientResponse ToResponse(Client client) => new(
        client.Id, client.Name, client.Notes, client.CreatedAtUtc,
        client.PublishTarget is null
            ? null
            : new PublishTargetResponse(
                client.PublishTarget.Id, client.PublishTarget.GeekBackendApiBaseUrl,
                client.PublishTarget.OAuthTokenEndpoint, client.PublishTarget.ClientIdEnvVar,
                client.PublishTarget.ClientSecretEnvVar, client.PublishTarget.DefaultAuthorId,
                client.PublishTarget.CategoryStrategy));
}
