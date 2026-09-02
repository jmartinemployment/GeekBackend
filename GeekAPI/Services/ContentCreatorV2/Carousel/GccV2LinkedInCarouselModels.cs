namespace GeekAPI.Services.ContentCreatorV2.Carousel;

public sealed record CarouselSlide(
    int Index,
    string Role,
    string Title,
    IReadOnlyList<string> Bullets,
    string? Subtitle = null);

public sealed record LinkedInCarouselDraft(
    IReadOnlyList<CarouselSlide> Slides,
    string Caption,
    IReadOnlyList<string> Hashtags,
    string SuggestedFilename);

public sealed record LinkedInCarouselBrandStyle(
    string PrimaryColor,
    string SecondaryColor,
    string TextColor,
    string BackgroundColor,
    string? CompanyName);

public sealed record LinkedInCarouselArtifact(
    LinkedInCarouselDraft Draft,
    string Slug,
    DateTimeOffset GeneratedAtUtc);

public static class GccV2LinkedInCarouselRoles
{
    public const string Cover = "cover";
    public const string Problem = "problem";
    public const string Teach = "teach";
    public const string Framework = "framework";
    public const string Cta = "cta";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        Cover, Problem, Teach, Framework, Cta,
    };
}
