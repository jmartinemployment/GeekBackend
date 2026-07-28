using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekApplication.Models.ContentWriterV3;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.ContentWriterV3;

[ApiController]
[Route("api/content-writer/v3/workspaces")]
public class WorkspacesApiController : ControllerBase
{
    private readonly HttpContentWriterV3Repository _repo;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<WorkspacesApiController> _logger;

    public WorkspacesApiController(
        HttpContentWriterV3Repository repo,
        ICurrentUserContext currentUser,
        ILogger<WorkspacesApiController> logger)
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

        return Ok(workspace);
    }

    [HttpPost]
    public async Task<ActionResult<WorkspaceDto>> Create([FromBody] CreateWorkspaceCommand command, CancellationToken ct)
    {
        _logger.LogInformation("User {UserId} creating workspace", _currentUser.UserId);

        var workspace = await _repo.CreateWorkspaceAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = workspace.Id }, workspace);
    }
}

[ApiController]
[Route("api/content-writer/v3/clients")]
public class ClientsApiController : ControllerBase
{
    private readonly HttpContentWriterV3Repository _repo;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<ClientsApiController> _logger;

    public ClientsApiController(
        HttpContentWriterV3Repository repo,
        ICurrentUserContext currentUser,
        ILogger<ClientsApiController> logger)
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

        return Ok(client);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ClientDto>>> GetByWorkspaceId([FromQuery] Guid workspaceId, CancellationToken ct)
    {
        if (workspaceId == Guid.Empty)
            return BadRequest("workspaceId is required");

        _logger.LogInformation("User {UserId} fetching clients for workspace {WorkspaceId}",
            _currentUser.UserId, workspaceId);

        var clients = await _repo.GetClientsByWorkspaceIdAsync(workspaceId, ct);
        return Ok(clients);
    }

    [HttpPost]
    public async Task<ActionResult<ClientDto>> Create([FromBody] CreateClientCommand command, CancellationToken ct)
    {
        _logger.LogInformation("User {UserId} creating client for workspace {WorkspaceId}",
            _currentUser.UserId, command.WorkspaceId);

        var client = await _repo.CreateClientAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = client.Id }, client);
    }
}
