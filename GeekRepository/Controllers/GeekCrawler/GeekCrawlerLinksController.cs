using GeekRepository.Auth;
using GeekRepository.Data;
using GeekRepository.Data.Entities.GeekCrawler;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace GeekRepository.Controllers.GeekCrawler;

[ApiController]
[Route("repo/geek-crawler/links")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class GeekCrawlerLinksController : ControllerBase
{
    private readonly GeekCrawlerDbContext _db;

    public GeekCrawlerLinksController(GeekCrawlerDbContext db) => _db = db;

    [HttpGet("activity")]
    public async Task<ActionResult<object>> GetRunActivity(
        [FromQuery] Guid runId,
        CancellationToken ct = default)
    {
        if (runId == Guid.Empty)
            return BadRequest("runId is required");

        var linkCount = await _db.GeekCrawlerLinks.AsNoTracking()
            .CountAsync(l => l.RunId == runId, ct);

        return Ok(new { linkCount });
    }

    [HttpGet("for-resume")]
    public async Task<ActionResult<IReadOnlyList<string>>> ListForResume(
        [FromQuery] Guid runId,
        [FromQuery] int limit = 500,
        [FromQuery] int offset = 0,
        CancellationToken ct = default)
    {
        if (runId == Guid.Empty)
            return BadRequest("runId is required");

        limit = Math.Clamp(limit, 1, 500);
        offset = Math.Max(0, offset);

        var urls = await _db.GeekCrawlerLinks.AsNoTracking()
            .Where(l => l.RunId == runId && l.IsSameOrigin)
            .OrderBy(l => l.DiscoveredAtUtc)
            .ThenBy(l => l.Id)
            .Skip(offset)
            .Take(limit)
            .Select(l => l.LinkUrl)
            .ToListAsync(ct);

        return Ok(urls);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GeekCrawlerLink>>> ListByRun(
        [FromQuery] Guid runId,
        [FromQuery] bool? sameOrigin = null,
        [FromQuery] int limit = 100,
        [FromQuery] int offset = 0,
        CancellationToken ct = default)
    {
        if (runId == Guid.Empty)
            return BadRequest("runId is required");

        limit = Math.Clamp(limit, 1, 500);
        offset = Math.Max(0, offset);

        var query = _db.GeekCrawlerLinks.AsNoTracking().Where(l => l.RunId == runId);
        if (sameOrigin is not null)
            query = query.Where(l => l.IsSameOrigin == sameOrigin.Value);

        var links = await query
            .OrderBy(l => l.DiscoveredAtUtc)
            .ThenBy(l => l.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(ct);
        return Ok(links);
    }

    [HttpPost("batch")]
    public async Task<ActionResult<object>> CreateBatch(
        [FromBody] CreateGeekCrawlerLinkBatchCommand command,
        CancellationToken ct)
    {
        if (command is null || command.RunId == Guid.Empty || command.Links is null || command.Links.Count == 0)
            return BadRequest("runId and links are required");

        var inserted = await InsertLinksIgnoringDuplicatesAsync(command.RunId, command.Links, ct);
        return Ok(new { count = inserted });
    }

    private async Task<int> InsertLinksIgnoringDuplicatesAsync(
        Guid runId,
        IReadOnlyList<CreateGeekCrawlerLinkItem> links,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var ids = new Guid[links.Count];
        var runIds = new Guid[links.Count];
        var pageIds = new Guid[links.Count];
        var fromUrls = new string[links.Count];
        var linkUrls = new string[links.Count];
        var sameOrigins = new bool[links.Count];
        var discoveredAt = new DateTimeOffset[links.Count];

        for (var i = 0; i < links.Count; i++)
        {
            var link = links[i];
            ids[i] = Guid.NewGuid();
            runIds[i] = runId;
            pageIds[i] = link.PageId;
            fromUrls[i] = link.FromUrl ?? "";
            linkUrls[i] = link.LinkUrl ?? "";
            sameOrigins[i] = link.IsSameOrigin;
            discoveredAt[i] = now;
        }

        const string sql = """
            INSERT INTO geek_crawler.crawl_links
                ("Id", "RunId", "PageId", "FromUrl", "LinkUrl", "IsSameOrigin", "DiscoveredAtUtc")
            SELECT *
            FROM UNNEST(
                @ids,
                @runIds,
                @pageIds,
                @fromUrls,
                @linkUrls,
                @sameOrigins,
                @discoveredAt)
            ON CONFLICT ("RunId", "FromUrl", "LinkUrl") DO NOTHING
            """;

        await using var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = ids });
        cmd.Parameters.Add(new NpgsqlParameter("runIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = runIds });
        cmd.Parameters.Add(new NpgsqlParameter("pageIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = pageIds });
        cmd.Parameters.Add(new NpgsqlParameter("fromUrls", NpgsqlDbType.Array | NpgsqlDbType.Varchar) { Value = fromUrls });
        cmd.Parameters.Add(new NpgsqlParameter("linkUrls", NpgsqlDbType.Array | NpgsqlDbType.Varchar) { Value = linkUrls });
        cmd.Parameters.Add(new NpgsqlParameter("sameOrigins", NpgsqlDbType.Array | NpgsqlDbType.Boolean) { Value = sameOrigins });
        cmd.Parameters.Add(new NpgsqlParameter("discoveredAt", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz) { Value = discoveredAt });

        var inserted = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return inserted;
    }

    public record CreateGeekCrawlerLinkBatchCommand(
        Guid RunId,
        IReadOnlyList<CreateGeekCrawlerLinkItem> Links);

    public record CreateGeekCrawlerLinkItem(
        Guid PageId,
        string FromUrl,
        string LinkUrl,
        bool IsSameOrigin);
}
