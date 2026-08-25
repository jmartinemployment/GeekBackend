using System.Text.RegularExpressions;
using GeekAPI.HttpClients;
using GeekAPI.Services.Workflow.Domain.Entities;
using Microsoft.Extensions.Caching.Memory;

namespace GeekAPI.Services.ContentCreatorV2.Guardrail;

public enum GccV2GuardrailAction { Strip, Replace, Restructure }

public sealed record GccV2GuardrailRuleModel(
    string Pattern,
    GccV2GuardrailAction Action,
    string? ReplaceWith,
    string? ReasonCode,
    string? Scope = null);

public sealed record GccV2GuardrailApplyResult(
    ContentDocument Document,
    int FlaggedCount,
    int RestructureCount,
    IReadOnlyList<string> RestructurePhrases);

/// <summary>
/// Injectable port of v1 <c>ContentGuardrail</c> mechanics — loads rules from
/// <see cref="GccV2GuardrailRule"/> (DB) with a short TTL cache, falling back to the same eight
/// seed rules v1 ships if the table is empty.
/// </summary>
public sealed class GccV2GuardrailService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);
    private const string CacheKey = "gcc_v2_guardrail_rules";

    private static readonly IReadOnlyList<GccV2GuardrailRuleModel> SeedRules =
    [
        new("in today's fast-paced digital world", GccV2GuardrailAction.Strip, null, "AI_FILLER"),
        new("delve deeper", GccV2GuardrailAction.Replace, "examine", "AI_FILLER"),
        new("it is crucial to remember", GccV2GuardrailAction.Strip, null, "AI_FILLER"),
        new("testament to", GccV2GuardrailAction.Restructure, null, "AI_FILLER"),
        new("synergistic approach", GccV2GuardrailAction.Replace, "collaborative strategy", "CORP_JARGON"),
        new("paradigm shift", GccV2GuardrailAction.Replace, "fundamental change", "CORP_JARGON"),
        new("utilize", GccV2GuardrailAction.Replace, "use", "CORP_JARGON"),
        new("moving the needle", GccV2GuardrailAction.Replace, "achieving measurable results", "CORP_JARGON"),
    ];

    private readonly HttpGccV2Repository _repo;
    private readonly IMemoryCache _cache;
    private readonly ILogger<GccV2GuardrailService> _logger;

    public GccV2GuardrailService(HttpGccV2Repository repo, IMemoryCache cache, ILogger<GccV2GuardrailService> logger)
    {
        _repo = repo;
        _cache = cache;
        _logger = logger;
    }

    public async Task<GccV2GuardrailApplyResult> ApplyAsync(ContentDocument document, string? contentType, CancellationToken ct)
    {
        var rules = await GetRulesAsync(ct);
        if (!string.IsNullOrWhiteSpace(contentType))
            rules = rules.Where(r => r.Scope is null || string.Equals(r.Scope, contentType, StringComparison.OrdinalIgnoreCase)).ToList();

        var compiled = Compile(rules);
        var counter = new Counter();
        var restructurePhrases = new List<string>();

        var lede = CleanSection(document.Lede, compiled, counter, restructurePhrases);
        var sections = document.Sections.Select(s => CleanSection(s, compiled, counter, restructurePhrases)).ToList();
        return new GccV2GuardrailApplyResult(new ContentDocument(lede, sections), counter.Value, restructurePhrases.Count, restructurePhrases);
    }

    private async Task<IReadOnlyList<GccV2GuardrailRuleModel>> GetRulesAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue(CacheKey, out IReadOnlyList<GccV2GuardrailRuleModel>? cached) && cached is not null)
            return cached;

        IReadOnlyList<GccV2GuardrailRuleModel> rules;
        try
        {
            var dtos = await _repo.ListGuardrailRulesAsync(enabled: true, ct);
            if (dtos.Count == 0)
            {
                try { await _repo.SeedDefaultGuardrailRulesAsync(ct); } catch (Exception ex) { _logger.LogWarning(ex, "Guardrail seed failed; using in-memory seed."); }
                dtos = await _repo.ListGuardrailRulesAsync(enabled: true, ct);
            }

            rules = dtos.Count == 0
                ? SeedRules
                : dtos.Select(d => new GccV2GuardrailRuleModel(
                    d.Pattern,
                    ParseAction(d.Action),
                    d.ReplaceWith,
                    d.ReasonCode,
                    d.Scope)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Loading guardrail rules failed; using in-memory seed.");
            rules = SeedRules;
        }

        _cache.Set(CacheKey, rules, CacheTtl);
        return rules;
    }

    private static GccV2GuardrailAction ParseAction(string action) => action.ToLowerInvariant() switch
    {
        "replace" => GccV2GuardrailAction.Replace,
        "restructure" => GccV2GuardrailAction.Restructure,
        _ => GccV2GuardrailAction.Strip,
    };

    private static IReadOnlyList<(GccV2GuardrailRuleModel Rule, Regex Regex)> Compile(IReadOnlyList<GccV2GuardrailRuleModel> rules) =>
        rules.Select(r => (r, new Regex($@"\b{Regex.Escape(r.Pattern)}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled))).ToList();

    private static (string Text, int Flagged, bool RestructureHit) CleanText(
        string text,
        IReadOnlyList<(GccV2GuardrailRuleModel Rule, Regex Regex)> compiled,
        List<string> restructurePhrases)
    {
        if (string.IsNullOrEmpty(text)) return (text, 0, false);
        var flagged = 0;
        var restructure = false;
        var working = text;
        foreach (var (rule, regex) in compiled)
        {
            if (!regex.IsMatch(working)) continue;
            flagged += regex.Matches(working).Count;
            switch (rule.Action)
            {
                case GccV2GuardrailAction.Strip:
                    working = regex.Replace(working, string.Empty);
                    break;
                case GccV2GuardrailAction.Replace:
                    working = regex.Replace(working, rule.ReplaceWith ?? string.Empty);
                    break;
                case GccV2GuardrailAction.Restructure:
                    restructure = true;
                    restructurePhrases.Add(rule.Pattern);
                    break;
            }
        }

        working = Regex.Replace(working, @"[ \t]{2,}", " ");
        working = Regex.Replace(working, @"\s+([.,;:!?])", "$1");
        return (working.Trim().Length == 0 ? text : working, flagged, restructure);
    }

    private static Section CleanSection(
        Section s,
        IReadOnlyList<(GccV2GuardrailRuleModel Rule, Regex Regex)> compiled,
        Counter counter,
        List<string> restructurePhrases)
    {
        var heading = s.Heading;
        if (!string.IsNullOrEmpty(heading))
        {
            var (h, f, r) = CleanText(heading, compiled, restructurePhrases);
            heading = h;
            counter.Value += f;
            if (r) { /* counted via restructurePhrases */ }
        }

        var paragraphs = s.Paragraphs.Select(p => CleanParagraph(p, compiled, counter, restructurePhrases)).ToList();
        var children = s.Children.Select(c => CleanSection(c, compiled, counter, restructurePhrases)).ToList();
        return s with { Heading = heading, Paragraphs = paragraphs, Children = children };
    }

    private static Paragraph CleanParagraph(
        Paragraph p,
        IReadOnlyList<(GccV2GuardrailRuleModel Rule, Regex Regex)> compiled,
        Counter counter,
        List<string> restructurePhrases)
    {
        switch (p)
        {
            case TextParagraph tp:
                return new TextParagraph(CleanRuns(tp.Runs, compiled, counter, restructurePhrases));
            case ListParagraph lp:
                var items = lp.Items.Select(item => CleanRuns(item, compiled, counter, restructurePhrases)).ToList();
                return new ListParagraph(lp.Ordered, items);
            default:
                return p;
        }
    }

    private static IReadOnlyList<Run> CleanRuns(
        IReadOnlyList<Run> runs,
        IReadOnlyList<(GccV2GuardrailRuleModel Rule, Regex Regex)> compiled,
        Counter counter,
        List<string> restructurePhrases)
    {
        var result = new List<Run>(runs.Count);
        foreach (var run in runs)
        {
            var (text, f, _) = CleanText(run.Text, compiled, restructurePhrases);
            counter.Value += f;
            result.Add(run with { Text = text });
        }
        return result;
    }

    private sealed class Counter { public int Value; }
}
