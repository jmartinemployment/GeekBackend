using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekApplication.Models.ContentWriterV3;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.Gcw;

/// <summary>
/// GCW-facing workspaces (tenant above clients). Not exposed under /api/content-writer/v3/*.
/// </summary>
[ApiController]
[Route("api/gcw/workspaces")]
public class GcwWorkspacesController : ControllerBase
{
    private readonly HttpContentWriterV3Repository _repo;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<GcwWorkspacesController> _logger;

    public GcwWorkspacesController(
        HttpContentWriterV3Repository repo,
        ICurrentUserContext currentUser,
        ILogger<GcwWorkspacesController> logger)
    {
        _repo = repo;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<WorkspaceDto>> GetById(Guid id, CancellationToken ct)
    {
        var workspace = await _repo.GetWorkspaceByIdAsync(id, ct);
        if (workspace is null)
            return NotFound();
        if (workspace.OwnerId != _currentUser.UserId)
            return Forbid();
        return Ok(workspace);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<WorkspaceDto>>> List(CancellationToken ct)
    {
        var workspaces = await _repo.GetWorkspacesByOwnerIdAsync(_currentUser.UserId, ct);
        return Ok(workspaces);
    }

    [HttpPost]
    public async Task<ActionResult<WorkspaceDto>> Create(
        [FromBody] CreateGcwWorkspaceRequest request,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest("Body is required");
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("name is required");

        _logger.LogInformation(
            "GCW user {UserId} creating workspace {Name}",
            _currentUser.UserId,
            request.Name);

        var workspace = await _repo.CreateWorkspaceAsync(
            new CreateWorkspaceCommand(request.Name.Trim(), _currentUser.UserId),
            ct);
        return CreatedAtAction(nameof(GetById), new { id = workspace.Id }, workspace);
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<WorkspaceDto>> Update(
        Guid id,
        [FromBody] UpdateGcwWorkspaceRequest request,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("name is required");

        var existing = await _repo.GetWorkspaceByIdAsync(id, ct);
        if (existing is null)
            return NotFound();
        if (existing.OwnerId != _currentUser.UserId)
            return Forbid();

        _logger.LogInformation(
            "GCW user {UserId} renaming workspace {WorkspaceId}",
            _currentUser.UserId,
            id);

        try
        {
            var workspace = await _repo.UpdateWorkspaceAsync(
                new UpdateWorkspaceCommand(id, request.Name.Trim()),
                ct);
            return Ok(workspace);
        }
        catch (HttpRequestException)
        {
            return NotFound();
        }
    }

    public sealed record CreateGcwWorkspaceRequest(string Name);
    public sealed record UpdateGcwWorkspaceRequest(string Name);
}

/// <summary>
/// GCW-facing CWV3 clients scoped to a workspace.
/// Separate from CWV2 /api/clients used by drafting.
/// </summary>
[ApiController]
[Route("api/gcw/clients")]
public class GcwClientsController : ControllerBase
{
    private readonly HttpContentWriterV3Repository _repo;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<GcwClientsController> _logger;

    public GcwClientsController(
        HttpContentWriterV3Repository repo,
        ICurrentUserContext currentUser,
        ILogger<GcwClientsController> logger)
    {
        _repo = repo;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ClientDto>> GetById(Guid id, CancellationToken ct)
    {
        var client = await _repo.GetClientByIdAsync(id, ct);
        if (client is null)
            return NotFound();

        var workspace = await _repo.GetWorkspaceByIdAsync(client.WorkspaceId, ct);
        if (workspace is null || workspace.OwnerId != _currentUser.UserId)
            return Forbid();

        return Ok(client);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ClientDto>>> List(
        [FromQuery] Guid workspaceId,
        CancellationToken ct)
    {
        if (workspaceId == Guid.Empty)
            return BadRequest("workspaceId is required");

        var workspace = await _repo.GetWorkspaceByIdAsync(workspaceId, ct);
        if (workspace is null)
            return NotFound("workspace not found");
        if (workspace.OwnerId != _currentUser.UserId)
            return Forbid();

        var clients = await _repo.GetClientsByWorkspaceIdAsync(workspaceId, ct);
        return Ok(clients);
    }

    [HttpPost]
    public async Task<ActionResult<ClientDto>> Create(
        [FromBody] CreateGcwClientRequest request,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest("Body is required");
        if (request.WorkspaceId == Guid.Empty)
            return BadRequest("workspaceId is required");
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("name is required");

        var workspace = await _repo.GetWorkspaceByIdAsync(request.WorkspaceId, ct);
        if (workspace is null)
            return BadRequest("workspace not found");
        if (workspace.OwnerId != _currentUser.UserId)
            return Forbid();

        _logger.LogInformation(
            "GCW user {UserId} creating client in workspace {WorkspaceId}",
            _currentUser.UserId,
            request.WorkspaceId);

        var client = await _repo.CreateClientAsync(
            new CreateClientCommand(request.WorkspaceId, request.Name.Trim()),
            ct);
        return CreatedAtAction(nameof(GetById), new { id = client.Id }, client);
    }

    public sealed record CreateGcwClientRequest(Guid WorkspaceId, string Name);
}
