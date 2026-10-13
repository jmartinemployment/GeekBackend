using System.Text;
using GeekAPI.Services.ContentCreator.Guardrail;
using GeekAPI.Services.Workflow.Services;
using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// The page a Repurpose writes from: its type, its name on the project, its title and its text.
/// </summary>
/// <param name="PageId">The page on the project. Every piece written from it is saved under it.</param>
/// <param name="Type">The page's content type as stored: pillar, blog or tool.</param>
/// <param name="Name">The page's name: the keyword for a pillar or a blog, the product for a tool page.
/// A piece written from the page carries the same name, so a page has one piece of each type.</param>
/// <param name="Title">The page's own title, or its name when the stored body carries none.</param>
/// <param name="Text">Every heading and paragraph of the page, in order, as text.</param>
public sealed record GccRepurposeSource(Guid PageId, string Type, string Name, string Title, string Text);

/// <summary>
/// Repurpose: a short piece (an email, a social post, an ad) written from one approved page of the
/// project, and saved under that page.
/// </summary>
/// <remarks>
/// <para>
/// <b>The one way a short piece is written</b> (Jeff, 2026-10-12: short content types "ARE DERIVED OR
/// REPURPOSED FROM EXISTING CONTENT"). Until then Email, Social and Ads were ticked on Generate and
/// written from the Brief alone, with no page in front of the writer, and the Repurpose route answered
/// 403: it was given no Brief, never checked that the project's site was crawled, replied in a shape
/// the form could not show, saved under types the form had no tab for, and tied nothing it made to the
/// page it came from.
/// </para>
/// <para>
/// <b>What the writer is handed.</b> The page's own text, whole, and from the Brief what a short piece
/// was already handed: the reader, the voice and the problem. No research and no search: the page
/// already used the research, and a piece that went back to it could say what the approved page does
/// not.
/// </para>
/// <para>
/// <b>One check, no second attempt.</b> A figure in the piece must be in the page it was written from
/// (<see cref="FigureRefusal"/>). A piece that states another is refused and nothing is saved for it;
/// the other pieces of the run are kept.
/// </para>
/// </remarks>
public static class GccRepurpose
{
    /// <summary>The types a Repurpose writes, as their letters in lower case.</summary>
    private static readonly HashSet<string> ShortTypes = new(StringComparer.Ordinal)
    {
        "email", "emailcoldoutreach",
        "social", "ads", "linkedin", "x", "instagram", "facebook", "metaads", "googleads",
    };

    /// <summary>The types of page a Repurpose is written from, as their letters in lower case.</summary>
    private static readonly HashSet<string> SourceTypes = new(StringComparer.Ordinal) { "pillar", "blog", "tool", "aitool" };

    /// <summary>What the operator is told a Repurpose writes from, in every refusal that says so.</summary>
    public const string FromWhat = "an approved pillar, blog or tool page";

    /// <summary>Whether a requested type is a short piece, written by Repurpose and never by Generate.</summary>
    public static bool IsShortType(string? requestedType) =>
        !string.IsNullOrWhiteSpace(requestedType) && ShortTypes.Contains(GccGenerationCoordinator.LettersOf(requestedType));

    /// <summary>Whether a short type is the email, the one short piece with a subject and a button.</summary>
    public static bool IsEmail(string requestedType) =>
        GccGenerationCoordinator.LettersOf(requestedType) is "email" or "emailcoldoutreach";

    /// <summary>The platform a social post or an ad is written for, as the writer's style guidance reads it.</summary>
    public static string PlatformOf(string requestedType) => GccGenerationCoordinator.LettersOf(requestedType) switch
    {
        "x" => "X",
        "instagram" => "Instagram",
        "metaads" => "MetaAds",
        "googleads" => "GoogleAds",
        var letters => letters,
    };

    /// <summary>
    /// What a piece for a platform is called, to its writer and in a refusal the operator reads: "LinkedIn
    /// post", "ad". The email is "email".
    /// </summary>
    public static string PieceName(string platform) => platform switch
    {
        "ads" => "ad",
        "MetaAds" => "Meta ad",
        "GoogleAds" => "Google ad",
        "linkedin" => "LinkedIn post",
        "facebook" => "Facebook post",
        "social" => "social post",
        _ => $"{platform} post",
    };

    /// <summary>
    /// What a Generate is told when it is asked for a short piece, or null when it is asked for none.
    /// </summary>
    public static string? GenerateRefusal(IReadOnlyList<string> requested)
    {
        var shortOnes = requested.Where(IsShortType).ToList();
        return shortOnes.Count == 0
            ? null
            : $"Refused: '{string.Join("', '", shortOnes)}' "
                + (shortOnes.Count == 1 ? "is" : "are")
                + $" written by Repurpose from {FromWhat}, not by Generate. Nothing was generated.";
    }

    /// <summary>
    /// What a Repurpose is told when its types cannot be written, or null: it must name at least one,
    /// and every one must be a short piece.
    /// </summary>
    public static string? TypesRefusal(IReadOnlyList<string> requested)
    {
        if (requested.Count == 0)
            return "Refused: name at least one piece to write. Repurpose has no default.";

        var others = requested.Where(type => !IsShortType(type)).ToList();
        return others.Count == 0
            ? null
            : $"Refused: Repurpose writes an email, a social post or an ad from a page; '{string.Join("', '", others)}' "
                + (others.Count == 1 ? "is" : "are") + " not one of those. Nothing was written.";
    }

    /// <summary>
    /// The page as a Repurpose reads it, or why it cannot be written from. Nothing is searched and
    /// nothing is paid for before this is known.
    /// </summary>
    /// <param name="page">The page on the project.</param>
    /// <param name="bodyJson">The page's one version, as stored.</param>
    public static (GccRepurposeSource? Source, string? Refusal) SourceOf(GccArtifactDto page, string? bodyJson)
    {
        if (page.ParentArtifactId is not null)
            return (null, $"Refused: Repurpose writes from {FromWhat}, and this is a piece written from one.");

        if (!SourceTypes.Contains(GccGenerationCoordinator.LettersOf(page.Type)))
            return (null, $"Refused: Repurpose writes from {FromWhat}; this page is a {page.Type.Trim()}.");

        if (!string.Equals(page.Status, "approved", StringComparison.OrdinalIgnoreCase))
            return (null, "Refused: approve the page before Repurpose. A piece is written from a page that has been approved.");

        var read = GccBodyEnvelope.Read(bodyJson, GccDocumentJson.Options);
        var text = ContentDocumentText.Flatten(read.Document).Trim();
        if (text.Length == 0)
            return (null, "Refused: the page has no text to write from.");

        var title = string.IsNullOrWhiteSpace(read.Title) ? page.Name.Trim() : read.Title.Trim();
        return (new GccRepurposeSource(page.Id, page.Type.Trim(), page.Name.Trim(), title, text), null);
    }

    /// <summary>The page, as the writer of a short piece is shown it.</summary>
    /// <param name="piece">What is being written: "email", "LinkedIn post".</param>
    public static string SourceBlock(GccRepurposeSource source, string piece)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== THE PAGE THIS {piece.ToUpperInvariant()} IS WRITTEN FROM ===");
        sb.AppendLine($"An approved {source.Type} page of this publisher's: \"{source.Title}\".");
        sb.AppendLine();
        sb.AppendLine(source.Text);
        sb.AppendLine("=== END OF THE PAGE ===");
        sb.AppendLine($"Write the {piece} from this page. Take the one idea of it that serves this reader best and make that");
        sb.AppendLine("idea's case; do not summarise the whole page. Say nothing the page does not say: a figure, a product");
        sb.AppendLine("name or a claim may appear only if the page states it, and a figure is written exactly as the page");
        sb.Append("writes it. Do not reprint the page's sentences.");
        return sb.ToString();
    }

    /// <summary>
    /// What a piece is refused with when it states a figure the page it was written from does not, or
    /// null. What a figure is, and is not, is <see cref="GccFigureGrammar"/>, as it is for a page.
    /// </summary>
    /// <param name="piece">What was written: "email", "LinkedIn post".</param>
    /// <param name="written">Every part of the piece the reader sees, a part to an entry.</param>
    public static string? FigureRefusal(string piece, IReadOnlyList<string?> written, GccRepurposeSource source)
    {
        var known = GccFigureGrammar.NumbersIn(source.Title + Environment.NewLine + source.Text);
        var unsupported = new List<string>();
        foreach (var part in written)
        {
            if (string.IsNullOrWhiteSpace(part)) continue;
            foreach (var figure in GccFigureGrammar.Find(part))
            {
                if (!known.Contains(figure.Value)) unsupported.Add(figure.Written);
            }
        }

        return unsupported.Count == 0
            ? null
            : $"Refused: the {piece} states "
                + (unsupported.Distinct(StringComparer.Ordinal).Count() == 1 ? "a figure" : "figures")
                + $" the page it was written from does not: {string.Join(", ", unsupported.Distinct(StringComparer.Ordinal))}. "
                + "Nothing was saved for it.";
    }
}
