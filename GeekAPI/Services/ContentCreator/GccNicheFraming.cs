using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// The operator's own framing of a niche: the problem, how it is failed, and what automation answers it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> The brief had no problem field. On a <c>problem_solution</c> angle the
/// opening slot asks for <i>"the problem this reader has with {keyword} today, what it costs them, and
/// where {product} breaks it"</i> (<c>ToolPrompts.cs</c>) with <c>Guidance: null</c> — so the writer
/// invented all three. Jeff researches exactly those three things per niche, by hand, and the answer
/// was being read and then discarded.
/// </para>
/// <para>
/// <b>Framing, not evidence.</b> This is what the page should <i>argue</i>. It is not a citation source
/// and never enters the corpus: quotes and vendor claims still come from retrieved, verified crawl text.
/// A page may say the problem is late invoices because the operator says so; it may not attribute a
/// capability to a product on this basis.
/// </para>
/// <para>
/// <b>Affiliate economics are deliberately absent.</b> Jeff, 2026-10-02: <i>"Partner program text has
/// no place in my output."</i> There is no field for a commission rate or payout cap, and that is the
/// enforcement — <c>BriefJson</c> is prompt input (<c>ExtractBriefFields</c> reads it,
/// <c>BuildBriefAndResearchBlock</c> feeds it to every long-form prompt), so a figure stored there sits
/// one field addition away from a page. Absence cannot regress; a test can.
/// </para>
/// </remarks>
/// <param name="CoreProblem">The problem the reader has today, in the operator's words.</param>
/// <param name="PainPoints">Where it goes wrong, one entry per failure — the body's substance. Each
/// entry may be a full paragraph; the operator's research states a failure as a lead plus its
/// explanation, and a fragment is not something a writer can argue from.</param>
/// <param name="AutomationToPitch">What removes the problem, and the shape of the offer.</param>
public sealed record GccNicheFraming(
    string CoreProblem,
    IReadOnlyList<string> PainPoints,
    string AutomationToPitch)
{
    /// <summary>
    /// One retrieval question per failure (Geek-Crawler-Rag plans/retrieval-from-the-brief.md,
    /// 2026-10-08): the reader's problem, the vendor's solution in the vendor's own vocabulary, and
    /// a few of the vendor's terms. Retrieval input, never guidance and never quoted: the solution
    /// goes to the index's meaning half and the terms to its keyword half. Per tool, rows are added
    /// to the category's, like pain points. Empty when the brief carries none.
    /// </summary>
    public IReadOnlyList<GccEvidenceRow> Evidence { get; init; } = [];

    /// <summary>True when any part carries something. An all-blank set is not framing.</summary>
    public bool HasAny =>
        !string.IsNullOrWhiteSpace(CoreProblem)
        || PainPoints.Count > 0
        || !string.IsNullOrWhiteSpace(AutomationToPitch)
        || Evidence.Count > 0;

    /// <summary>
    /// The three parts as section guidance, or null when there is nothing to say.
    /// </summary>
    /// <remarks>
    /// Labelled as the operator's framing on purpose. Without the label the writer cannot tell this
    /// apart from retrieved evidence and may attribute it to a source, which is the one way this
    /// becomes a citation problem. The instruction to argue rather than cite is part of the payload,
    /// not a separate prompt line that could be added to one call site and missed on another.
    /// </remarks>
    public string? ToGuidance()
    {
        if (!HasAny) return null;

        var sb = new StringBuilder();
        sb.Append("THE OPERATOR'S OWN FRAMING OF THIS PROBLEM -- argue from it, never cite it. ");
        sb.Append("It is the publisher's position, not retrieved evidence: do not attribute any of it ");
        sb.Append("to a source, and do not present it as something a vendor said.");

        if (!string.IsNullOrWhiteSpace(CoreProblem))
        {
            sb.AppendLine();
            sb.Append("The problem: ").Append(CoreProblem.Trim());
        }

        if (PainPoints.Count > 0)
        {
            // One per line, numbered. These were joined on " | ", which was fine for three terse points
            // and unreadable for the fourteen a real category query returns -- nine tabular failures plus
            // five consequence paragraphs, pipe-joined into a single wall of text the model has to parse
            // before it can use any of it.
            sb.AppendLine();
            sb.Append("Where it goes wrong. Cover these in your own prose, never as a list, and never ");
            sb.Append("verbatim:");
            for (var i = 0; i < PainPoints.Count; i++)
            {
                sb.AppendLine();
                sb.Append(i + 1).Append(". ").Append(PainPoints[i]);
            }
        }

        if (!string.IsNullOrWhiteSpace(AutomationToPitch))
        {
            sb.AppendLine();
            sb.Append("What answers it: ").Append(AutomationToPitch.Trim());
        }

        return sb.ToString();
    }

    /// <summary>
    /// The failures alone, for the section whose job is what is going wrong today. Null when the
    /// operator listed none.
    /// </summary>
    /// <remarks>
    /// The pillar and the blog each have a slot for "what is actually going wrong in this work today",
    /// and until 2026-10-06 it carried no guidance, so the writer filled it from retrieved vendor
    /// prose and competitor coverage -- a methodology of its own. Jeff, 2026-10-06: "It is again
    /// inventing its own Methodology versus using mine?" This section's substance is the operator's
    /// failures, every one of them and no other.
    /// </remarks>
    public string? FailuresGuidance()
    {
        if (PainPoints.Count == 0) return null;

        var sb = new StringBuilder();
        sb.Append("THE OPERATOR'S OWN FRAMING -- argue from it, never cite it; it is the publisher's position, ");
        sb.Append("not retrieved evidence, so attribute none of it to a source. ");
        sb.Append("This section's substance is where the work goes wrong, in the operator's own analysis. Cover ");
        sb.Append("every one of these, in your own prose, never as a list and never verbatim, and add no failure ");
        sb.Append("the operator did not name:");
        for (var i = 0; i < PainPoints.Count; i++)
        {
            sb.AppendLine();
            sb.Append(i + 1).Append(". ").Append(PainPoints[i]);
        }

        return sb.ToString();
    }

    /// <summary>
    /// The automation alone, for the sections about how the approach works, what makes it hold up and
    /// what rolling it out involves. Null when the operator described none.
    /// </summary>
    /// <remarks>
    /// "How the approach works end to end: the mechanics" was answered, until 2026-10-06, with whatever
    /// approach the retrieved passages described -- a vendor's, or the aggregate of the pages already
    /// ranking. The approach is the operator's; the writer supplies the prose and the evidence for it.
    /// </remarks>
    public string? ApproachGuidance()
    {
        if (string.IsNullOrWhiteSpace(AutomationToPitch)) return null;

        return "THE OPERATOR'S OWN FRAMING -- argue from it, never cite it; it is the publisher's position, "
            + "not retrieved evidence, so attribute none of it to a source. The approach this section describes "
            + "is the operator's, not a generic one and not a vendor's: " + AutomationToPitch.Trim()
            + " Describe its mechanics, its sequence and its decisions. The partner tools appear where they do "
            + "this work, as the evidence shows them doing it.";
    }

    /// <summary>A one-line pointer for the later sections, so the approach is stated once and held to throughout.</summary>
    public string? ApproachPointer() =>
        string.IsNullOrWhiteSpace(AutomationToPitch)
            ? null
            : "Within the operator's own approach, stated in the section before this one -- never a generic "
              + "or a vendor's alternative to it.";
}

/// <summary>
/// Reads <see cref="GccNicheFraming"/> out of a create's <c>BriefJson</c>.
/// </summary>
/// <remarks>
/// <para>
/// Wire shape, under <c>nicheFraming</c>:
/// <code>
/// {
///   "nicheFraming": {
///     "taxonomyPath": "Accounting &gt; Cash Flow Forecasting &gt; Accounts Receivable",
///     "coreProblem": "...",
///     "painPoints": "one failure per paragraph, blank line between",
///     "automationToPitch": "...",
///     "perTool": { "bill.com": { "coreProblem": "...", "painPoints": "...", "automationToPitch": "..." } }
///   }
/// }
/// </code>
/// </para>
/// <para>
/// <b>One category set, with optional per-tool overrides.</b> Jeff's own research states the framing
/// once for the category as often as it states it per tool (Perplexity repeated all three per tool;
/// Claude desktop stated them once and listed five tools beneath). Requiring five sets would force
/// invented per-tool distinctions, so a tool with no entry inherits the category's.
/// </para>
/// <para>
/// <b>Keyed by host, never by a typed name.</b> <c>GccPartnerToolSlices</c> buckets evidence by
/// <see cref="GccRequiredToolMentions.HostKeyOf"/> and names products through
/// <c>GccRequiredToolMentions.AnchorLookup</c>. A key of "Bill" would not match <c>bill.com</c>'s
/// slice, and the framing would then reach the wrong page or none at all — silently, because a null
/// <c>Guidance</c> is indistinguishable from no framing. The host is the one identity both sides agree
/// on.
/// </para>
/// <para>
/// Every failure is silent and returns null, per this codebase's no-exceptions rule: a brief that will
/// not parse means no framing, which the prompts already handle.
/// </para>
/// </remarks>
public static class GccNicheFramingReader
{
    private const string Root = "nicheFraming";

    /// <summary>The category-level framing, or null when the brief carries none.</summary>
    public static GccNicheFraming? ForCategory(string? briefJson)
    {
        var root = ReadRoot(briefJson);
        if (root is null) return null;

        var framing = ReadFraming(root.Value);
        return framing is not null && framing.HasAny ? framing : null;
    }

    /// <summary>
    /// The framing for one product: its own override when the operator wrote one, otherwise the
    /// category's. Null when neither exists.
    /// </summary>
    /// <param name="partnerUrls">
    /// The project's declared partner URLs, which is what resolves a product name back to the host the
    /// override is keyed by. Without them only the category framing can be found.
    /// </param>
    public static GccNicheFraming? ForProduct(
        string? briefJson,
        IReadOnlyList<string>? partnerUrls,
        string? productName)
    {
        var host = HostForProduct(briefJson, partnerUrls, productName);
        return host.Length == 0 ? ForCategory(briefJson) : ForHost(briefJson, host);
    }

    /// <summary>
    /// The framing for one partner host (the <c>perTool</c> key, e.g. <c>tipalti.com</c>): its own
    /// override merged over the category's when the operator wrote one, otherwise the category's.
    /// Null when neither exists. Retrieval resolves partners by host, not by product name, so this
    /// is the entry it uses (plans/retrieval-from-the-brief.md P5).
    /// </summary>
    public static GccNicheFraming? ForHost(string? briefJson, string host)
    {
        var category = ForCategory(briefJson);
        host = (host ?? string.Empty).Trim();
        if (host.Length == 0) return category;

        var root = ReadRoot(briefJson);
        if (root is null) return category;

        if (!TryGetPropertyIgnoreCase(root.Value, "perTool", out var perTool)
            || perTool.ValueKind != JsonValueKind.Object)
        {
            return category;
        }

        foreach (var entry in perTool.EnumerateObject())
        {
            if (!string.Equals(GccRequiredToolMentions.HostKeyOf(Absolute(entry.Name)), host, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(entry.Name.Trim(), host, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (entry.Value.ValueKind != JsonValueKind.Object) continue;

            var own = ReadFraming(entry.Value);
            // An override that was opened and left blank is not an override. Inheriting the category
            // beats handing the writer an empty frame it would have to invent around anyway.
            if (own is not null && own.HasAny) return Merge(own, category);
        }

        return category;
    }

    /// <summary>
    /// The taxonomy path the operator researched, e.g.
    /// <c>["Accounting","Cash Flow Forecasting","Accounts Receivable"]</c>. Accepts a string split on
    /// <c>&gt;</c>, <c>-&gt;</c> or <c>›</c>, or an array.
    /// </summary>
    /// <remarks>
    /// Its first level is a department: <c>Accounting</c> is a member of <c>Departments.Slugs</c>, which
    /// is why the research template lines up with the one axis the URL scheme already has. The caller
    /// decides whether to trust it as a department — this only reads what was written.
    /// </remarks>
    public static IReadOnlyList<string> TaxonomyPath(string? briefJson)
    {
        var root = ReadRoot(briefJson);
        if (root is null) return [];

        if (!TryGetPropertyIgnoreCase(root.Value, "taxonomyPath", out var prop)) return [];

        var parts = new List<string>();
        if (prop.ValueKind == JsonValueKind.String)
        {
            var raw = prop.GetString() ?? string.Empty;
            foreach (var piece in raw.Replace("->", ">").Replace('›', '>').Split('>'))
            {
                var trimmed = piece.Trim();
                if (trimmed.Length > 0) parts.Add(trimmed);
            }
        }
        else if (prop.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in prop.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.String) continue;
                var trimmed = (el.GetString() ?? string.Empty).Trim();
                if (trimmed.Length > 0) parts.Add(trimmed);
            }
        }

        return parts;
    }

    /// <summary>
    /// The operator's questions for the appointment: the closing asks the reader to book and to answer
    /// these when booking. Empty when the brief carries none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Category-level, with no per-tool override, and that is a property of the data rather than a
    /// simplification.</b> Jeff's set for AP approval workflows asks how many invoices need an approval,
    /// who approves spending, and whether the audit trail can answer "who approved this payment and
    /// why" — every question is about the reader's own process and none names a product. There is
    /// nothing for a partner to override. So this reads from the framing root only, like
    /// <see cref="TaxonomyPath"/>, and is deliberately not a field on <see cref="GccNicheFraming"/>,
    /// which <see cref="ForProduct"/> resolves per product.
    /// </para>
    /// <para>
    /// <b>One question per line — not the paragraph rule <see cref="ReadParagraphs"/> uses.</b> A failure
    /// mode is a paragraph, which is why pain points split on blank lines; a discovery question is a
    /// single line, and eight consecutive ones read through that rule collapse into one run-on entry.
    /// The two formats differ, so the two readers differ.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> DiagnosisQuestions(string? briefJson)
    {
        var root = ReadRoot(briefJson);
        return root is null ? [] : ReadQuestionLines(root.Value, "diagnosisQuestions");
    }

    /// <summary>
    /// A per-tool override laid over the category set, <b>field by field</b>.
    /// </summary>
    /// <remarks>
    /// Per field, not per set. The override used to replace the category wholesale, so filling only a
    /// tool's Core Problem silently discarded the category's Pain Points and Automation — the operator
    /// states one thing differently for one tool and loses the two they meant to keep. Jeff asked
    /// whether the backend combines these (2026-10-03); it did not, and it should.
    ///
    /// <para>
    /// <b>The two single-statement fields replace; the list field appends.</b> Two core problems on one
    /// page is incoherent, so the tool's wins. Pain points are different in kind: the category's are by
    /// definition true of every tool in the niche, so a tool-specific failure is <i>additional</i> rather
    /// than a correction.
    /// </para>
    /// <para>
    /// The research is what settled it. One category query returned fourteen pain points — nine in a
    /// table, five as consequence paragraphs — every one of them true of all five AP tools. Replacing
    /// that set with a single tool-specific line would discard almost everything the operator gathered.
    /// </para>
    /// <para>
    /// Category first, then the tool's: the shared problem is established before the slice that is
    /// particular to this product. Duplicates are dropped, since an operator restating a shared failure
    /// inside an override means to emphasise it, not to have it argued twice.
    /// </para>
    /// </remarks>
    private static GccNicheFraming Merge(GccNicheFraming own, GccNicheFraming? category)
    {
        var pains = new List<string>(category?.PainPoints ?? []);
        foreach (var pain in own.PainPoints)
        {
            if (!pains.Contains(pain, StringComparer.OrdinalIgnoreCase)) pains.Add(pain);
        }

        // Rows add, like pain points: the category's failures are true of every tool in the niche,
        // and the tool's rows are the slice it owns. A row restating a category problem replaces it,
        // because the tool's solution to that problem is the one its page should search for.
        var rows = new List<GccEvidenceRow>(category?.Evidence ?? []);
        foreach (var row in own.Evidence)
        {
            var at = rows.FindIndex(r => string.Equals(r.Problem, row.Problem, StringComparison.OrdinalIgnoreCase));
            if (at >= 0) rows[at] = row;
            else rows.Add(row);
        }

        return new GccNicheFraming(
            own.CoreProblem.Length > 0 ? own.CoreProblem : category?.CoreProblem ?? string.Empty,
            pains,
            own.AutomationToPitch.Length > 0
                ? own.AutomationToPitch
                : category?.AutomationToPitch ?? string.Empty)
        {
            Evidence = rows,
        };
    }

    /// <summary>
    /// The host whose override applies to <paramref name="productName"/>, or empty when the name
    /// matches no declared partner.
    /// </summary>
    private static string HostForProduct(
        string? briefJson,
        IReadOnlyList<string>? partnerUrls,
        string? productName)
    {
        var wanted = (productName ?? string.Empty).Trim();
        if (wanted.Length == 0 || partnerUrls is null || partnerUrls.Count == 0) return string.Empty;

        // The same lookup the fan-out names its pages from, so the two cannot disagree about which
        // host a product is.
        var names = GccRequiredToolMentions.AnchorLookup(briefJson, partnerUrls);
        foreach (var pair in names)
        {
            if (string.Equals(pair.Value.Trim(), wanted, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Key;
            }
        }

        // A product named as its host ("bill.com") rather than as its product name still resolves.
        var direct = GccRequiredToolMentions.HostKeyOf(Absolute(wanted));
        return names.ContainsKey(direct) ? direct : string.Empty;
    }

    /// <summary>A bare host becomes a URL so <c>HostKeyOf</c>, which requires an absolute URI, can read it.</summary>
    private static string Absolute(string value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0) return string.Empty;
        return trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? trimmed
                : $"https://{trimmed}";
    }

    private static JsonElement? ReadRoot(string? briefJson)
    {
        if (string.IsNullOrWhiteSpace(briefJson)) return null;

        try
        {
            using var doc = JsonDocument.Parse(briefJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!TryGetPropertyIgnoreCase(doc.RootElement, Root, out var root)) return null;
            if (root.ValueKind != JsonValueKind.Object) return null;
            // Cloned because the JsonDocument is disposed on the way out of this method and an
            // undisposed element would read freed memory.
            return root.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static GccNicheFraming? ReadFraming(JsonElement obj)
    {
        var core = ReadString(obj, "coreProblem");
        var automation = ReadString(obj, "automationToPitch");
        var pains = ReadParagraphs(obj, "painPoints");
        var evidence = ReadEvidence(obj);

        // Since 2026-10-08 the form enters each failure once, as a row, and derives painPoints from
        // the rows' Problem column on every change. A brief written by hand or by an older client
        // may carry rows and no paragraphs; the rows' problems are the failures then.
        if (pains.Count == 0 && evidence.Count > 0)
        {
            pains = [.. evidence.Select(r => r.Problem).Where(p => p.Length > 0)];
        }

        if (core.Length == 0 && automation.Length == 0 && pains.Count == 0 && evidence.Count == 0) return null;
        return new GccNicheFraming(core, pains, automation) { Evidence = evidence };
    }

    private static string ReadString(JsonElement obj, string name) =>
        TryGetPropertyIgnoreCase(obj, name, out var prop) && prop.ValueKind == JsonValueKind.String
            ? (prop.GetString() ?? string.Empty).Trim()
            : string.Empty;

    /// <summary>
    /// The <c>evidence</c> array: objects with <c>problem</c>, <c>solution</c> and <c>terms</c>
    /// (an array of strings, or one comma-separated string -- the form stores an array; a pasted
    /// research answer may carry the string). A row with every part blank is not a row.
    /// </summary>
    private static IReadOnlyList<GccEvidenceRow> ReadEvidence(JsonElement obj)
    {
        if (!TryGetPropertyIgnoreCase(obj, "evidence", out var prop) || prop.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var rows = new List<GccEvidenceRow>();
        foreach (var item in prop.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var problem = ReadString(item, "problem");
            var solution = ReadString(item, "solution");
            var terms = new List<string>();
            if (TryGetPropertyIgnoreCase(item, "terms", out var termsProp))
            {
                if (termsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var t in termsProp.EnumerateArray())
                    {
                        if (t.ValueKind == JsonValueKind.String) terms.Add((t.GetString() ?? string.Empty).Trim());
                    }
                }
                else if (termsProp.ValueKind == JsonValueKind.String)
                {
                    terms.AddRange((termsProp.GetString() ?? string.Empty).Split(',').Select(t => t.Trim()));
                }
            }
            terms.RemoveAll(string.IsNullOrWhiteSpace);
            if (problem.Length == 0 && solution.Length == 0 && terms.Count == 0) continue;
            rows.Add(new GccEvidenceRow(problem, solution, terms));
        }
        return rows;
    }

    /// <summary>
    /// One item per <b>line</b>, or an array. The counterpart to <see cref="ReadParagraphs"/>, for data
    /// whose unit is a line rather than a paragraph.
    /// </summary>
    /// <remarks>
    /// Blank lines separate nothing here, they are just skipped: an operator pasting a researched set
    /// often leaves one between questions, and reading that as a grouping would invent structure the
    /// closing has no use for.
    /// </remarks>
    private static IReadOnlyList<string> ReadQuestionLines(JsonElement obj, string name)
    {
        if (!TryGetPropertyIgnoreCase(obj, name, out var prop)) return [];

        var lines = new List<string>();
        if (prop.ValueKind == JsonValueKind.String)
        {
            var raw = (prop.GetString() ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
            foreach (var line in raw.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0) lines.Add(trimmed);
            }
        }
        else if (prop.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in prop.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.String) continue;
                var trimmed = (el.GetString() ?? string.Empty).Trim();
                if (trimmed.Length > 0) lines.Add(trimmed);
            }
        }

        return lines;
    }

    /// <summary>
    /// One item per <b>paragraph</b> — blank-line separated — or an array.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Paragraphs, not lines. Jeff, 2026-10-03: <i>"the data I am inputting is a paragraph"</i> — and it
    /// is: the research states each failure as a lead plus a paragraph explaining it, so splitting on
    /// every newline turned one failure into four fragments, each too thin for the writer to argue from.
    /// </para>
    /// <para>
    /// A single blank line is the separator, so a failure may run to as many sentences or wrapped lines
    /// as it needs. Text with no blank line in it is therefore <b>one</b> item, which is the correct
    /// reading of a single paragraph — and the one case where this differs from the old behaviour.
    /// </para>
    /// <para>
    /// The array shape is still accepted, for a programmatic writer (a later paste-and-extract
    /// acquisition) that already has the items separated.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<string> ReadParagraphs(JsonElement obj, string name)
    {
        if (!TryGetPropertyIgnoreCase(obj, name, out var prop)) return [];

        var lines = new List<string>();
        if (prop.ValueKind == JsonValueKind.String)
        {
            var raw = (prop.GetString() ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
            foreach (var block in Regex.Split(raw, @"\n\s*\n"))
            {
                // Soft-wrapped lines inside one paragraph join into one sentence rather than staying
                // broken: the writer is given prose to argue from, not a column of fragments.
                var joined = string.Join(
                    " ",
                    block.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                var trimmed = joined.Trim();
                if (trimmed.Length > 0) lines.Add(trimmed);
            }
        }
        else if (prop.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in prop.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.String) continue;
                var trimmed = (el.GetString() ?? string.Empty).Trim();
                if (trimmed.Length > 0) lines.Add(trimmed);
            }
        }

        return lines;
    }

    /// <summary>
    /// Case-insensitive property read. The frontend sends camelCase; nothing guarantees a future writer
    /// does, and a brief that silently reads as empty is the failure this whole file exists to remove.
    /// </summary>
    private static bool TryGetPropertyIgnoreCase(JsonElement obj, string name, out JsonElement value)
    {
        if (obj.TryGetProperty(name, out value)) return true;

        foreach (var prop in obj.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}

/// <summary>
/// One retrieval question from the brief's niche framing (the operator's <c>evidence</c> rows):
/// the reader's failure, the vendor's solution in the vendor's own vocabulary, and a few of the
/// vendor's terms. <paramref name="Solution"/> is sent to the index as <c>need</c> (the meaning
/// half); <paramref name="Terms"/>, joined, as <c>keyword</c> (the keyword half). Never quoted and
/// never shown to the writer as evidence: what retrieval returns for it is the evidence.
/// </summary>
public sealed record GccEvidenceRow(string Problem, string Solution, IReadOnlyList<string> Terms)
{
    /// <summary>The keyword half's text, or null when the row names no terms.</summary>
    public string? Keyword => Terms.Count == 0 ? null : string.Join(" ", Terms);

    /// <summary>What the meaning half is asked: the solution, else the problem.</summary>
    public string Need => Solution.Length > 0 ? Solution : Problem;
}
