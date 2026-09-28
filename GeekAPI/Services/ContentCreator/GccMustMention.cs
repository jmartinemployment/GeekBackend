namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// The block that tells a draft which of the site's own subtopics it has to cover, written and read
/// in one place.
///
/// <para>
/// The block says those subtopics are compulsory -- "all of which this piece must cover" -- and the
/// heading provenance guard had no source they could be tagged against. Its evidence holds brief
/// fields, PAA questions and competitor headings, nothing else. So a heading written to obey a
/// mandatory instruction had no honest tag, the model reached for the nearest thing and wrote
/// <c>brief:Subtopics the site already treats under it, all of which this piece must cover</c>, and
/// the guard refused the draft. A fail-closed gate rejecting compliant output is the worst way for
/// one to be wrong.
/// </para>
///
/// <para>
/// Format and parse live together because the guard reads back what the controller wrote. Two
/// implementations of the same bullet shape is the drift this codebase keeps paying for.
/// </para>
/// </summary>
public static class GccMustMention
{
    private const string Header = "=== THIS SITE ALREADY COVERS THIS TOPIC ===";
    private const string SubtopicsLine = "Subtopics the site already treats under it, all of which this piece must cover:";
    private const string Bullet = "  - ";

    public static string Format(string matchedHeading, string? sourcePageUrl, IReadOnlyList<string> subtopics)
    {
        var block = new System.Text.StringBuilder()
            .AppendLine(Header)
            .AppendLine($"Matched heading on {sourcePageUrl}: {matchedHeading}");

        if (subtopics.Count > 0)
        {
            block.AppendLine(SubtopicsLine);
            foreach (var subtopic in subtopics)
            {
                block.AppendLine($"{Bullet}{subtopic}");
            }
        }

        block.AppendLine(
            "This is the publisher's own structure, not a suggestion. Cover these, in their terms, " +
            "and do not invent a competing breakdown of the same topic. Where the page above already " +
            "answers something, point the reader at it rather than repeating it here.");
        return block.ToString();
    }

    /// <summary>
    /// The subtopics a block carries, for the guard to license headings against. Empty when the
    /// create has no matched section -- which is not a licence to invent, it is simply no source.
    /// </summary>
    public static IReadOnlyList<string> Subtopics(string? block)
    {
        if (string.IsNullOrWhiteSpace(block)) return [];

        var found = new List<string>();
        foreach (var line in block.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (!trimmed.StartsWith(Bullet, StringComparison.Ordinal)) continue;

            var subtopic = trimmed[Bullet.Length..].Trim();
            if (subtopic.Length > 0) found.Add(subtopic);
        }

        return found;
    }
}
