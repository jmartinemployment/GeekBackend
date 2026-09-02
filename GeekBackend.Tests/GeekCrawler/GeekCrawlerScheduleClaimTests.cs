using GeekRepository.Controllers.GeekCrawler;
using GeekRepository.Data;
using GeekRepository.Data.Entities.GeekCrawler;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GeekBackend.Tests.GeekCrawler;

public class GeekCrawlerScheduleClaimTests : IDisposable
{
    private readonly GeekCrawlerDbContext _db;

    public GeekCrawlerScheduleClaimTests()
    {
        var options = new DbContextOptionsBuilder<GeekCrawlerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new GeekCrawlerDbContext(options);
    }

    [Fact]
    public async Task ClaimDue_succeeds_once_then_rejects_stale_expectedNextRunUtc()
    {
        var dueAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var schedule = new GeekCrawlerSchedule
        {
            OwnerUserId = "user-1",
            CrawlType = "partner",
            SeedUrlsJson = "[\"https://example.com/\"]",
            Enabled = true,
            NextRunUtc = dueAt,
            CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-1),
        };
        _db.GeekCrawlerSchedules.Add(schedule);
        await _db.SaveChangesAsync();

        var controller = new GeekCrawlerSchedulesController(_db);
        var firstNext = DateTimeOffset.UtcNow.AddHours(168);
        var first = await controller.ClaimDue(
            schedule.Id,
            new GeekCrawlerSchedulesController.ClaimGeekCrawlerScheduleCommand(
                dueAt,
                firstNext),
            CancellationToken.None);

        var second = await controller.ClaimDue(
            schedule.Id,
            new GeekCrawlerSchedulesController.ClaimGeekCrawlerScheduleCommand(
                dueAt,
                DateTimeOffset.UtcNow.AddHours(336)),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(first.Result);
        var claimed = Assert.IsType<GeekCrawlerSchedule>(ok.Value);
        Assert.Equal(firstNext, claimed.NextRunUtc);
        Assert.IsType<NotFoundResult>(second.Result);
    }

    [Fact]
    public async Task ClaimDue_returns_not_found_when_schedule_disabled()
    {
        var dueAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var schedule = new GeekCrawlerSchedule
        {
            OwnerUserId = "user-1",
            CrawlType = "partner",
            SeedUrlsJson = "[\"https://example.com/\"]",
            Enabled = false,
            NextRunUtc = dueAt,
            CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-1),
        };
        _db.GeekCrawlerSchedules.Add(schedule);
        await _db.SaveChangesAsync();

        var controller = new GeekCrawlerSchedulesController(_db);
        var result = await controller.ClaimDue(
            schedule.Id,
            new GeekCrawlerSchedulesController.ClaimGeekCrawlerScheduleCommand(
                dueAt,
                DateTimeOffset.UtcNow.AddHours(168)),
            CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    public void Dispose() => _db.Dispose();
}
