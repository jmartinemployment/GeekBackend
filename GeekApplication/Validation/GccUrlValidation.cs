namespace GeekApplication.Validation;

/// <summary>
/// Validates the site, partner and competitor URLs a project declares. Every one of them becomes a
/// seed for a crawl and, downstream, a lookup key for retrieval
/// (<c>GccGroundingResolver</c>/<c>HostsIndexedAsync</c> in GeekAPI) — a malformed URL there does
/// not fail loudly at the point it was typed, it fails as a confusing refusal or a silent
/// non-match much later, on someone else's draft. Reject it here instead, where the operator is
/// looking at the field that caused it.
/// </summary>
public static class GccUrlValidation
{
    /// <summary>
    /// True for an absolute http/https URL with a non-empty host. Ftp, mailto, javascript,
    /// relative paths and bare words are all rejected — they cannot be crawled and cannot resolve
    /// to a run id.
    /// </summary>
    public static bool IsValid(string? url) =>
        !string.IsNullOrWhiteSpace(url)
        && Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && !string.IsNullOrWhiteSpace(uri.Host);

    /// <summary>
    /// The first invalid URL in <paramref name="urls"/>, or null if every one validates (an empty
    /// or null list validates — these fields are optional).
    /// </summary>
    public static string? FirstInvalid(IReadOnlyList<string>? urls) =>
        urls?.Select(u => u?.Trim() ?? string.Empty)
            .Where(u => u.Length > 0)
            .FirstOrDefault(u => !IsValid(u));
}
