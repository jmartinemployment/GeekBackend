using GeekRepository.Auth;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreatorV2;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace GeekRepository.Controllers.ContentCreatorV2;

/// <summary>
/// Derived brand/voice kits (see <see cref="GccV2BrandKit"/>). Built by GeekAPI's
/// <c>GccV2BrandKitBuilder</c> from a read-only site analysis profile — this controller only
/// persists/serves the resulting JSON, it never talks to Geek-SEO itself.
/// </summary>
[ApiController]
[Route("repo/content-creator-v2/brand-kits")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class GccV2BrandKitsController : ControllerBase
{
    private readonly ContentCreatorV2DbContext _db;

    public GccV2BrandKitsController(ContentCreatorV2DbContext db) => _db = db;

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GccV2BrandKit>> GetById(Guid id, CancellationToken ct)
    {
        var kit = await _db.GccV2BrandKits.AsNoTracking().FirstOrDefaultAsync(k => k.Id == id, ct);
        return kit is null ? NotFound() : Ok(kit);
    }

    /// <summary>Latest-first list, optionally scoped to the profile it was derived from.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GccV2BrandKit>>> List(
        [FromQuery] Guid? derivedFromProfileId,
        [FromQuery] Guid? clientId,
        [FromQuery] string? ownerUserId,
        CancellationToken ct)
    {
        var query = _db.GccV2BrandKits.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(ownerUserId))
            query = query.Where(k => k.OwnerUserId == ownerUserId);
        if (derivedFromProfileId is not null)
            query = query.Where(k => k.DerivedFromProfileId == derivedFromProfileId);
        if (clientId is not null)
            query = query.Where(k => k.ClientId == clientId);

        var results = await query.OrderByDescending(k => k.DerivedAtUtc).ToListAsync(ct);
        return Ok(results);
    }

    [HttpPost]
    public async Task<ActionResult<GccV2BrandKit>> Create([FromBody] CreateGccV2BrandKitCommand command, CancellationToken ct)
    {
        if (command is null || command.DerivedFromProfileId == Guid.Empty)
            return BadRequest("derivedFromProfileId is required");

        var maxVersion = await _db.GccV2BrandKits
            .Where(k => k.DerivedFromProfileId == command.DerivedFromProfileId)
            .Select(k => (int?)k.Version)
            .MaxAsync(ct);

        var kit = new GccV2BrandKit
        {
            ClientId = command.ClientId,
            OwnerUserId = command.OwnerUserId,
            DerivedFromProfileId = command.DerivedFromProfileId,
            Version = (maxVersion ?? 0) + 1,
            KitJson = string.IsNullOrWhiteSpace(command.KitJson) ? "{}" : command.KitJson,
            CanonicalSha256 = Sha256(string.IsNullOrWhiteSpace(command.KitJson) ? "{}" : command.KitJson),
            VoiceStatus = string.IsNullOrWhiteSpace(command.VoiceStatus) ? "provisional" : command.VoiceStatus,
        };

        _db.GccV2BrandKits.Add(kit);
        await _db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(GetById), new { id = kit.Id }, kit);
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<GccV2BrandKit>> Patch(Guid id, [FromBody] PatchGccV2BrandKitCommand command, CancellationToken ct)
    {
        var kit = await _db.GccV2BrandKits.FirstOrDefaultAsync(k => k.Id == id, ct);
        if (kit is null) return NotFound();
        if (kit.AcceptedAtUtc is not null && command.KitJson is not null)
            return Conflict("Accepted brand kit versions are immutable.");

        if (command.KitJson is not null)
        {
            kit.KitJson = command.KitJson;
            kit.CanonicalSha256 = Sha256(command.KitJson);
        }
        if (command.VoiceStatus is not null) kit.VoiceStatus = command.VoiceStatus;
        if (command.AcceptedAtUtc is not null)
        {
            if (string.IsNullOrWhiteSpace(command.ActorUserId))
                return BadRequest("actorUserId is required when accepting a brand kit.");
            kit.AcceptedAtUtc = command.AcceptedAtUtc;
            kit.AcceptedByUserId = command.ActorUserId;
        }
        else if (string.Equals(command.VoiceStatus, "provisional", StringComparison.OrdinalIgnoreCase))
            kit.AcceptedAtUtc = null;

        await _db.SaveChangesAsync(ct);
        return Ok(kit);
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    public record CreateGccV2BrandKitCommand(
        Guid DerivedFromProfileId, Guid? ClientId, string? KitJson, string? VoiceStatus, string? OwnerUserId = null);

    public record PatchGccV2BrandKitCommand(
        string? KitJson, string? VoiceStatus, DateTimeOffset? AcceptedAtUtc, string? ActorUserId = null);
}
