using System.Text;
using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// Gives a passage of research its labels: which parts of a pillar or a blog it is for.
/// </summary>
/// <remarks>
/// <para>
/// <b>By code, the same way every time,</b> from three things the page already carries: the kind of
/// page the index marked it, which of the brief's searches found the passage, and the words in the
/// passage. No model reads it: a model's labels would differ from run to run and would be one more
/// request that can fail.
/// </para>
/// <para>
/// <b>Each thing adds to a label's strength, and a label needs a strength of
/// <see cref="Threshold"/> to be given.</b> One common word is not enough on its own ("team", "plan"),
/// and neither is a page being a product page; a pricing page is, and so is the brief's problem
/// having been what found the passage. A passage no label reaches is general.
/// </para>
/// <para>
/// <b>The word lists name no subject.</b> They are the words any vendor's pages use for these seven
/// things, whatever the product is. A list that named invoices or approvals would label one project's
/// research and misread the next: the tool page's outline did exactly that until 2026-10-11.
/// </para>
/// <para>
/// <b>A word and its other forms are one word.</b> "client" and "clients", "team" and "finance team",
/// "setup" and "set up" are each a family here, and a family counts once however many of its forms a
/// passage holds. They were counted separately until the 14:20 UTC run of 2026-10-11, the first real
/// one: a passage that said "clients" and "each client" was given people and roles from one word.
/// </para>
/// <para>
/// <b>A word that is the subject's own is not in a list.</b> The same run's research was about cash
/// flow forecasting, where a "plan" is a financial plan and a "timeline" is a forecast's: 17 passages
/// were labelled fit and price by "plan" and "plans" alone. Both are gone, with "results", "review",
/// "faster" and "increased", which labelled seven passages as proof and none of them was.
/// </para>
/// <para>
/// <b>Crude, and known to be.</b> Word matching failed twice on 2026-10-10 at a harder job, finding
/// the one passage that answers a question. This asks less of it: a label decides the order a
/// partner's passages are taken in, never whether a partner is carried. The lists were adjusted from
/// the first real run, which was under the ceiling; no run has yet chosen by them.
/// </para>
/// </remarks>
public static class GccPassageClassifier
{
    /// <summary>The strength a label needs before a passage is given it.</summary>
    public const int Threshold = 2;

    /// <summary>The most a passage's own words add to one label, however many of its words match.</summary>
    public const int MaxFromWords = 3;

    /// <summary>
    /// The words for each label, a family to a line: a word with its other forms and spellings. Each
    /// as it reads once a text is lower case with everything that is not a letter or a digit turned
    /// into a space: "go-live" is "go live", "real-time" is "real time".
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string[][]> Words = new Dictionary<string, string[][]>(StringComparer.Ordinal)
    {
        [GccResearchLabel.Problem] =
        [
            ["manual", "manually", "by hand"],
            ["error", "errors", "error prone"],
            ["mistake", "mistakes"],
            ["time consuming"],
            ["tedious"],
            ["waste", "wasted"],
            ["late"],
            ["delay", "delays", "delayed"],
            ["risk", "risks"],
            ["costly"],
            ["bottleneck", "bottlenecks"],
            ["struggle", "struggling"],
            ["problem", "problems"],
            ["challenge", "challenges"],
            ["pain"],
            ["frustrating"],
            ["inefficient"],
            ["guesswork"],
            ["out of date", "outdated"],
        ],
        [GccResearchLabel.HowItWorks] =
        [
            ["how it works"],
            ["automatically", "automates", "automate", "automated", "automation"],
            ["workflow", "workflows"],
            ["feature", "features"],
            ["dashboard", "dashboards"],
            ["real time"],
            ["generates"],
            ["updates"],
            ["tracks"],
            ["step by step"],
            ["in one place"],
            ["built in"],
        ],
        [GccResearchLabel.GettingItRunning] =
        [
            ["setup", "set up"],
            ["onboarding", "onboard"],
            ["implementation", "implement", "implementing"],
            ["getting started", "get started"],
            ["go live"],
            ["rollout", "roll out"],
            ["migration", "migrate"],
            ["configure", "configuration"],
            ["training"],
            ["deploy", "deployment"],
            ["install"],
            ["in minutes"],
            ["up and running"],
            ["kickoff"],
        ],
        [GccResearchLabel.DataAndIntegrations] =
        [
            ["integration", "integrations", "integrates", "integrate", "integrated"],
            ["connect", "connects", "connected", "connector", "connectors"],
            ["api"],
            ["sync", "syncs", "synced"],
            ["import", "imports"],
            ["export", "exports"],
            ["csv"],
            ["data source", "data sources"],
            ["erp"],
            ["accounting software"],
            ["bank feed", "bank feeds"],
            ["webhook", "webhooks"],
            ["single sign on", "sso"],
        ],
        [GccResearchLabel.PeopleAndRoles] =
        [
            ["team", "teams", "finance team"],
            ["role", "roles"],
            ["cfo"],
            ["controller", "controllers"],
            ["accountant", "accountants"],
            ["bookkeeper", "bookkeepers"],
            ["owner", "owners"],
            ["manager", "managers"],
            ["staff"],
            ["collaborate", "collaboration"],
            ["permission", "permissions"],
            ["stakeholder", "stakeholders"],
            ["advisor", "advisors"],
            ["client", "clients"],
            ["employee", "employees"],
        ],
        [GccResearchLabel.ResultsAndProof] =
        [
            ["case study", "case studies"],
            ["customer story", "customer stories", "success story"],
            ["saved", "saves"],
            ["reduced", "reduces"],
            ["roi", "return on investment"],
            ["percent"],
            ["testimonial", "testimonials"],
            ["rated"],
            ["award", "awards"],
            ["customers say"],
            ["hours a week", "hours per week"],
        ],
        [GccResearchLabel.FitPriceAndLimits] =
        [
            ["pricing", "price", "prices", "priced"],
            ["per month", "per user", "per year"],
            ["free trial"],
            ["tier", "tiers"],
            ["enterprise"],
            ["limit", "limits", "limited"],
            ["best for", "ideal for"],
            ["not suitable"],
            ["alternative", "alternatives"],
            ["compare", "comparison", "versus", "vs"],
            ["cancel"],
            ["contract"],
            ["billed"],
            ["subscription"],
        ],
    };

    /// <summary>
    /// The labels the passage at <paramref name="paragraph"/> of <paramref name="page"/> is given, each
    /// with its strength. Empty for a general passage, and for a place the page has no passage at.
    /// </summary>
    public static IReadOnlyDictionary<string, int> LabelsOf(GccQuoteablePage page, int paragraph)
    {
        if (page?.Paragraphs is null || paragraph < 0 || paragraph >= page.Paragraphs.Count) return Empty;

        var strength = new Dictionary<string, int>(StringComparer.Ordinal);
        AddFromPageKind(strength, page.Category);
        AddFromSearches(strength, page.SearchesThatFound(paragraph));
        AddFromWords(strength, Normalize(WhatItSays(page.Paragraphs[paragraph] ?? string.Empty)), each: 1, most: MaxFromWords);

        var given = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var label in GccResearchLabel.All)
        {
            if (strength.TryGetValue(label, out var s) && s >= Threshold) given[label] = s;
        }

        return given;
    }

    private static readonly IReadOnlyDictionary<string, int> Empty = new Dictionary<string, int>();

    /// <summary>
    /// A passage without the line of link text printed under it. That line is the section's links,
    /// often the site's own menu ("Pricing, Integrations, Customers"), and would label every passage
    /// of a site by its navigation.
    /// </summary>
    private static string WhatItSays(string passage)
    {
        var said = new StringBuilder(passage.Length);
        foreach (var line in passage.Split('\n'))
        {
            if (line.TrimStart().StartsWith(GeekCrawler.HttpGeekCrawlerRagClient.LinkedFromLabel, StringComparison.Ordinal)) continue;
            said.Append(line).Append('\n');
        }

        return said.ToString();
    }

    /// <summary>
    /// What the kind of page says. A pricing or a comparison page is about fit and price whatever its
    /// passage says. A product page and a documentation page lean a passage one way without settling
    /// it. A blog post, an about page and an unmarked page say nothing: they can be about anything.
    /// </summary>
    private static void AddFromPageKind(Dictionary<string, int> strength, string? kind)
    {
        switch (kind)
        {
            case "pricing":
            case "compare":
                Add(strength, GccResearchLabel.FitPriceAndLimits, 2);
                break;
            case "product":
                Add(strength, GccResearchLabel.HowItWorks, 1);
                break;
            case "docs":
                Add(strength, GccResearchLabel.HowItWorks, 1);
                Add(strength, GccResearchLabel.GettingItRunning, 1);
                Add(strength, GccResearchLabel.DataAndIntegrations, 1);
                break;
        }
    }

    /// <summary>
    /// What the searches that found it say. The brief's problem finds passages about the problem. A
    /// row's search is asked the vendor's remedy, so it finds how the product works, and the remedy's
    /// own words say what else it is about. Counted once however many searches of a kind found it.
    /// </summary>
    private static void AddFromSearches(Dictionary<string, int> strength, IReadOnlyList<GccPassageSearch> searches)
    {
        var fromProblem = false;
        var fromRow = false;
        var remedies = new StringBuilder();
        foreach (var search in searches)
        {
            if (search is null) continue;
            if (search.Kind == GccPassageSearch.KindProblem) fromProblem = true;
            if (search.Kind != GccPassageSearch.KindRow) continue;
            fromRow = true;
            remedies.Append(' ').Append(search.Asked).Append(' ').Append(search.Terms);
        }

        if (fromProblem) Add(strength, GccResearchLabel.Problem, 2);
        if (!fromRow) return;

        Add(strength, GccResearchLabel.HowItWorks, 2);
        var words = new Dictionary<string, int>(StringComparer.Ordinal);
        AddFromWords(words, Normalize(remedies.ToString()), each: 1, most: 1);
        foreach (var (label, s) in words)
        {
            // How it works is already given for a row; its remedy's words do not give it twice.
            if (label != GccResearchLabel.HowItWorks) Add(strength, label, s);
        }
    }

    /// <summary>
    /// Adds <paramref name="each"/> to a label for every one of its words the text holds, up to
    /// <paramref name="most"/>. A word is counted once, in whichever of its forms it is found.
    /// </summary>
    private static void AddFromWords(Dictionary<string, int> strength, string normalized, int each, int most)
    {
        foreach (var (label, families) in Words)
        {
            var found = 0;
            foreach (var family in families)
            {
                if (found >= most) break;
                if (family.Any(form => normalized.Contains($" {form} ", StringComparison.Ordinal))) found += each;
            }

            if (found > 0) Add(strength, label, Math.Min(found, most));
        }
    }

    private static void Add(Dictionary<string, int> strength, string label, int by) =>
        strength[label] = strength.GetValueOrDefault(label) + by;

    /// <summary>
    /// A text as its words: lower case, every run of anything that is not a letter or a digit one
    /// space, and a space at each end, so a word is found by looking for it between two spaces.
    /// </summary>
    internal static string Normalize(string? text)
    {
        var sb = new StringBuilder((text?.Length ?? 0) + 2);
        sb.Append(' ');
        var betweenWords = true;
        foreach (var ch in text ?? string.Empty)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
                betweenWords = false;
                continue;
            }

            if (betweenWords) continue;
            sb.Append(' ');
            betweenWords = true;
        }

        if (!betweenWords) sb.Append(' ');
        return sb.ToString();
    }
}
