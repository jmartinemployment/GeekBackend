using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GeekRepository.Data.Entities.GeekCrawler;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
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

    /// <summary>Creates Mongo indexes required for RunId filters and CrawledAtUtc sorts (idempotent).</summary>
    Task EnsureIndexesAsync(CancellationToken ct = default);
}

public sealed class MongoGeekCrawlerService : IMongoGeekCrawlerService
{
    private readonly IMongoDatabase _db;
    private readonly ILogger<MongoGeekCrawlerService> _logger;

    // The collections were imported from a PostgreSQL CSV export, so every value is stored as a
    // string: Guids as "d"-format text, booleans as Postgres "t"/"f", ints as digits, and
    // timestamps as "yyyy-MM-dd HH:mm:ss.ffffff+00". The auto-generated ObjectId _id is ignored;
    // the logical key lives in the separate "Id" string field. These class maps make reads and
    // writes match that shape (the timestamp format is also what keeps string range filters and
    // sorts ordering correctly).
    static MongoGeekCrawlerService()
    {
        var guid = new GuidSerializer(BsonType.String);
        var nullableGuid = new NullableSerializer<Guid>(guid);
        var date = new PgTextDateTimeOffsetSerializer();
        var nullableDate = new NullableSerializer<DateTimeOffset>(date);
        var pgBool = new PgTextBooleanSerializer();
        var pgInt = new PgTextInt32Serializer();

        BsonClassMap.RegisterClassMap<GeekCrawlerRun>(cm =>
        {
            cm.AutoMap();
            cm.SetIgnoreExtraElements(true);
            cm.UnmapMember(x => x.Id);
            cm.MapMember(x => x.Id).SetElementName("Id").SetSerializer(guid);
            cm.MapMember(x => x.CreatedAtUtc).SetSerializer(date);
            cm.MapMember(x => x.StartedAtUtc).SetSerializer(nullableDate);
            cm.MapMember(x => x.CompletedAtUtc).SetSerializer(nullableDate);
        });

        BsonClassMap.RegisterClassMap<GeekCrawlerPage>(cm =>
        {
            cm.AutoMap();
            cm.SetIgnoreExtraElements(true);
            cm.UnmapMember(x => x.Id);
            cm.MapMember(x => x.Id).SetElementName("Id").SetSerializer(guid);
            cm.MapMember(x => x.RunId).SetSerializer(guid);
            cm.MapMember(x => x.StatusCode).SetSerializer(pgInt);
            cm.MapMember(x => x.RobotsAllowed).SetSerializer(pgBool);
            cm.MapMember(x => x.CrawledAtUtc).SetSerializer(date);
        });

        BsonClassMap.RegisterClassMap<GeekCrawlerLink>(cm =>
        {
            cm.AutoMap();
            cm.SetIgnoreExtraElements(true);
            cm.UnmapMember(x => x.Id);
            cm.MapMember(x => x.Id).SetElementName("Id").SetSerializer(guid);
            cm.MapMember(x => x.RunId).SetSerializer(guid);
            cm.MapMember(x => x.PageId).SetSerializer(guid);
            cm.MapMember(x => x.IsSameOrigin).SetSerializer(pgBool);
            cm.MapMember(x => x.DiscoveredAtUtc).SetSerializer(date);
        });

        BsonClassMap.RegisterClassMap<GeekCrawlerSchedule>(cm =>
        {
            cm.AutoMap();
            cm.SetIgnoreExtraElements(true);
            cm.UnmapMember(x => x.Id);
            cm.MapMember(x => x.Id).SetElementName("Id").SetSerializer(guid);
            cm.MapMember(x => x.IntervalHours).SetSerializer(pgInt);
            cm.MapMember(x => x.Enabled).SetSerializer(pgBool);
            cm.MapMember(x => x.NextRunUtc).SetSerializer(date);
            cm.MapMember(x => x.LastStartedUtc).SetSerializer(nullableDate);
            cm.MapMember(x => x.LastRunId).SetSerializer(nullableGuid);
            cm.MapMember(x => x.CreatedAtUtc).SetSerializer(date);
        });
    }

    public MongoGeekCrawlerService(string mongoConnectionString, ILogger<MongoGeekCrawlerService> logger)
    {
        if (string.IsNullOrWhiteSpace(mongoConnectionString))
            throw new ArgumentNullException(nameof(mongoConnectionString));

        var client = new MongoClient(mongoConnectionString);
        _db = client.GetDatabase("geek_crawler");
        _logger = logger;
    }

    public async Task EnsureIndexesAsync(CancellationToken ct = default)
    {
        // PG had ix_crawl_pages_run_id / run_url; Mongo import never recreated them.
        // Without these, pages/activity (count + max CrawledAtUtc) and resume scans COLLSCAN.
        var pages = _db.GetCollection<GeekCrawlerPage>("crawl_pages");
        await pages.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<GeekCrawlerPage>(
                    Builders<GeekCrawlerPage>.IndexKeys
                        .Ascending(p => p.RunId)
                        .Descending(p => p.CrawledAtUtc),
                    new CreateIndexOptions { Name = "ix_crawl_pages_run_crawled", Background = true }),
                new CreateIndexModel<GeekCrawlerPage>(
                    Builders<GeekCrawlerPage>.IndexKeys
                        .Ascending(p => p.RunId)
                        .Ascending(p => p.Url),
                    new CreateIndexOptions { Name = "ix_crawl_pages_run_url", Background = true }),
            ],
            cancellationToken: ct).ConfigureAwait(false);

        var links = _db.GetCollection<GeekCrawlerLink>("crawl_links");
        await links.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<GeekCrawlerLink>(
                    Builders<GeekCrawlerLink>.IndexKeys
                        .Ascending(l => l.RunId)
                        .Ascending(l => l.DiscoveredAtUtc)
                        .Ascending(l => l.Id),
                    new CreateIndexOptions { Name = "ix_crawl_links_run_discovered_id", Background = true }),
                new CreateIndexModel<GeekCrawlerLink>(
                    Builders<GeekCrawlerLink>.IndexKeys
                        .Ascending(l => l.RunId)
                        .Ascending(l => l.FromUrl)
                        .Ascending(l => l.LinkUrl),
                    new CreateIndexOptions { Name = "ix_crawl_links_run_from_link", Background = true }),
            ],
            cancellationToken: ct).ConfigureAwait(false);

        _logger.LogInformation("Geek-Crawler Mongo indexes ensured on crawl_pages and crawl_links.");
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
            // Derive HasHtml in Mongo — never materialize multi-MB Html into the driver
            // (resume only needs Origin/Url/HasHtml; loading Html caused Hostinger 502s).
            var pipeline = new[]
            {
                new BsonDocument("$match", new BsonDocument("RunId", runId.ToString())),
                new BsonDocument("$sort", new BsonDocument("CrawledAtUtc", 1)),
                new BsonDocument("$skip", offset),
                new BsonDocument("$limit", limit),
                new BsonDocument("$project", new BsonDocument
                {
                    { "_id", 0 },
                    { "Origin", 1 },
                    { "Url", 1 },
                    {
                        "HasHtml", new BsonDocument("$cond", new BsonDocument
                        {
                            {
                                "if", new BsonDocument("$eq", new BsonArray
                                {
                                    new BsonDocument("$type", "$Html"),
                                    "string",
                                })
                            },
                            {
                                "then", new BsonDocument("$gt", new BsonArray
                                {
                                    new BsonDocument("$strLenCP", "$Html"),
                                    0,
                                })
                            },
                            { "else", false },
                        })
                    },
                }),
            };

            var rows = await collection.Aggregate<BsonDocument>(pipeline, cancellationToken: ct)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            return rows.Select(doc => new GeekCrawlerPageResumeRow(
                    doc.GetValue("Origin", "").AsString,
                    doc.GetValue("Url", "").AsString,
                    doc.GetValue("HasHtml", false).ToBoolean()))
                .ToList();
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
            // Activity only needs the timestamp. Exclusion-only projection (Mongo forbids
            // mixing Include+Exclude except _id) — never pull multi-MB Html into the sort.
            var page = await collection
                .Find(p => p.RunId == runId)
                .Sort(Builders<GeekCrawlerPage>.Sort.Descending(p => p.CrawledAtUtc))
                .Project<GeekCrawlerPage>(Builders<GeekCrawlerPage>.Projection.Exclude(p => p.Html))
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
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
                .Find(r => r.Status == normalized)
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
            var fb = Builders<GeekCrawlerSchedule>.Filter;
            var schedules = await collection
                .Find(fb.Eq(s => s.Enabled, true) & fb.Lte(s => s.NextRunUtc, beforeUtc))
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
            var fb = Builders<GeekCrawlerSchedule>.Filter;
            var schedule = await collection.Find(
                fb.Eq(s => s.Id, id)
                & fb.Eq(s => s.Enabled, true)
                & fb.Lte(s => s.NextRunUtc, now)
                & fb.Eq(s => s.NextRunUtc, expectedNextRunUtc)
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

/// <summary>Reads/writes timestamps in the PostgreSQL text format the data was imported with
/// ("yyyy-MM-dd HH:mm:ss.ffffff+00"). Values are normalized to UTC so the string ordering stays
/// consistent for range filters and sorts.</summary>
internal sealed class PgTextDateTimeOffsetSerializer : SerializerBase<DateTimeOffset>
{
    public override DateTimeOffset Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        var bsonType = context.Reader.GetCurrentBsonType();
        return bsonType switch
        {
            BsonType.String => DateTimeOffset.Parse(context.Reader.ReadString(), CultureInfo.InvariantCulture),
            BsonType.DateTime => DateTimeOffset.FromUnixTimeMilliseconds(context.Reader.ReadDateTime()),
            _ => throw new FormatException($"Cannot deserialize DateTimeOffset from BsonType {bsonType}."),
        };
    }

    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, DateTimeOffset value)
        => context.Writer.WriteString(value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.ffffff+00", CultureInfo.InvariantCulture));
}

/// <summary>Reads/writes booleans as PostgreSQL "t"/"f" text.</summary>
internal sealed class PgTextBooleanSerializer : SerializerBase<bool>
{
    public override bool Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        var bsonType = context.Reader.GetCurrentBsonType();
        return bsonType switch
        {
            BsonType.String => context.Reader.ReadString() is "t" or "true" or "1",
            BsonType.Boolean => context.Reader.ReadBoolean(),
            _ => throw new FormatException($"Cannot deserialize bool from BsonType {bsonType}."),
        };
    }

    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, bool value)
        => context.Writer.WriteString(value ? "t" : "f");
}

/// <summary>Reads/writes ints as digit strings, tolerating native numeric BSON values.</summary>
internal sealed class PgTextInt32Serializer : SerializerBase<int>
{
    public override int Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        var bsonType = context.Reader.GetCurrentBsonType();
        return bsonType switch
        {
            BsonType.String => int.Parse(context.Reader.ReadString(), CultureInfo.InvariantCulture),
            BsonType.Int32 => context.Reader.ReadInt32(),
            BsonType.Int64 => (int)context.Reader.ReadInt64(),
            BsonType.Double => (int)context.Reader.ReadDouble(),
            _ => throw new FormatException($"Cannot deserialize int from BsonType {bsonType}."),
        };
    }

    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, int value)
        => context.Writer.WriteString(value.ToString(CultureInfo.InvariantCulture));
}
