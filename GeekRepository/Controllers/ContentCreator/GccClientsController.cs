using GeekApplication.Interfaces.ContentCreator;
using GeekApplication.Models.ContentCreator;
using GeekRepository.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.ContentCreator;

[ApiController]
[Route("repo/content-creator/clients")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class GccClientsController : ControllerBase
{
    private readonly IGccClientRepository _repository;
    private readonly ILogger<GccClientsController> _logger;

    public GccClientsController(IGccClientRepository repository, ILogger<GccClientsController> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GccClientDto>> GetById(Guid id, CancellationToken ct)
    {
        var client = await _repository.GetByIdAsync(id, ct);
        if (client is null)
            return NotFound();

        return Ok(client);
    }

    [HttpGet]
    public async Task<ActionResult<GccClientDto>> GetByName([FromQuery] string? name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest("name query parameter is required");

        var client = await _repository.GetByNameAsync(name, ct);
        if (client is null)
            return NotFound();

        return Ok(client);
    }

    [HttpPost]
    public async Task<ActionResult<GccClientDto>> Create([FromBody] CreateGccClientCommand command, CancellationToken ct)
    {
        if (command is null)
            return BadRequest("Command is required");

        if (string.IsNullOrWhiteSpace(command.Name))
            return BadRequest("Name is required");

        var client = await _repository.CreateAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = client.Id }, client);
    }
}
