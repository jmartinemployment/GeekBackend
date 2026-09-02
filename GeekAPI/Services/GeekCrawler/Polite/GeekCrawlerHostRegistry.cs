using System.Collections.Concurrent;
using TurnerSoftware.RobotsExclusionTools;

namespace GeekAPI.Services.GeekCrawler.Polite;

/// <summary>Process-wide per-origin traffic controllers and robots.txt cache for Geek-Crawler.</summary>
public sealed class GeekCrawlerHostRegistry
{
    private readonly ConcurrentDictionary<string, GeekCrawlerHostTrafficController> _controllers =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, RobotsFile?> _robotsCache =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, byte> _robotsForbidden =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly int _parallelismPerOrigin;

    public GeekCrawlerHostRegistry(GeekCrawlerOptions options)
    {
        _parallelismPerOrigin = options.ParallelismPerOrigin;
    }

    public GeekCrawlerHostTrafficController GetController(string origin) =>
        _controllers.GetOrAdd(origin, _ => new GeekCrawlerHostTrafficController(_parallelismPerOrigin));

    public bool TryGetRobots(string origin, out RobotsFile? robots) =>
        _robotsCache.TryGetValue(origin, out robots);

    public void SetRobots(string origin, RobotsFile? robots) =>
        _robotsCache[origin] = robots;

    public void SetRobotsForbidden(string origin) =>
        _robotsForbidden[origin] = 1;

    public bool IsRobotsForbidden(string origin) =>
        _robotsForbidden.ContainsKey(origin);

    public void Clear()
    {
        _controllers.Clear();
        _robotsCache.Clear();
        _robotsForbidden.Clear();
    }
}
