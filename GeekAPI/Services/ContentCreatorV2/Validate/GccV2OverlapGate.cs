using System.Text;
using System.Text.RegularExpressions;
using GeekAPI.Services.Workflow.Domain.Entities;

namespace GeekAPI.Services.ContentCreatorV2.Validate;

/// <summary>One drafted section, reduced to what OverlapGate needs to compare it against every
/// other section. <see cref="PlainText"/> is already-flattened body text — never markup.</summary>
public sealed record OverlapSectionInput(string SectionKey, string Heading, string? Job, string PlainText);

/// <summary>
/// A named, paraphrase-level duplicate: two different H2s both restate the same practitioner
/// pain and the same fix — v1's core quality failure (see design plan §"v1 quality failures you
/// can see", item 1). <see cref="SectionKeyB"/> is always the later section — REPAIR targets it,
/// keeping <see cref="SectionKeyA"/> (the earlier, "owning" section) untouched.
/// </summary>
public sealed record OverlapHit(
    string HeadingA,
    string HeadingB,
    string SharedClaim,
    string SectionKeyA,
    string SectionKeyB,
    string RepairHint);

/// <summary>
/// v2-only heuristic duplicate-claim detector. No LLM call — cheap enough to run on every
/// VALIDATE pass. Flags two sections as overlapping when their openings share both a pain
/// "category" and a fix "category" (paraphrase-tolerant — categories are matched by stem/keyword,
/// not exact words) AND their opening content-word sets are highly similar (Jaccard). Both
/// conditions are required so two sections that merely share domain vocabulary (e.g. "AI",
/// "implementation") without actually restating the same claim are not falsely flagged.
/// </summary>
public static class GccV2OverlapGate
{
    /// <summary>How many leading words of a section's plain text are considered its "opening" —
    /// where v1's shared PROBLEM-FIRST OPENING requirement means a restated pain/fix would land.</summary>
    private const int OpeningWordWindow = 70;

    /// <summary>Jaccard similarity of opening content words at/above this triggers a hit, provided
    /// at least one pain category and one fix category are also shared.</summary>
    private const double JaccardThreshold = 0.32;

    private static readonly Regex WordPattern = new(@"[a-zA-Z][a-zA-Z'-]{2,}", RegexOptions.Compiled);

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "that", "this", "these", "those", "with", "from", "into", "than",
        "then", "when", "while", "which", "what", "where", "why", "how", "also", "still", "even",
        "just", "only", "both", "either", "neither", "does", "did", "not", "can", "will", "would",
        "could", "should", "their", "they", "them", "you", "your", "our", "more", "most", "some",
        "any", "all", "each", "every", "over", "under", "about", "such", "have", "has", "had",
        "was", "were", "are", "its", "it's", "per", "via", "using", "use", "used", "because",
        "since", "before", "after", "between", "without", "within", "who", "whom", "there", "here",
        "being", "been", "off", "out", "own", "same", "too", "very", "once", "here's",
    };

    private static readonly (string Label, string[] Stems)[] PainCategories =
    [
        ("cost", ["cost", "costly", "expensive", "budget", "spend"]),
        ("wasted-time", ["waste", "wasted", "wastes", "hour", "hours", "week", "weeks", "delay", "delayed", "slow"]),
        ("errors", ["error", "errors", "mistake", "mistakes", "inaccura"]),
        ("risk", ["risk", "risky", "complian", "penalt", "exposure"]),
        ("manual-effort", ["manual", "manually", "tedious", "repetit", "burden", "overwhelm", "struggl", "backlog", "bottleneck"]),
    ];

    private static readonly (string Label, string[] Stems)[] FixCategories =
    [
        ("automation", ["automat", "streamlin", "workflow"]),
        ("ai-solution", ["solution", "solves", "resolv", "eliminat", "reduc", "implement", "integrat", "ai-assist", "aiassist", "intelligent"]),
    ];

    /// <summary>Convenience for callers holding a Workflow <see cref="Section"/> tree rather than
    /// already-flattened plain text (WRITE's persisted sections, review-adapter bodies, etc.).</summary>
    public static string FlattenPlainText(Section section)
    {
        var sb = new StringBuilder();
        AppendSectionText(section, sb);
        return sb.ToString();
    }

    private static void AppendSectionText(Section section, StringBuilder sb)
    {
        foreach (var paragraph in section.Paragraphs)
        {
            AppendParagraphText(paragraph, sb);
        }

        foreach (var child in section.Children)
        {
            AppendSectionText(child, sb);
        }
    }

    private static void AppendParagraphText(Paragraph paragraph, StringBuilder sb)
    {
        switch (paragraph)
        {
            case TextParagraph text:
                foreach (var run in text.Runs)
                {
                    sb.Append(run.Text).Append(' ');
                }
                break;
            case ListParagraph list:
                foreach (var item in list.Items)
                {
                    foreach (var run in item)
                    {
                        sb.Append(run.Text).Append(' ');
                    }
                }
                break;
        }
    }

    /// <summary>Pure, deterministic — no I/O, no LLM. Compares every pair of sections once.</summary>
    public static IReadOnlyList<OverlapHit> Detect(IReadOnlyList<OverlapSectionInput> sections)
    {
        var hits = new List<OverlapHit>();
        var fingerprints = sections.Select(BuildFingerprint).ToList();

        for (var i = 0; i < sections.Count; i++)
        {
            for (var j = i + 1; j < sections.Count; j++)
            {
                var hit = CompareFingerprints(sections[i], fingerprints[i], sections[j], fingerprints[j]);
                if (hit is not null)
                {
                    hits.Add(hit);
                }
            }
        }

        return hits;
    }

    /// <summary>
    /// Deterministic fixture: two H2s that restate the same manual-process pain and the same
    /// automation fix in different words — the exact v1 failure mode. Exercised by
    /// <c>Detect(BuildOverlappingFixture())</c> in unit tests (and by WRITE's LLM-failure stub
    /// fallback, which intentionally reuses this same boilerplate — see
    /// <c>GccV2WriteService.BuildFallbackStubSection</c>) to prove OverlapGate returns exactly one
    /// named pair instead of silently passing.
    /// </summary>
    public static IReadOnlyList<OverlapSectionInput> BuildOverlappingFixture() =>
    [
        new OverlapSectionInput(
            "intro",
            "Introduction",
            "problem",
            "Manual invoice matching wastes hours of staff time every single week and introduces costly, " +
            "repetitive errors that finance teams then have to track down by hand. The solution is automating " +
            "the matching workflow with an AI-assisted system that eliminates that repetitive manual review."),
        new OverlapSectionInput(
            "body-1",
            "Section 1",
            "advance",
            "Finance teams still lose hours of staff time every week to manual invoice matching, and the " +
            "errors that process introduces are costly to track down. Automating the workflow with an " +
            "AI-assisted system is the fix, eliminating that same repetitive manual review."),
    ];

    private sealed record Fingerprint(HashSet<string> ContentWords, HashSet<string> PainCategoriesHit, HashSet<string> FixCategoriesHit);

    private static Fingerprint BuildFingerprint(OverlapSectionInput section)
    {
        var opening = string.Join(' ', (section.PlainText ?? "").Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries).Take(OpeningWordWindow));
        var lower = opening.ToLowerInvariant();

        var words = WordPattern.Matches(lower)
            .Select(m => m.Value)
            .Where(w => w.Length >= 4 && !StopWords.Contains(w))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var painHit = PainCategories.Where(c => c.Stems.Any(lower.Contains)).Select(c => c.Label).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fixHit = FixCategories.Where(c => c.Stems.Any(lower.Contains)).Select(c => c.Label).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new Fingerprint(words, painHit, fixHit);
    }

    private static OverlapHit? CompareFingerprints(
        OverlapSectionInput a, Fingerprint fpA,
        OverlapSectionInput b, Fingerprint fpB)
    {
        if (fpA.ContentWords.Count == 0 || fpB.ContentWords.Count == 0) return null;
        if (fpA.PainCategoriesHit.Count == 0 || fpB.PainCategoriesHit.Count == 0) return null;
        if (fpA.FixCategoriesHit.Count == 0 || fpB.FixCategoriesHit.Count == 0) return null;

        var sharedPain = fpA.PainCategoriesHit.Intersect(fpB.PainCategoriesHit, StringComparer.OrdinalIgnoreCase).ToList();
        var sharedFix = fpA.FixCategoriesHit.Intersect(fpB.FixCategoriesHit, StringComparer.OrdinalIgnoreCase).ToList();
        if (sharedPain.Count == 0 || sharedFix.Count == 0) return null;

        var jaccard = JaccardSimilarity(fpA.ContentWords, fpB.ContentWords);
        if (jaccard < JaccardThreshold) return null;

        var sharedClaim = $"same pain: {string.Join("/", sharedPain)}; same fix: {string.Join("/", sharedFix)}";
        var job = string.IsNullOrWhiteSpace(b.Job) ? "its assigned focus" : b.Job;

        return new OverlapHit(
            HeadingA: a.Heading,
            HeadingB: b.Heading,
            SharedClaim: sharedClaim,
            SectionKeyA: a.SectionKey,
            SectionKeyB: b.SectionKey,
            RepairHint: $"Rewrite H2 \"{b.Heading}\" to cover only its assigned job (\"{job}\"); " +
                        $"remove restatement of \"{a.Heading}\"'s problem/solution.");
    }

    private static double JaccardSimilarity(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 && b.Count == 0) return 0;
        var intersection = a.Count(b.Contains);
        var union = a.Count + b.Count - intersection;
        return union == 0 ? 0 : (double)intersection / union;
    }
}
