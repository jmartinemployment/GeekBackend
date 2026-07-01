using GeekSeo.Application.Infrastructure;
using GeekSeo.Persistence.Data;
using GeekSeo.Persistence.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Controllers.Gtm;

[ApiController]
[Route("repo/gtm/accounts")]
public sealed class GtmAccountsController(SeoDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid userId, CancellationToken ct)
    {
        if (userId == Guid.Empty)
            return BadRequest("userId is required.");

        var rows = await db.GtmAccountConnections.AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.AccountKey)
            .Select(c => ToSummary(c))
            .ToListAsync(ct);

        return Ok(rows);
    }

    [HttpGet("{accountKey}")]
    public async Task<IActionResult> Get(string accountKey, [FromQuery] Guid userId, CancellationToken ct)
    {
        if (userId == Guid.Empty)
            return BadRequest("userId is required.");

        var row = await FindAsync(userId, accountKey, ct);
        return row is null ? NotFound() : Ok(ToDetail(row));
    }

    [HttpPut("{accountKey}")]
    public async Task<IActionResult> Upsert(
        string accountKey,
        [FromQuery] Guid userId,
        [FromBody] UpsertGtmAccountRequest body,
        CancellationToken ct)
    {
        if (userId == Guid.Empty)
            return BadRequest("userId is required.");

        if (string.IsNullOrWhiteSpace(body.RefreshToken))
            return BadRequest("RefreshToken is required.");

        var normalizedKey = NormalizeAccountKey(accountKey);
        var (cipher, iv, tag) = SeoCredentialProtector.Encrypt(body.RefreshToken.Trim());

        var existing = await db.GtmAccountConnections
            .FirstOrDefaultAsync(c => c.UserId == userId && c.AccountKey == normalizedKey, ct);

        if (existing is null)
        {
            existing = new SeoGtmAccountConnection
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                AccountKey = normalizedKey,
                GoogleEmail = string.IsNullOrWhiteSpace(body.GoogleEmail) ? null : body.GoogleEmail.Trim(),
                EncryptedRefreshToken = cipher,
                EncryptionIv = iv,
                EncryptionTag = tag,
                ConnectedAt = DateTimeOffset.UtcNow,
            };
            db.GtmAccountConnections.Add(existing);
        }
        else
        {
            existing.GoogleEmail = string.IsNullOrWhiteSpace(body.GoogleEmail) ? existing.GoogleEmail : body.GoogleEmail.Trim();
            existing.EncryptedRefreshToken = cipher;
            existing.EncryptionIv = iv;
            existing.EncryptionTag = tag;
            existing.ConnectedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return Ok(ToDetail(existing));
    }

    [HttpDelete("{accountKey}")]
    public async Task<IActionResult> Delete(string accountKey, [FromQuery] Guid userId, CancellationToken ct)
    {
        if (userId == Guid.Empty)
            return BadRequest("userId is required.");

        var normalizedKey = NormalizeAccountKey(accountKey);
        var existing = await db.GtmAccountConnections
            .FirstOrDefaultAsync(c => c.UserId == userId && c.AccountKey == normalizedKey, ct);

        if (existing is null)
            return NotFound();

        db.GtmAccountConnections.Remove(existing);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private Task<SeoGtmAccountConnection?> FindAsync(Guid userId, string accountKey, CancellationToken ct) =>
        db.GtmAccountConnections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.UserId == userId && c.AccountKey == NormalizeAccountKey(accountKey), ct);

    private static string NormalizeAccountKey(string accountKey) =>
        accountKey.Trim().ToLowerInvariant();

    private static GtmAccountSummary ToSummary(SeoGtmAccountConnection row) => new()
    {
        AccountKey = row.AccountKey,
        GoogleEmail = row.GoogleEmail,
        ConnectedAt = row.ConnectedAt,
    };

    private static GtmAccountDetail ToDetail(SeoGtmAccountConnection row) => new()
    {
        AccountKey = row.AccountKey,
        GoogleEmail = row.GoogleEmail,
        ConnectedAt = row.ConnectedAt,
        EncryptedRefreshToken = row.EncryptedRefreshToken,
        EncryptionIv = row.EncryptionIv,
        EncryptionTag = row.EncryptionTag,
    };
}

public sealed class UpsertGtmAccountRequest
{
    public string? GoogleEmail { get; set; }
    public required string RefreshToken { get; set; }
}

public class GtmAccountSummary
{
    public required string AccountKey { get; set; }
    public string? GoogleEmail { get; set; }
    public DateTimeOffset ConnectedAt { get; set; }
}

public sealed class GtmAccountDetail : GtmAccountSummary
{
    public byte[] EncryptedRefreshToken { get; set; } = [];
    public byte[] EncryptionIv { get; set; } = [];
    public byte[] EncryptionTag { get; set; } = [];
}
