using GeekAPI.Services.SiteAnalyzer2;

namespace GeekAPI.Services.ContentCreatorV2.BrandKit;

/// <summary>
/// Derives a provisional brand/voice kit from an existing site analysis profile.
/// Read-only: talks only to <see cref="SiteAnalyzer2SiteProfileReader"/> (already registered for
/// GeekAPI, backed by <c>SITE_ANALYZER2_DATABASE_URL</c>) — never edits Geek-SEO/SiteAnalyzer2
/// source or data. If the profile can't be loaded for any reason, returns a structurally valid,
/// empty kit rather than failing the caller's generate flow.
/// </summary>
public sealed class GccV2BrandKitBuilder
{
    private readonly SiteAnalyzer2SiteProfileReader _profileReader;
    private readonly ILogger<GccV2BrandKitBuilder> _logger;

    public GccV2BrandKitBuilder(SiteAnalyzer2SiteProfileReader profileReader, ILogger<GccV2BrandKitBuilder> logger)
    {
        _profileReader = profileReader;
        _logger = logger;
    }

    public async Task<GccV2BrandKitContent> BuildAsync(Guid siteAnalysisProfileId, CancellationToken ct)
    {
        try
        {
            var profile = await _profileReader.GetByIdAsync(siteAnalysisProfileId, ct);
            if (profile is null)
            {
                _logger.LogInformation(
                    "No site analysis profile {ProfileId} found; returning empty brand kit shape.",
                    siteAnalysisProfileId);
                return EmptyKit();
            }

            var areaServed = profile.GeoAnchorNodes.Count > 0
                ? profile.GeoAnchorNodes
                : (string.IsNullOrWhiteSpace(profile.ServiceAreaDescription)
                    ? Array.Empty<string>()
                    : [profile.ServiceAreaDescription]);

            return new GccV2BrandKitContent
            {
                CompanyName = string.IsNullOrWhiteSpace(profile.DisplayName) ? HostFromUrl(profile.SiteUrl) : profile.DisplayName,
                Website = profile.SiteUrl,
                CompanyDescription = profile.BusinessSummary,
                Tagline = null,
                PositioningOneLiner = profile.FocusDescription,
                Audiences = [],
                Features = profile.FocusTags,
                KnowsAbout = profile.FocusTags,
                AreaServed = areaServed,
                SameAs = profile.AuthorityPageUrls,
                VoiceSamples = [],
                VoiceGuidance = profile.WritingRecommendations,
                VoiceStatus = "provisional",
                CtaPhrases = [],
                Notes = [],
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Brand kit build failed for profile {ProfileId}; returning empty shape.", siteAnalysisProfileId);
            return EmptyKit();
        }
    }

    private static GccV2BrandKitContent EmptyKit() => new()
    {
        VoiceStatus = "provisional",
        Notes = ["crawl data unavailable"],
    };

    private static string? HostFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url;
    }
}

/// <summary>Shape persisted as <c>GccV2BrandKit.KitJson</c> and summarized in the
/// <c>BrandKitReady</c> job event.</summary>
public sealed record GccV2BrandKitContent
{
    public string? CompanyName { get; init; }
    public string? Website { get; init; }
    public string? CompanyDescription { get; init; }
    public string? Tagline { get; init; }
    public string? PositioningOneLiner { get; init; }
    public IReadOnlyList<string> Audiences { get; init; } = [];
    public IReadOnlyList<string> Features { get; init; } = [];
    public IReadOnlyList<string> KnowsAbout { get; init; } = [];
    public IReadOnlyList<string> AreaServed { get; init; } = [];
    public IReadOnlyList<string> SameAs { get; init; } = [];
    public IReadOnlyList<string> VoiceSamples { get; init; } = [];
    public IReadOnlyList<string> VoiceGuidance { get; init; } = [];
    public string VoiceStatus { get; init; } = "provisional";
    public IReadOnlyList<string> CtaPhrases { get; init; } = [];
    public IReadOnlyList<string> Notes { get; init; } = [];
}
