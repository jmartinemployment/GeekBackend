using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GeekRepository.Data.Entities.GeekCrawler;
using MongoDB.Bson;
using MongoDB.Driver;

namespace GeekRepository.Services;

public interface IMongoGeekCrawlerService
{
    // READ: Pages
    Task<List<GeekCrawlerPage>> ListPagesByRunAsync(Guid runId, int limit, int offset, CancellationToken ct = default);
    Task<List<GeekCrawlerPage>> ListPagesBySeedsAsync(Guid runId, IReadOnlyList<string> seeds, CancellationToken ct = default);
    Task<List<GeekCrawlerPageResumeRow>> ListPagesByRunForResumeAsync(Guid runId, int limit, int offset, CancellationToken ct = default);
    Task<int> CountPagesByRunAsync(Guid runId, CancellationToken ct = default);
    Task<DateTimeOffset?> GetLastCrawledTimeAsync(Guid runId, CancellationToken ct = default);

    // WRITE: Pages
    Task<List<(string Url, Guid PageId)>> CreatePagesBatchAsync(Guid runId, IReadOnlyList<GeekCrawlerPage> pages, CancellationToken ct = default);

    // READ: Runs
    Task<GeekCrawlerRun?> GetRunByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<GeekCrawlerRun>> ListRunsByUserAsync(string ownerUserId, string? crawlType = null, int limit = 50, CancellationToken ct = default);
    Task<GeekCrawlerRun?> GetLatestRunAsync(string ownerUserId, string crawlType, string seedsJson, CancellationToken ct = default);
    Task<GeekCrawlerRun?> GetRunForSlotAsync(string ownerUserId, string crawlType, string seedKey, CancellationToken ct = default);
    Task<List<GeekCrawlerRun>> ListRunsByStatusAsync(string status, int limit = 200, CancellationToken ct = default);

    // READ: Links
    Task<List<GeekCrawlerLink>> ListLinksByRunAsync(Guid runId, bool? sameOrigin = null, int limit = 100, int offset = 0, CancellationToken ct = default);
    Task<List<GeekCrawlerLinkResumeRow>> ListLinksByRunForResumeAsync(Guid runId, int limit = 500, DateTimeOffset? afterDiscoveredAtUtc = null, Guid? afterId = null, CancellationToken ct = default);
    Task<int> CountLinksByRunAsync(Guid runId, CancellationToken ct = default);
    Task<DateTimeOffset?> GetLastLinkDiscoveredTimeAsync(Guid runId, CancellationToken ct = default);

    // READ: Schedules
    Task<List<GeekCrawlerSchedule>> ListSchedulesForUserAsync(string ownerUserId, string? crawlType = null, int limit = 50, CancellationToken ct = default);
    Task<List<GeekCrawlerSchedule>> ListDueSchedulesAsync(DateTimeOffset beforeUtc, int limit = 50, CancellationToken ct = default);
    Task<GeekCrawlerSchedule?> GetScheduleByIdAsync(Guid id, CancellationToken ct = default);

    // WRITE: Runs
    Task<GeekCrawlerRun> CreateRunAsync(GeekCrawlerRun run, CancellationToken ct = default);
    Task UpdateRunAsync(Guid id, Action<GeekCrawlerRun> updateAction, CancellationToken ct = default);
    Task DeleteRunCrawlDataAsync(Guid runId, CancellationToken ct = default);

    // WRITE: Links
    Task<int> InsertLinksIgnoringDuplicatesAsync(Guid runId, IReadOnlyList<(Guid PageId, string FromUrl, string LinkUrl, bool IsSameOrigin)> links, CancellationToken ct = default);

    // WRITE: Schedules
    Task<GeekCrawlerSchedule> CreateScheduleAsync(GeekCrawlerSchedule schedule, CancellationToken ct = default);
    Task UpdateScheduleAsync(Guid id, Action<GeekCrawlerSchedule> updateAction, CancellationToken ct = default);
    Task DeleteScheduleAsync(Guid id, CancellationToken ct = default);
    Task<GeekCrawlerSchedule?> ClaimDueScheduleAsync(Guid id, DateTimeOffset expectedNextRunUtc, DateTimeOffset newNextRunUtc, DateTimeOffset? lastStartedUtc, CancellationToken ct = default);
}

public sealed class MongoGeekCrawlerService : IMongoGeekCrawlerService
{
    private readonly IMongoDatabase _db;
    private readonly ILogger<MongoGeekCrawlerService> _logger;

    public MongoGeekCrawlerService(string mongoConnectionString, ILogger<MongoGeekCrawlerService> logger)
    {
        if (string.IsNullOrWhiteSpace(mongoConnectionString))
            throw new ArgumentNullException(nameof(mongoConnectionString));

        var client = new MongoClient(mongoConnectionString);
        _db = client.GetDatabase("geek_crawler");
        _logger = logger;
    }

    public async Task<List<GeekCrawlerPage>> ListPagesByRunAsync(Guid runId, int limit, int offset, CancellationToken ct = default)
    {
        if (runId == Guid.Empty) throw new ArgumentException("runId is required", nameof(runId));
        limit = Math.Clamp(limit, 1, 500);
        offset = Math.Max(0, offset);

        try
        {
            var collection = _db.GetCollection<GeekCrawlerPage>("crawl_pages");
            var pages = await collection
                .Find(p => p.RunId == runId)
                .Sort(Builders<GeekCrawlerPage>.Sort.Ascending(p => p.CrawledAtUtc))
                .Skip(offset)
                .Limit(limit)
                .ToListAsync(ct);
            return pages;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list pages by run {RunId}", runId);
            throw;
        }
    }

    public async Task<List<GeekCrawlerPage>> ListPagesBySeedsAsync(Guid runId, IReadOnlyList<string> seeds, CancellationToken ct = default)
    {
        if (runId == Guid.Empty) throw new ArgumentException("runId is required", nameof(runId));
        if (seeds == null || seeds.Count == 0) throw new ArgumentException("seeds are required", nameof(seeds));

        try
        {
            var seedList = seeds.Distinct(StringComparer.OrdinalIgnoreCase).Take(32).ToList();
            var collection = _db.GetCollection<GeekCrawlerPage>("crawl_pages");
            var pages = await collection
                .Find(p => p.RunId == runId && (seedList.Contains(p.Url) || seedList.Contains(p.FinalUrl)))
                .Sort(Builders<GeekCrawlerPage>.Sort.Ascending(p => p.CrawledAtUtc))
                .ToListAsync(ct);
            return pages;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list pages by seeds for run {RunId}", runId);
            throw;
        }
    }

    public async Task<List<GeekCrawlerPageResumeRow>> ListPagesByRunForResumeAsync(Guid runId, int limit, int offset, CancellationToken ct = default)
    {
        if (runId == Guid.Empty) throw new ArgumentException("runId is required", nameof(runId));
        limit = Math.Clamp(limit, 1, 500);
        offset = Math.Max(0, offset);

        try
        {
            var collection = _db.GetCollection<BsonDocument>("crawl_pages");
            var rows = await collection
                .Find(new BsonDocument("RunId", runId.ToString()))
                .Sort(Builders<BsonDocument>.Sort.Ascending("CrawledAtUtc"))
                .Skip(offset)
                .Limit(limit)
                .Project(Builders<BsonDocument>.Projection
                    .Include("Origin")
                    .Include("Url")
                    .Include("Html"))
                .ToListAsync(ct);

            return rows.Select(doc =>
                new GeekCrawlerPageResumeRow(
                    doc["Origin"].AsString,
                    doc["Url"].AsString,
                    !string.IsNullOrEmpty(doc.GetValue("Html", BsonNull.Value).AsString)
                )).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list pages for resume {RunId}", runId);
            throw;
        }
    }

    public async Task<int> CountPagesByRunAsync(Guid runId, CancellationToken ct = default)
    {
        if (runId == Guid.Empty) throw new ArgumentException("runId is required", nameof(runId));

        try
        {
            var collection = _db.GetCollection<GeekCrawlerPage>("crawl_pages");
            var count = await collection.CountDocumentsAsync(p => p.RunId == runId, cancellationToken: ct);
            return (int)count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to count pages for run {RunId}", runId);
            throw;
        }
    }

    public async Task<DateTimeOffset?> GetLastCrawledTimeAsync(Guid runId, CancellationToken ct = default)
    {
        if (runId == Guid.Empty) throw new ArgumentException("runId is required", nameof(runId));

        try
        {
            var collection = _db.GetCollection<GeekCrawlerPage>("crawl_pages");
            var page = await collection
                .Find(p => p.RunId == runId)
                .Sort(Builders<GeekCrawlerPage>.Sort.Descending(p => p.CrawledAtUtc))
                .FirstOrDefaultAsync(ct);
            return page?.CrawledAtUtc;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get last crawled time for run {RunId}", runId);
            throw;
        }
    }

    public async Task<List<(string Url, Guid PageId)>> CreatePagesBatchAsync(
        Guid runId,
        IReadOnlyList<GeekCrawlerPage> pages,
        CancellationToken ct = default)
    {
        if (runId == Guid.Empty) throw new ArgumentException("runId is required", nameof(runId));
        if (pages == null || pages.Count == 0) throw new ArgumentException("pages are required", nameof(pages));

        try
        {
            var collection = _db.GetCollection<GeekCrawlerPage>("crawl_pages");
            await collection.InsertManyAsync(pages, cancellationToken: ct);
            return pages.Select(p => (p.Url, p.Id)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create pages batch for run {RunId}", runId);
            throw;
        }
    }

    public async Task<GeekCrawlerRun?> GetRunByIdAsync(Guid id, CancellationToken ct = default)
    {
        if (id == Guid.Empty) throw new ArgumentException("id is required", nameof(id));

        try
        {
            var collection = _db.GetCollection<GeekCrawlerRun>("crawl_runs");
            var run = await collection.Find(r => r.Id == id).FirstOrDefaultAsync(ct);
            return run;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get run {RunId}", id);
            throw;
        }
    }

    public async Task<List<GeekCrawlerRun>> ListRunsByUserAsync(string ownerUserId, string? crawlType = null, int limit = 50, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId)) throw new ArgumentException("ownerUserId is required", nameof(ownerUserId));

        try
        {
            var collection = _db.GetCollection<GeekCrawlerRun>("crawl_runs");
            var filterBuilder = Builders<GeekCrawlerRun>.Filter;
            var filter = filterBuilder.Eq(r => r.OwnerUserId, ownerUserId);

            if (!string.IsNullOrWhiteSpace(crawlType))
                filter = filter & filterBuilder.Eq(r => r.CrawlType, crawlType);

            var runs = await collection
                .Find(filter)
                .Sort(Builders<GeekCrawlerRun>.Sort.Descending(r => r.CreatedAtUtc))
                .Limit(limit)
                .ToListAsync(ct);
            return runs;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list runs for user {UserId}", ownerUserId);
            throw;
        }
    }

    public async Task<GeekCrawlerRun?> GetLatestRunAsync(string ownerUserId, string crawlType, string seedsJson, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId) || string.IsNullOrWhiteSpace(crawlType) || string.IsNullOrWhiteSpace(seedsJson))
            throw new ArgumentException("All parameters required");

        try
        {
            var collection = _db.GetCollection<GeekCrawlerRun>("crawl_runs");
            var run = await collection
                .Find(r => r.OwnerUserId == ownerUserId && r.CrawlType == crawlType && r.SeedUrlsJson == seedsJson)
                .Sort(Builders<GeekCrawlerRun>.Sort.Descending(r => r.CreatedAtUtc))
                .FirstOrDefaultAsync(ct);
            return run;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get latest run for user {UserId}", ownerUserId);
            throw;
        }
    }

    public async Task<GeekCrawlerRun?> GetRunForSlotAsync(string ownerUserId, string crawlType, string seedKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId) || string.IsNullOrWhiteSpace(crawlType) || string.IsNullOrWhiteSpace(seedKey))
            throw new ArgumentException("All parameters required");

        try
        {
            var collection = _db.GetCollection<GeekCrawlerRun>("crawl_runs");
            var run = await collection
                .Find(r => r.OwnerUserId == ownerUserId && r.CrawlType == crawlType && r.SeedKey == seedKey)
                .Sort(Builders<GeekCrawlerRun>.Sort.Descending(r => r.CreatedAtUtc))
                .FirstOrDefaultAsync(ct);
            return run;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get run for slot for user {UserId}", ownerUserId);
            throw;
        }
    }

    public async Task<List<GeekCrawlerRun>> ListRunsByStatusAsync(string status, int limit = 200, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(status)) throw new ArgumentException("status is required", nameof(status));
        limit = Math.Clamp(limit, 1, 200);

        try
        {
            var collection = _db.GetCollection<GeekCrawlerRun>("crawl_runs");
            var normalized = status.Trim().ToLowerInvariant();
            var runs = await collection
                .Find(r => r.Status.ToLower() == normalized)
                .Sort(Builders<GeekCrawlerRun>.Sort.Ascending(r => r.CreatedAtUtc))
                .Limit(limit)
                .ToListAsync(ct);
            return runs;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list runs by status {Status}", status);
            throw;
        }
    }

    public async Task<List<GeekCrawlerLink>> ListLinksByRunAsync(Guid runId, bool? sameOrigin = null, int limit = 100, int offset = 0, CancellationToken ct = default)
    {
        if (runId == Guid.Empty) throw new ArgumentException("runId is required", nameof(runId));
        limit = Math.Clamp(limit, 1, 500);
        offset = Math.Max(0, offset);

        try
        {
            var collection = _db.GetCollection<GeekCrawlerLink>("crawl_links");
            var filterBuilder = Builders<GeekCrawlerLink>.Filter;
            var filter = filterBuilder.Eq(l => l.RunId, runId);

            if (sameOrigin.HasValue)
                filter = filter & filterBuilder.Eq(l => l.IsSameOrigin, sameOrigin.Value);

            var links = await collection
                .Find(filter)
                .Sort(Builders<GeekCrawlerLink>.Sort.Ascending(l => l.DiscoveredAtUtc))
                .Skip(offset)
                .Limit(limit)
                .ToListAsync(ct);
            return links;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list links for run {RunId}", runId);
            throw;
        }
    }

    public async Task<List<GeekCrawlerLinkResumeRow>> ListLinksByRunForResumeAsync(Guid runId, int limit = 500, DateTimeOffset? afterDiscoveredAtUtc = null, Guid? afterId = null, CancellationToken ct = default)
    {
        if (runId == Guid.Empty) throw new ArgumentException("runId is required", nameof(runId));
        limit = Math.Clamp(limit, 1, 500);

        try
        {
            var collection = _db.GetCollection<GeekCrawlerLink>("crawl_links");
            var filterBuilder = Builders<GeekCrawlerLink>.Filter;
            var filter = filterBuilder.Eq(l => l.RunId, runId);

            if (afterDiscoveredAtUtc.HasValue && afterId.HasValue)
            {
                filter = filter & filterBuilder.Or(
                    filterBuilder.Gte(l => l.DiscoveredAtUtc, afterDiscoveredAtUtc.Value),
                    filterBuilder.Eq(l => l.Id, afterId.Value));
            }

            var links = await collection
                .Find(filter)
                .Sort(Builders<GeekCrawlerLink>.Sort.Ascending(l => l.DiscoveredAtUtc).Ascending(l => l.Id))
                .Limit(limit)
                .ToListAsync(ct);

            return links.Select(l => new GeekCrawlerLinkResumeRow(l.LinkUrl, l.DiscoveredAtUtc, l.Id)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list links for resume {RunId}", runId);
            throw;
        }
    }

    public async Task<int> CountLinksByRunAsync(Guid runId, CancellationToken ct = default)
    {
        if (runId == Guid.Empty) throw new ArgumentException("runId is required", nameof(runId));

        try
        {
            var collection = _db.GetCollection<GeekCrawlerLink>("crawl_links");
            var count = await collection.CountDocumentsAsync(l => l.RunId == runId, cancellationToken: ct);
            return (int)count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to count links for run {RunId}", runId);
            throw;
        }
    }

    public async Task<DateTimeOffset?> GetLastLinkDiscoveredTimeAsync(Guid runId, CancellationToken ct = default)
    {
        if (runId == Guid.Empty) throw new ArgumentException("runId is required", nameof(runId));

        try
        {
            var collection = _db.GetCollection<GeekCrawlerLink>("crawl_links");
            var link = await collection
                .Find(l => l.RunId == runId)
                .Sort(Builders<GeekCrawlerLink>.Sort.Descending(l => l.DiscoveredAtUtc))
                .FirstOrDefaultAsync(ct);
            return link?.DiscoveredAtUtc;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get last link discovered time for run {RunId}", runId);
            throw;
        }
    }

    public async Task<List<GeekCrawlerSchedule>> ListSchedulesForUserAsync(string ownerUserId, string? crawlType = null, int limit = 50, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId)) throw new ArgumentException("ownerUserId is required", nameof(ownerUserId));

        try
        {
            var collection = _db.GetCollection<GeekCrawlerSchedule>("crawl_schedules");
            var filterBuilder = Builders<GeekCrawlerSchedule>.Filter;
            var filter = filterBuilder.Eq(s => s.OwnerUserId, ownerUserId);

            if (!string.IsNullOrWhiteSpace(crawlType))
                filter = filter & filterBuilder.Eq(s => s.CrawlType, crawlType);

            var schedules = await collection
                .Find(filter)
                .Limit(limit)
                .ToListAsync(ct);
            return schedules;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list schedules for user {UserId}", ownerUserId);
            throw;
        }
    }

    public async Task<List<GeekCrawlerSchedule>> ListDueSchedulesAsync(DateTimeOffset beforeUtc, int limit = 50, CancellationToken ct = default)
    {
        try
        {
            var collection = _db.GetCollection<GeekCrawlerSchedule>("crawl_schedules");
            var schedules = await collection
                .Find(s => s.Enabled && s.NextRunUtc <= beforeUtc)
                .Sort(Builders<GeekCrawlerSchedule>.Sort.Ascending(s => s.NextRunUtc))
                .Limit(limit)
                .ToListAsync(ct);
            return schedules;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list due schedules");
            throw;
        }
    }

    public async Task<GeekCrawlerSchedule?> GetScheduleByIdAsync(Guid id, CancellationToken ct = default)
    {
        if (id == Guid.Empty) throw new ArgumentException("id is required", nameof(id));

        try
        {
            var collection = _db.GetCollection<GeekCrawlerSchedule>("crawl_schedules");
            var schedule = await collection.Find(s => s.Id == id).FirstOrDefaultAsync(ct);
            return schedule;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get schedule {ScheduleId}", id);
            throw;
        }
    }

    public async Task<GeekCrawlerRun> CreateRunAsync(GeekCrawlerRun run, CancellationToken ct = default)
    {
        if (run is null) throw new ArgumentNullException(nameof(run));
        if (run.Id == Guid.Empty) throw new ArgumentException("run.Id is required", nameof(run));

        try
        {
            var collection = _db.GetCollection<GeekCrawlerRun>("crawl_runs");
            await collection.InsertOneAsync(run, cancellationToken: ct);
            return run;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create run");
            throw;
        }
    }

    public async Task UpdateRunAsync(Guid id, Action<GeekCrawlerRun> updateAction, CancellationToken ct = default)
    {
        if (id == Guid.Empty) throw new ArgumentException("id is required", nameof(id));
        if (updateAction is null) throw new ArgumentNullException(nameof(updateAction));

        try
        {
            var collection = _db.GetCollection<GeekCrawlerRun>("crawl_runs");
            var run = await collection.Find(r => r.Id == id).FirstOrDefaultAsync(ct);
            if (run is null)
                throw new InvalidOperationException($"Run {id} not found");

            updateAction(run);
            await collection.ReplaceOneAsync(r => r.Id == id, run, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update run {RunId}", id);
            throw;
        }
    }

    public async Task DeleteRunCrawlDataAsync(Guid runId, CancellationToken ct = default)
    {
        if (runId == Guid.Empty) throw new ArgumentException("runId is required", nameof(runId));

        try
        {
            var linksCollection = _db.GetCollection<GeekCrawlerLink>("crawl_links");
            var pagesCollection = _db.GetCollection<GeekCrawlerPage>("crawl_pages");

            await linksCollection.DeleteManyAsync(l => l.RunId == runId, cancellationToken: ct);
            await pagesCollection.DeleteManyAsync(p => p.RunId == runId, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete crawl data for run {RunId}", runId);
            throw;
        }
    }

    public async Task<int> InsertLinksIgnoringDuplicatesAsync(
        Guid runId,
        IReadOnlyList<(Guid PageId, string FromUrl, string LinkUrl, bool IsSameOrigin)> links,
        CancellationToken ct = default)
    {
        if (runId == Guid.Empty) throw new ArgumentException("runId is required", nameof(runId));
        if (links is null || links.Count == 0) throw new ArgumentException("links are required", nameof(links));

        try
        {
            var collection = _db.GetCollection<GeekCrawlerLink>("crawl_links");
            var now = DateTimeOffset.UtcNow;
            var docsToInsert = new List<GeekCrawlerLink>(links.Count);

            foreach (var link in links)
            {
                docsToInsert.Add(new GeekCrawlerLink
                {
                    Id = Guid.NewGuid(),
                    RunId = runId,
                    PageId = link.PageId,
                    FromUrl = link.FromUrl ?? "",
                    LinkUrl = link.LinkUrl ?? "",
                    IsSameOrigin = link.IsSameOrigin,
                    DiscoveredAtUtc = now,
                });
            }

            var insertedCount = 0;
            foreach (var doc in docsToInsert)
            {
                try
                {
                    await collection.InsertOneAsync(doc, cancellationToken: ct);
                    insertedCount++;
                }
                catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
                {
                    continue;
                }
            }

            return insertedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to insert links for run {RunId}", runId);
            throw;
        }
    }

    public async Task<GeekCrawlerSchedule> CreateScheduleAsync(GeekCrawlerSchedule schedule, CancellationToken ct = default)
    {
        if (schedule is null) throw new ArgumentNullException(nameof(schedule));
        if (schedule.Id == Guid.Empty) throw new ArgumentException("schedule.Id is required", nameof(schedule));

        try
        {
            var collection = _db.GetCollection<GeekCrawlerSchedule>("crawl_schedules");
            await collection.InsertOneAsync(schedule, cancellationToken: ct);
            return schedule;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create schedule");
            throw;
        }
    }

    public async Task UpdateScheduleAsync(Guid id, Action<GeekCrawlerSchedule> updateAction, CancellationToken ct = default)
    {
        if (id == Guid.Empty) throw new ArgumentException("id is required", nameof(id));
        if (updateAction is null) throw new ArgumentNullException(nameof(updateAction));

        try
        {
            var collection = _db.GetCollection<GeekCrawlerSchedule>("crawl_schedules");
            var schedule = await collection.Find(s => s.Id == id).FirstOrDefaultAsync(ct);
            if (schedule is null)
                throw new InvalidOperationException($"Schedule {id} not found");

            updateAction(schedule);
            await collection.ReplaceOneAsync(s => s.Id == id, schedule, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update schedule {ScheduleId}", id);
            throw;
        }
    }

    public async Task DeleteScheduleAsync(Guid id, CancellationToken ct = default)
    {
        if (id == Guid.Empty) throw new ArgumentException("id is required", nameof(id));

        try
        {
            var collection = _db.GetCollection<GeekCrawlerSchedule>("crawl_schedules");
            await collection.DeleteOneAsync(s => s.Id == id, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete schedule {ScheduleId}", id);
            throw;
        }
    }

    public async Task<GeekCrawlerSchedule?> ClaimDueScheduleAsync(
        Guid id,
        DateTimeOffset expectedNextRunUtc,
        DateTimeOffset newNextRunUtc,
        DateTimeOffset? lastStartedUtc,
        CancellationToken ct = default)
    {
        if (id == Guid.Empty) throw new ArgumentException("id is required", nameof(id));

        try
        {
            var collection = _db.GetCollection<GeekCrawlerSchedule>("crawl_schedules");
            var now = DateTimeOffset.UtcNow;
            var schedule = await collection.Find(s =>
                s.Id == id
                && s.Enabled
                && s.NextRunUtc <= now
                && s.NextRunUtc == expectedNextRunUtc
            ).FirstOrDefaultAsync(ct);

            if (schedule is null)
                return null;

            schedule.NextRunUtc = newNextRunUtc;
            schedule.LastStartedUtc = lastStartedUtc ?? now;
            await collection.ReplaceOneAsync(s => s.Id == id, schedule, cancellationToken: ct);
            return schedule;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to claim due schedule {ScheduleId}", id);
            throw;
        }
    }
}
