using GeekRepository.Data;
using GeekRepository.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Controllers.Gtm;

/// <summary>
/// Google refresh-token store for Geek-GTM-MCP, reached through GeekAPI's api/gtm/internal/*
/// proxy. GTM is its own product: it shares no code with Geek-SEO, whose schema and credential
/// helper this controller used to borrow before that layer was deleted on 2026-09-29.
/// </summary>
[ApiController]
[Route("repo/gtm/accounts")]
public sealed class GtmAccountsController(GtmDbContext db) : ControllerBase
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

        // Fail closed: no key, no write. Storing the token unencrypted, or recording the row
        // without it, would both be worse than returning nothing -- CLAUDE.md section 2.
        var protectedToken = GtmCredentialProtector.Encrypt(body.RefreshToken.Trim());
        if (protectedToken is not { } secret)
            return StatusCode(StatusCodes.Status503ServiceUnavailable);

        var existing = await db.GtmAccountConnections
            .FirstOrDefaultAsync(c => c.UserId == userId && c.AccountKey == normalizedKey, ct);

        if (existing is null)
        {
            existing = new GtmAccountConnection
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                AccountKey = normalizedKey,
                GoogleEmail = string.IsNullOrWhiteSpace(body.GoogleEmail) ? null : body.GoogleEmail.Trim(),
                EncryptedRefreshToken = secret.Cipher,
                EncryptionIv = secret.Iv,
                EncryptionTag = secret.Tag,
                ConnectedAt = DateTimeOffset.UtcNow,
            };
            db.GtmAccountConnections.Add(existing);
        }
        else
        {
            existing.GoogleEmail = string.IsNullOrWhiteSpace(body.GoogleEmail) ? existing.GoogleEmail : body.GoogleEmail.Trim();
            existing.EncryptedRefreshToken = secret.Cipher;
            existing.EncryptionIv = secret.Iv;
            existing.EncryptionTag = secret.Tag;
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

    private Task<GtmAccountConnection?> FindAsync(Guid userId, string accountKey, CancellationToken ct) =>
        db.GtmAccountConnections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.UserId == userId && c.AccountKey == NormalizeAccountKey(accountKey), ct);

    private static string NormalizeAccountKey(string accountKey) =>
        accountKey.Trim().ToLowerInvariant();

    private static GtmAccountSummary ToSummary(GtmAccountConnection row) => new()
    {
        AccountKey = row.AccountKey,
        GoogleEmail = row.GoogleEmail,
        ConnectedAt = row.ConnectedAt,
    };

    private static GtmAccountDetail ToDetail(GtmAccountConnection row) => new()
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
