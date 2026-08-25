namespace GeekRepository.Data.Entities.ContentCreatorV2;

/// <summary>
/// A derived brand/voice kit for a create's owner, built from an existing site analysis profile
/// (Geek-SEO / SiteAnalyzer2 — read-only). <see cref="VoiceStatus"/> starts "provisional" until a
/// human accepts it; never auto-promoted.
/// </summary>
public class GccV2BrandKit
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Optional owning client/tenant — null when derived ad hoc for a single create.</summary>
    public Guid? ClientId { get; set; }

    public Guid DerivedFromProfileId { get; set; }
    public int Version { get; set; } = 1;
    public string KitJson { get; set; } = "{}";

    /// <summary>provisional | accepted</summary>
    public string VoiceStatus { get; set; } = "provisional";

    public DateTimeOffset DerivedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? AcceptedAtUtc { get; set; }
}
