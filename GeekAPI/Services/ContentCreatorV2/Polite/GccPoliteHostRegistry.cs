using System.Collections.Concurrent;
using TurnerSoftware.RobotsExclusionTools;

namespace GeekAPI.Services.ContentCreatorV2.Polite;

/// <summary>
/// Process-wide per-origin traffic controllers and robots.txt cache.
/// Survives transient typed <see cref="GccV2PoliteCrawler"/> instances.
/// </summary>
public sealed class GccV2PoliteHostRegistry
{
    private readonly ConcurrentDictionary<string, HostTrafficController> _controllers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, RobotsFile?> _robotsCache = new(StringComparer.OrdinalIgnoreCase);

    public HostTrafficController GetController(string origin) =>
        _controllers.GetOrAdd(origin, _ => new HostTrafficController());

    public bool TryGetRobots(string origin, out RobotsFile? robots) =>
        _robotsCache.TryGetValue(origin, out robots);

    public void SetRobots(string origin, RobotsFile? robots) =>
        _robotsCache[origin] = robots;

    /// <summary>Test helper — clear state between cases that share the singleton.</summary>
    public void Clear()
    {
        _controllers.Clear();
        _robotsCache.Clear();
    }
}
