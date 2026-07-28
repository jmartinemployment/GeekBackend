using GeekApplication.Interfaces.ContentWriterV3;
using GeekApplication.Models.ContentWriterV3;
using GeekRepository.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.ContentWriterV3;

[ApiController]
[Route("repo/content-writer-v3/workspaces")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class WorkspacesController : ControllerBase
{
    private readonly IWorkspaceRepository _repository;

    public WorkspacesController(IWorkspaceRepository repository) => _repository = repository;

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<WorkspaceDto>> GetById(Guid id, CancellationToken ct)
    {
        var workspace = await _repository.GetByIdAsync(id, ct);
        if (workspace is null)
            return NotFound();

        return Ok(workspace);
    }

    [HttpPost]
    public async Task<ActionResult<WorkspaceDto>> Create([FromBody] CreateWorkspaceCommand command, CancellationToken ct)
    {
        if (command is null)
            return BadRequest("Command is required");

        var workspace = await _repository.CreateAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = workspace.Id }, workspace);
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<WorkspaceDto>> Update(Guid id, [FromBody] UpdateWorkspaceCommand command, CancellationToken ct)
    {
        var workspace = await _repository.UpdateAsync(command, ct);
        return Ok(workspace);
    }
}

[ApiController]
[Route("repo/content-writer-v3/clients")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class ClientsController : ControllerBase
{
    private readonly IClientRepository _repository;

    public ClientsController(IClientRepository repository) => _repository = repository;

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ClientDto>> GetById(Guid id, CancellationToken ct)
    {
        var client = await _repository.GetByIdAsync(id, ct);
        if (client is null)
            return NotFound();

        return Ok(client);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ClientDto>>> GetByWorkspaceId([FromQuery] Guid workspaceId, CancellationToken ct)
    {
        if (workspaceId == Guid.Empty)
            return BadRequest("workspaceId is required");

        var clients = await _repository.GetByWorkspaceIdAsync(workspaceId, ct);
        return Ok(clients);
    }

    [HttpPost]
    public async Task<ActionResult<ClientDto>> Create([FromBody] CreateClientCommand command, CancellationToken ct)
    {
        if (command is null)
            return BadRequest("Command is required");

        var client = await _repository.CreateAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = client.Id }, client);
    }
}
