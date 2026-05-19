namespace GeekApplication.Infrastructure;

public static class AiHtmlSanitizer
{
    public static string ToHtmlFragment(string raw)
    {
        var trimmed = raw.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
            return trimmed;

        var lines = trimmed.Split('\n');
        var body = lines.Skip(1).TakeWhile(l => !l.TrimStart().StartsWith("```", StringComparison.Ordinal));
        return string.Join('\n', body).Trim();
    }
}
