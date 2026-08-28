using System.Text.Json;
using System.Text.RegularExpressions;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Geo;
using GeekAPI.Services.ContentCreatorV2.Guardrail;
using GeekAPI.Services.ContentCreatorV2.Jobs;
using GeekAPI.Services.ContentCreatorV2.Write;
using GeekAPI.Services.Gcw;
using GeekAPI.Services.Workflow.Domain.Entities;

namespace GeekAPI.Services.ContentCreatorV2.Validate;

/// <summary>One VALIDATE pass's findings — everything the <c>ValidationReport</c> job event and
/// the REPAIR loop need. <see cref="GeoScore"/>/<see cref="GeoChecks"/> (AI-visibility readiness,
/// <see cref="GccV2GeoAnalyzer"/>) are advisory only — like SEO, a low GEO score never blocks
/// <see cref="ShipReady"/>.</summary>
public sealed record GccV2ValidationReport(
    string ReviewVerdict,
    string? ReviewNotes,
    int SeoScore,
    int PolishScore,
    bool PolishShipReady,
    IReadOnlyList<OverlapHit> OverlapHits,
    int GuardrailFlaggedCount = 0,
    int GuardrailRestructureCount = 0,
    IReadOnlyList<string>? GuardrailRestructurePhrases = null,
    int GeoScore = 0,
    IReadOnlyList<GccV2GeoAnalyzer.GeoCheck>? GeoChecks = null,
    string? GeoSummary = null,
    IReadOnlyList<GcwSeoAnalyzer.SeoCheck>? SeoChecks = null)
{
    public bool ShipReady => OverlapHits.Count == 0
        && (ReviewVerdict is "approved" or "skipped")
        && PolishShipReady
        && GuardrailRestructureCount == 0;
}

public sealed record GccV2ValidateOutcome(GccV2WriteOutput Final, GccV2ValidationReport Report, bool ShipReady, bool OutstandingIssues, int RepairAttempts);

/// <summary>
/// Phase 5 VALIDATE + REPAIR: <see cref="GccV2ReviewAdapter"/> (editorial rubric) +
/// <c>GcwSeoAnalyzer</c> + <c>GcwPolishAnalyzer</c> (called, never edited) +
/// <see cref="GccV2OverlapGate"/> (v2-only). Never reports <c>ShipReady:true</c> while an overlap
/// hit remains. REPAIR targets only the flagged section(s), capped at
/// <see cref="MaxRepairAttempts"/> whole VALIDATE passes — if issues remain after the cap, the job
/// still completes as <c>ready</c> with an explicit <c>outstandingIssues</c> flag rather than
/// failing or silently discarding the problem.
/// </summary>
public sealed class GccV2ValidateService
{
    private const int MaxRepairAttempts = 2;

    private static readonly Regex SectionRefPattern = new(@"\[Section:\s*""([^""]+)""\]", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly HttpGccV2Repository _repo;
    private readonly GccV2ReviewAdapter _reviewAdapter;
    private readonly GccV2JobEventWriter _events;
    private readonly GccV2WriteService _writeService;
    private readonly GuardrailGateService _guardrailGate;
    private readonly GccV2RestructurePassService _restructurePass;
    private readonly ILogger<GccV2ValidateService> _logger;

    public GccV2ValidateService(
        HttpGccV2Repository repo,
        GccV2ReviewAdapter reviewAdapter,
        GccV2JobEventWriter events,
        GccV2WriteService writeService,
        GuardrailGateService guardrailGate,
        GccV2RestructurePassService restructurePass,
        ILogger<GccV2ValidateService> logger)
    {
        _repo = repo;
        _reviewAdapter = reviewAdapter;
        _events = events;
        _writeService = writeService;
        _guardrailGate = guardrailGate;
        _restructurePass = restructurePass;
        _logger = logger;
    }

    public async Task<GccV2ValidateOutcome> RunAsync(
        GccV2WriteContext wc, Guid ownerUserId, GccV2WriteOutput initial, CancellationToken ct)
    {
        var current = initial;
        var attempt = 0;

        while (true)
        {
            var report = await EvaluateAsync(wc, current, ct);
            await PersistAndEmitReportAsync(wc.Job.Id, ownerUserId, report, attempt, ct);

            if (report.ShipReady || attempt >= MaxRepairAttempts)
            {
                return new GccV2ValidateOutcome(current, report, report.ShipReady, !report.ShipReady, attempt);
            }

            attempt++;
            current = await RepairAsync(wc, ownerUserId, current, report, attempt, ct);
        }
    }

    /// <summary>Operator-triggered readiness repair — one evaluate/repair/evaluate pass for advisory SEO/GEO fails.</summary>
    public async Task<GccV2ValidateOutcome> RunReadinessFixAsync(
        GccV2WriteContext wc, Guid ownerUserId, GccV2WriteOutput current, CancellationToken ct)
    {
        var report = await EvaluateAsync(wc, current, ct);
        if (!HasReadinessFailures(report))
        {
            await PersistAndEmitReportAsync(wc.Job.Id, ownerUserId, report, attempt: 0, ct);
            return new GccV2ValidateOutcome(current, report, report.ShipReady, false, 0);
        }

        var repaired = await RepairAsync(wc, ownerUserId, current, report, attempt: 1, ct);
        report = await EvaluateAsync(wc, repaired, ct);
        await PersistAndEmitReportAsync(wc.Job.Id, ownerUserId, report, attempt: 1, ct);
        var outstanding = !report.ShipReady || HasReadinessFailures(report);
        return new GccV2ValidateOutcome(repaired, report, report.ShipReady, outstanding, 1);
    }

    private static bool HasReadinessFailures(GccV2ValidationReport report) =>
        (report.SeoChecks?.Any(c => !c.Passed) ?? false)
        || (report.GeoChecks?.Any(c => !c.Passed) ?? false);

    private async Task<GccV2ValidationReport> EvaluateAsync(GccV2WriteContext wc, GccV2WriteOutput output, CancellationToken ct)
    {
        var contentType = (wc.Job.ContentType ?? "blog").ToLowerInvariant();
        var document = output.ToContentDocument();
        var gate = await _guardrailGate.EvaluateAsync(document, wc.BaseContext.TargetKeyword, contentType, ct);
        document = gate.CleanedDocument;

        var overlapInputs = BuildOverlapInputs(output, document);
        var overlapHits = contentType is "social" or "image-prompt"
            ? Array.Empty<OverlapHit>()
            : GccV2OverlapGate.Detect(overlapInputs);

        string reviewVerdict = "skipped";
        string? reviewNotes = null;
        // Phase D: skip Groq editorial VALIDATE for all content types — keep overlap + guardrail + polish.

        return new GccV2ValidationReport(
            reviewVerdict,
            reviewNotes,
            gate.Seo.Score,
            gate.Polish.Score,
            gate.Polish.ShipReady,
            overlapHits,
            gate.GuardrailFlaggedCount,
            gate.GuardrailRestructureCount,
            gate.GuardrailRestructurePhrases,
            gate.Geo.Score,
            gate.Geo.Checks,
            gate.Geo.Summary,
            gate.Seo.Checks);
    }

    private static List<OverlapSectionInput> BuildOverlapInputs(GccV2WriteOutput output, ContentDocument document)
    {
        var allSections = document.Sections.Prepend(document.Lede).ToList();
        var writeSections = output.AllSections;
        var inputs = new List<OverlapSectionInput>();
        for (var i = 0; i < writeSections.Count && i < allSections.Count; i++)
        {
            var ws = writeSections[i];
            inputs.Add(new OverlapSectionInput(
                ws.SectionKey,
                ws.Heading,
                ws.Job,
                GccV2OverlapGate.FlattenPlainText(allSections[i])));
        }

        return inputs;
    }

    private async Task PersistAndEmitReportAsync(Guid jobId, Guid ownerUserId, GccV2ValidationReport report, int attempt, CancellationToken ct)
    {
        var payload = new
        {
            shipReady = report.ShipReady,
            reviewVerdict = report.ReviewVerdict,
            reviewNotes = report.ReviewNotes,
            seoScore = report.SeoScore,
            polishScore = report.PolishScore,
            polishShipReady = report.PolishShipReady,
            guardrailFlaggedCount = report.GuardrailFlaggedCount,
            guardrailRestructureCount = report.GuardrailRestructureCount,
            guardrailRestructurePhrases = report.GuardrailRestructurePhrases ?? Array.Empty<string>(),
            geoScore = report.GeoScore,
            geoSummary = report.GeoSummary,
            geoChecks = (report.GeoChecks ?? Array.Empty<GccV2GeoAnalyzer.GeoCheck>()).Select(c => new
            {
                id = c.Id,
                label = c.Label,
                passed = c.Passed,
                detail = c.Detail,
                fixHint = c.FixHint,
            }).ToList(),
            seoChecks = (report.SeoChecks ?? Array.Empty<GcwSeoAnalyzer.SeoCheck>()).Select(c => new
            {
                id = c.Id,
                label = c.Label,
                passed = c.Passed,
                detail = c.Detail,
                fixHint = c.FixHint,
            }).ToList(),
            overlapHits = report.OverlapHits.Select(h => new
            {
                headingA = h.HeadingA,
                headingB = h.HeadingB,
                sharedClaim = h.SharedClaim,
                sectionKeyA = h.SectionKeyA,
                sectionKeyB = h.SectionKeyB,
                repairHint = h.RepairHint,
            }).ToList(),
            outstandingIssues = !report.ShipReady,
            repairAttempt = attempt,
        };

        await _repo.AddStageResultAsync(
            jobId,
            new CreateGccV2StageResultCommand("validate", null, JsonSerializer.Serialize(payload, JsonOpts), 0),
            ct);
        await _events.AppendAsync(jobId, ownerUserId, "ValidationReport", payload, ct: ct);
    }

    private async Task<GccV2WriteOutput> RepairAsync(
        GccV2WriteContext wc, Guid ownerUserId, GccV2WriteOutput current, GccV2ValidationReport report, int attempt, CancellationToken ct)
    {
        var targets = SelectRepairTargets(wc, current, report);
        if (targets.Count == 0)
        {
            _logger.LogInformation(
                "VALIDATE flagged issues for job {JobId} on attempt {Attempt} but no section could be matched for repair.",
                wc.Job.Id, attempt);
            return current;
        }

        var updated = current;
        foreach (var target in targets)
        {
            if (target.IsAppendFaq)
            {
                var paa = wc.BaseContext.PeopleAlsoAskQuestions ?? [];
                updated = await _writeService.AppendFaqSectionAsync(wc, ownerUserId, updated, paa, ct);
                continue;
            }

            var section = updated.AllSections.FirstOrDefault(s => s.SectionKey == target.SectionKey);
            if (section is null) continue;

            GccV2WriteSection rewritten;
            if (target.IsRestructurePass && report.GuardrailRestructurePhrases?.Count > 0)
            {
                var (newSection, tokens) = await _restructurePass.RewriteSectionAsync(
                    section.Section,
                    report.GuardrailRestructurePhrases,
                    wc.BaseContext,
                    wc.Provider,
                    ct);
                rewritten = new GccV2WriteSection(section.SectionKey, section.Heading, section.Job, newSection, false);
                await _writeService.PublishSectionRepairAsync(wc, ownerUserId, rewritten, tokens, ct);
            }
            else
            {
                rewritten = await _writeService.RewriteSectionAsync(
                    wc, ownerUserId, current.Title, section, target.RevisionNotes, ct);
            }

            updated = updated.WithSection(rewritten);
        }

        return updated;
    }

    private static List<RepairTarget> SelectRepairTargets(
        GccV2WriteContext wc,
        GccV2WriteOutput current,
        GccV2ValidationReport report)
    {
        var targets = new Dictionary<string, string>();
        var contentType = (wc.Job.ContentType ?? "blog").Trim().ToLowerInvariant();
        var longForm = GcwContentTypeScoring.IsLongForm(contentType);
        var keyword = (wc.BaseContext.TargetKeyword ?? "").Trim();
        var paaQuestions = wc.BaseContext.PeopleAlsoAskQuestions ?? [];

        foreach (var hit in report.OverlapHits)
        {
            targets[hit.SectionKeyB] = hit.RepairHint;
        }

        if (!string.IsNullOrWhiteSpace(report.ReviewNotes))
        {
            foreach (Match m in SectionRefPattern.Matches(report.ReviewNotes))
            {
                var heading = m.Groups[1].Value;
                var match = current.AllSections.FirstOrDefault(s => string.Equals(s.Heading, heading, StringComparison.OrdinalIgnoreCase));
                if (match is not null && !targets.ContainsKey(match.SectionKey))
                {
                    targets[match.SectionKey] = $"Editorial review requires a change here: {report.ReviewNotes}";
                }
            }
        }

        if (targets.Count == 0 && !report.PolishShipReady)
        {
            targets[current.Lede.SectionKey] =
                "The ship-readiness polish check failed (placeholder copy, prohibited phrasing, or similar). " +
                "Rewrite this section so it is publish-ready.";
        }

        if (report.GuardrailRestructureCount > 0 && report.GuardrailRestructurePhrases?.Count > 0)
        {
            foreach (var writeSection in current.AllSections)
            {
                var text = GccV2OverlapGate.FlattenPlainText(writeSection.Section);
                if (!report.GuardrailRestructurePhrases.Any(p =>
                        text.Contains(p, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                if (!targets.ContainsKey(writeSection.SectionKey))
                {
                    targets[writeSection.SectionKey] =
                        $"Pass-2 restructure: rewrite to remove flagged phrases ({string.Join(", ", report.GuardrailRestructurePhrases)}).";
                }
            }
        }

        if (longForm && targets.Count < 3)
        {
            AddSeoGeoRepairTargets(targets, current, report, keyword, paaQuestions);
        }

        return targets.Select(kv => new RepairTarget(
            kv.Key,
            kv.Value,
            kv.Value.StartsWith("Pass-2 restructure", StringComparison.OrdinalIgnoreCase),
            string.Equals(kv.Key, GcwContentTypeScoring.AppendFaqSectionKey, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    private static void AddSeoGeoRepairTargets(
        Dictionary<string, string> targets,
        GccV2WriteOutput current,
        GccV2ValidationReport report,
        string keyword,
        IReadOnlyList<string> paaQuestions)
    {
        foreach (var check in report.GeoChecks ?? [])
        {
            if (check.Passed || targets.Count >= 3) continue;
            if (check.Id == "faq-or-direct-answers")
            {
                var faqSection = FindFaqSection(current);
                var questions = paaQuestions.Count > 0
                    ? string.Join("; ", paaQuestions.Take(12))
                    : "Use 3–5 direct questions about the topic.";
                var faqNotes =
                    $"GEO repair — FAQ/direct answers: rewrite this section as “People Also Ask”. "
                    + $"Each question must be an H3 with a direct one-sentence answer opener plus 2–4 sentences. "
                    + $"Use these operator PAA questions verbatim when provided: {questions}";

                if (faqSection is not null && !targets.ContainsKey(faqSection.SectionKey))
                {
                    targets[faqSection.SectionKey] = faqNotes;
                }
                else if (faqSection is null && paaQuestions.Count > 0 && !targets.ContainsKey(GcwContentTypeScoring.AppendFaqSectionKey))
                {
                    targets[GcwContentTypeScoring.AppendFaqSectionKey] = faqNotes;
                }
            }
            else if (check.Id == "citeable-passages")
            {
                var body = PickBodySectionForExpansion(current, skipFaq: true);
                if (body is not null && !targets.ContainsKey(body.SectionKey))
                {
                    targets[body.SectionKey] =
                        check.FixHint
                        ?? "Rewrite 1–2 paragraphs here as standalone, citeable claims (≥40 words each, no “this/it” openers).";
                }
            }
        }

        foreach (var check in report.SeoChecks ?? [])
        {
            if (check.Passed || targets.Count >= 3) continue;
            switch (check.Id)
            {
                case "keyword-in-lede":
                    if (!targets.ContainsKey(current.Lede.SectionKey))
                    {
                        targets[current.Lede.SectionKey] =
                            check.FixHint
                            ?? $"Include “{keyword}” naturally in the opening lede.";
                    }
                    break;
                case "keyword-in-heading":
                {
                    var section = current.AllSections.FirstOrDefault(s =>
                        s.Job != "faq"
                        && !s.Heading.Contains("People Also Ask", StringComparison.OrdinalIgnoreCase));
                    if (section is not null && !targets.ContainsKey(section.SectionKey))
                    {
                        targets[section.SectionKey] =
                            check.FixHint
                            ?? $"Use “{keyword}” in this section heading or add a sibling H2 that includes it.";
                    }
                    break;
                }
                case "word-count":
                case "keyword-density":
                case "section-count":
                {
                    var section = PickBodySectionForExpansion(current, skipFaq: true);
                    if (section is not null && !targets.ContainsKey(section.SectionKey))
                    {
                        targets[section.SectionKey] =
                            check.FixHint
                            ?? "Expand this section with concrete examples, steps, and specifics while keeping the keyword natural.";
                    }
                    break;
                }
            }
        }
    }

    private static GccV2WriteSection? FindFaqSection(GccV2WriteOutput current) =>
        current.AllSections.FirstOrDefault(s =>
            string.Equals(s.Job, "faq", StringComparison.OrdinalIgnoreCase)
            || s.Heading.Contains("People Also Ask", StringComparison.OrdinalIgnoreCase));

    private static GccV2WriteSection? PickBodySectionForExpansion(GccV2WriteOutput current, bool skipFaq)
    {
        var candidates = current.AllSections
            .Where(s => s.SectionKey != current.Lede.SectionKey)
            .Where(s => !skipFaq || (s.Job != "faq" && !s.Heading.Contains("People Also Ask", StringComparison.OrdinalIgnoreCase)))
            .ToList();
        return candidates.Count == 0 ? null : candidates[candidates.Count / 2];
    }

    private sealed record RepairTarget(string SectionKey, string RevisionNotes, bool IsRestructurePass = false, bool IsAppendFaq = false);
}
