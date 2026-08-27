namespace GeekAPI.Services.ContentCreator.Polite;

/// <summary>Outcome of one polite fetch — HTML only when status is <see cref="Statuses.Success"/>.</summary>
public sealed record GccPoliteFetchResult(string Status, string? Html)
{
    public bool HasHtml => !string.IsNullOrWhiteSpace(Html);

    public static class Statuses
    {
        public const string Success = "Success";
        public const string BlockedByRobots = "BlockedByRobots";
        public const string RateLimited = "RateLimited";
        public const string HttpError = "HttpError";
        public const string EmptyBody = "EmptyBody";
        public const string ContentTypeSkipped = "ContentTypeSkipped";
        public const string RequestFailed = "RequestFailed";
        public const string CacheHit = "CacheHit";
        public const string ExtractFailed = "ExtractFailed";
    }
}
