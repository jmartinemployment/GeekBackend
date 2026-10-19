using System.Text;
using GeekAPI.Services.ContentCreator.Guardrail;
using GeekAPI.Services.Workflow.Services;
using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// The page a short piece is written from: its type, its name on the project, its title and its text.
/// </summary>
/// <param name="PageId">The page on the project. Every piece written from it is saved under it.</param>
/// <param name="Type">The page's content type as stored: pillar, blog or tool.</param>
/// <param name="Name">The page's name: the keyword for a pillar or a blog, the product for a tool page.
/// A piece written from the page carries the same name, so a page has one piece of each type.</param>
/// <param name="Title">The page's own title, or its name when the stored body carries none.</param>
/// <param name="Text">Every heading and paragraph of the page, in order, as text.</param>
public sealed record GccRepurposeSource(Guid PageId, string Type, string Name, string Title, string Text);

/// <summary>
/// The page a Generate writes its short pieces from: the project's Pillar, or its Blog when it has no
/// Pillar.
/// </summary>
/// <param name="Type">The page's type: "pillar" or "blog".</param>
/// <param name="WrittenByThisRun">True when the run was asked for that page too: the pieces wait for
/// it and are written from what this run wrote, and are refused with it when it is refused. False when
/// they are written from the page the project already has.</param>
public sealed record GccGenerateSource(string Type, bool WrittenByThisRun)
{
    /// <summary>The page as the form's "Content type" list names it: "Pillar", "Blog".</summary>
    public string Label => Type == "pillar" ? "Pillar" : "Blog";
}

/// <summary>
/// A short piece (an email, a social post, an ad) is written from one page of the project and saved
/// under that page, never from the Brief alone.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two runs write one, with one writer</b> (Jeff, 2026-10-12: short content types "ARE DERIVED OR
/// REPURPOSED FROM EXISTING CONTENT"; 2026-10-13: "Add my Email, Social, and Ads, back to generate").
/// A Generate that is asked for one writes it from the project's Pillar, the one that run wrote or the
/// one already saved (<see cref="GenerateSourceOf"/>). A Repurpose writes it again from one
/// page, a pillar, a blog or a tool page, without running Generate. Until 2026-10-12 the three were
/// Generate types written from the Brief alone, with no page in front of the writer; from then until
/// 2026-10-13 a Generate asked for one was refused.
/// </para>
/// <para>
/// <b>What the writer is handed.</b> The page's own text, whole, and from the Brief what a short piece
/// was already handed: the reader, the voice and the problem. No research and no search: the page
/// already used the research, and a piece that went back to it could say what the page does not.
/// </para>
/// <para>
/// <b>One check, no second attempt.</b> A figure in the piece must be in the page it was written from
/// or in the Brief (<see cref="FigureRefusal"/>). A piece that states another is refused and nothing is
/// saved for it; the other pieces of the run are kept.
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
    public const string FromWhat = "a pillar, blog or tool page";

    /// <summary>The types of page a Generate writes its short pieces from, the first the project has.</summary>
    private static readonly string[] GenerateSourceTypes = ["pillar", "blog"];

    /// <summary>Whether a requested type is a short piece: written from a page, never from the Brief alone.</summary>
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
    /// The page a Generate writes its short pieces from, or why it has none. Null and null when the
    /// run was asked for no short piece.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The project's Pillar: the one this run writes when "Pillar" is among the types, otherwise the
    /// one the project already has. A project with no Pillar and none asked for: its
    /// Blog, by the same rule. Neither, and neither asked for: refused, and the caller asks this before
    /// a run exists and before anything is deleted.
    /// </para>
    /// <para>
    /// One page, decided once. A Pillar that is asked for and then refused takes its pieces with it
    /// (<see cref="SourceNotWrittenRefusal"/>); they are not written from the Blog in its place, which
    /// would save under one page what the operator asked to be written from another.
    /// </para>
    /// </remarks>
    /// <param name="requested">Every type the run was asked for, pages and pieces.</param>
    /// <param name="onProject">The project's pages and pieces as they are before the run deletes anything.</param>
    public static (GccGenerateSource? Source, string? Refusal) GenerateSourceOf(
        IReadOnlyList<string> requested, IReadOnlyList<GccArtifactDto> onProject)
    {
        var shortOnes = requested.Where(IsShortType).ToList();
        if (shortOnes.Count == 0) return (null, null);

        foreach (var type in GenerateSourceTypes)
        {
            if (requested.Any(asked => GccGenerationCoordinator.LettersOf(asked) == type))
                return (new GccGenerateSource(type, WrittenByThisRun: true), null);
            if (PageOf(type, onProject) is not null)
                return (new GccGenerateSource(type, WrittenByThisRun: false), null);
        }

        return (null, $"Refused: '{string.Join("', '", shortOnes)}' "
            + (shortOnes.Count == 1 ? "is" : "are")
            + " written from this project's Pillar, or from its Blog when it has no Pillar, and it has neither. "
            + "Tick \"Pillar\" as well, or generate one first. Nothing was generated.");
    }

    /// <summary>
    /// The project's page of one type, the one a short piece is written from: a page of the project's
    /// own, not a piece under one. The newest when an older project holds more than one.
    /// </summary>
    public static GccArtifactDto? PageOf(string type, IReadOnlyList<GccArtifactDto> onProject) =>
        onProject
            .Where(page => page.ParentArtifactId is null && GccGenerationCoordinator.LettersOf(page.Type) == type)
            .OrderByDescending(page => page.LatestVersionAtUtc ?? page.CreatedAtUtc)
            .FirstOrDefault();

    /// <summary>
    /// What a short piece is refused with when the run was asked for the page it is written from and
    /// did not write that page.
    /// </summary>
    /// <param name="requestedType">The piece's type as asked for.</param>
    public static string SourceNotWrittenRefusal(GccGenerateSource source, string requestedType) =>
        $"Refused: the {PieceOf(requestedType)} is written from this project's {source.Label}, which this run did not write. "
        + "Nothing was written for it.";

    /// <summary>
    /// What a short piece is refused with when the page it was to be written from is not on the project
    /// when the run reaches it.
    /// </summary>
    public static string SourceGoneRefusal(GccGenerateSource source, string requestedType) =>
        $"Refused: the {PieceOf(requestedType)} is written from this project's {source.Label}, and the project no longer has one. "
        + "Nothing was written for it.";

    /// <summary>What a requested short type is called in a refusal: "email", "social post", "ad".</summary>
    public static string PieceOf(string requestedType) =>
        IsEmail(requestedType) ? "email" : PieceName(PlatformOf(requestedType));

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
    /// The page as the writer of a short piece reads it, or why it cannot be written from. Nothing is
    /// searched and nothing is paid for before this is known.
    /// </summary>
    /// <param name="page">The page on the project.</param>
    /// <param name="bodyJson">The page's one version, as stored.</param>
    /// <remarks>
    /// No page has to be approved first (Jeff, 2026-10-18: "remove approve content bs", then "Email and
    /// social just work"). Until then a Repurpose refused a page nobody had approved, and approving was
    /// the only thing the form's Approve button did that anything read.
    /// </remarks>
    public static (GccRepurposeSource? Source, string? Refusal) SourceOf(GccArtifactDto page, string? bodyJson)
    {
        if (page.ParentArtifactId is not null)
            return (null, $"Refused: Repurpose writes from {FromWhat}, and this is a piece written from one.");

        if (!SourceTypes.Contains(GccGenerationCoordinator.LettersOf(page.Type)))
            return (null, $"Refused: Repurpose writes from {FromWhat}; this page is a {page.Type.Trim()}.");

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
        sb.AppendLine($"A {source.Type} page of this publisher's: \"{source.Title}\".");
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
    /// What a piece is refused with when it states a figure that neither the page it was written from nor
    /// the Brief states, or null. What a figure is, and is not, is <see cref="GccFigureGrammar"/>, as it is
    /// for a page.
    /// </summary>
    /// <remarks>
    /// The Brief counts as a page's does: the writer is handed its reader, voice, problem and pitch, and a
    /// figure the operator typed there is his own statement. Until 2026-10-19 only the page was read, and
    /// the 16:44 UTC run of that day refused its social post for "in under 60 seconds", which the pitch
    /// says and the Pillar did not.
    /// </remarks>
    /// <param name="piece">What was written: "email", "LinkedIn post".</param>
    /// <param name="written">Every part of the piece the reader sees, a part to an entry.</param>
    /// <param name="operatorsWords">The Brief, the topic and the notes as text, read as for a page.</param>
    public static string? FigureRefusal(
        string piece, IReadOnlyList<string?> written, GccRepurposeSource source, string operatorsWords)
    {
        var known = GccFigureGrammar.NumbersIn(
            string.Join(Environment.NewLine, source.Title, source.Text, operatorsWords));
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
                + $" found in neither the page it was written from nor the Brief: {string.Join(", ", unsupported.Distinct(StringComparer.Ordinal))}. "
                + "Nothing was saved for it.";
    }
}
