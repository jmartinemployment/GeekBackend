using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekApplication.Models.ContentCreator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.ContentCreator;

/// <summary>
/// Clients, on the v1 surface. The only HTTP surface for them.
/// </summary>
/// <remarks>
/// There were two client stores: this one, and a blob store behind api/clients. They assigned
/// unrelated ids to the same client, which is how gcc_projects.client_id — the first foreign key
/// to actually enforce the relationship — became unsatisfiable by anything the UI had in hand.
/// gcc_clients is the one client table now.
///
/// A client holds contact and billing details, so every route requires the
/// content-creator.manage scope. The authority is shared across Geek apps; a valid token alone
/// says nothing about being granted this.
/// </remarks>
[ApiController]
[Route("api/geek-content-creator/clients")]
[Authorize(Policy = ContentCreatorAuthConstants.ManagePolicy)]
public class GccClientsController : ControllerBase
{
    private readonly HttpGccRepository _repo;
    private readonly ILogger<GccClientsController> _logger;

    public GccClientsController(HttpGccRepository repo, ILogger<GccClientsController> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GccClientDto>>> List(CancellationToken ct) =>
        Ok(await _repo.ListClientsAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GccClientDto>> GetById(Guid id, CancellationToken ct)
    {
        var client = await _repo.GetClientByIdAsync(id, ct);
        if (client is null) return NotFound();
        return Ok(client);
    }

    [HttpPost]
    public async Task<ActionResult<GccClientDto>> Create(
        [FromBody] CreateGccClientCommand command,
        CancellationToken ct)
    {
        if (command is null) return BadRequest("A body is required.");

        // GeekRepository validates the same rules, and the database enforces them underneath both.
        // This is here so the operator gets a sentence rather than a round trip.
        var invalid = Validate(
            command.Name,
            command.ContactName,
            command.ContactEmail,
            command.BillingEmail,
            command.PaymentTermsDays,
            command.Currency,
            command.Rate);
        if (invalid is not null) return BadRequest(invalid);

        return Ok(await _repo.CreateClientAsync(command, ct));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<GccClientDto>> Update(
        Guid id,
        [FromBody] UpdateGccClientCommand command,
        CancellationToken ct)
    {
        if (command is null) return BadRequest("A body is required.");
        if (id != command.Id) return BadRequest("The id in the route and the body must match.");

        var invalid = Validate(
            command.Name,
            command.ContactName,
            command.ContactEmail,
            command.BillingEmail,
            command.PaymentTermsDays,
            command.Currency,
            command.Rate);
        if (invalid is not null) return BadRequest(invalid);

        return Ok(await _repo.UpdateClientAsync(command, ct));
    }

    /// <summary>
    /// Delete a client.
    /// </summary>
    /// <remarks>
    /// A client with projects is refused by the database, and nothing here tries to talk it round.
    /// The only way to make the delete succeed would be to delete the client's projects too —
    /// their schedule, their log, and in time their tracked hours — which is a worse outcome than
    /// a refused button.
    /// </remarks>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var deleted = await _repo.DeleteClientAsync(id, ct);
        return deleted ? NoContent() : NotFound();
    }

    private static string? Validate(
        string? name,
        string? contactName,
        string? contactEmail,
        string? billingEmail,
        int paymentTermsDays,
        string? currency,
        decimal? rate)
    {
        if (string.IsNullOrWhiteSpace(name)) return "name is required.";
        if (string.IsNullOrWhiteSpace(contactName)) return "contactName is required.";
        if (string.IsNullOrWhiteSpace(contactEmail)) return "contactEmail is required.";
        if (string.IsNullOrWhiteSpace(billingEmail))
            return "billingEmail is required — it is stored, never derived from the contact email.";
        if (paymentTermsDays < 0) return "paymentTermsDays cannot be negative.";
        if (string.IsNullOrWhiteSpace(currency)) return "currency is required.";

        var trimmed = currency.Trim();
        if (trimmed.Length != 3 || !trimmed.All(char.IsAsciiLetter))
            return "currency must be a three-letter ISO-4217 code.";

        if (rate is <= 0)
            return "rate must be greater than zero, or omitted — a rate of zero is an unfilled field.";

        return null;
    }
}
