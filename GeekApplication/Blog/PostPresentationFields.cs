namespace GeekApplication.Blog;

public static class PostPresentationFields
{
    public static IReadOnlyDictionary<string, string> Normalize(
        IReadOnlyDictionary<string, string>? presentation)
    {
        if (presentation is null || presentation.Count == 0)
            return new Dictionary<string, string>();

        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (surface, copy) in presentation)
        {
            if (string.IsNullOrWhiteSpace(surface) || string.IsNullOrWhiteSpace(copy))
                continue;

            normalized[surface.Trim()] = copy.Trim();
        }

        return normalized;
    }
}
