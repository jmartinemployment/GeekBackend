using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GeekAPI.Services.ContentCreatorV2.Write;
using GeekAPI.Services.Rag;
using GeekAPI.Services.Workflow.Domain.Entities;

namespace GeekAPI.Services.ContentCreatorV2.Generation;

/// <summary>
/// Interim partner-mention coverage gate (master-plan P1 / R8): when scanned body prose
/// mentions a named partner token, the same section needs ≥1 verified partner citation.
/// Not claim-level / sentence-level NLP.
/// </summary>
public static class GccV2PartnerMentionGate
{
    private static readonly Regex BareUrlPattern = new(
        @"https?://[^\s\]\)""']+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public sealed record PartnerToken(string DisplayLabel, IReadOnlyList<string> NormalizedForms);

    public static IReadOnlyList<PartnerToken> CollectPartnerTokens(
        IReadOnlyList<string>? operatorTools,
        string? rawBriefJson)
    {
        var tokens = new List<PartnerToken>();
        var formToIndex = new Dictionary<string, int>(StringComparer.Ordinal);

        void Add(string? label, IEnumerable<string>? aliases = null)
        {
            var display = (label ?? "").Trim();
            if (display.Length == 0) return;
            if (display.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || display.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return;

            var forms = new HashSet<string>(StringComparer.Ordinal) { Normalize(display) };
            if (aliases is not null)
            {
                foreach (var alias in aliases)
                {
                    var a = (alias ?? "").Trim();
                    if (a.Length == 0) continue;
                    forms.Add(Normalize(a));
                }
            }

            forms.RemoveWhere(f => f.Length == 0);
            if (forms.Count == 0) return;

            int? existingIdx = null;
            foreach (var form in forms)
            {
                if (formToIndex.TryGetValue(form, out var idx))
                {
                    existingIdx = idx;
                    break;
                }
            }

            if (existingIdx is int i)
            {
                var existing = tokens[i];
                var merged = existing.NormalizedForms
                    .Concat(forms)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                tokens[i] = existing with { NormalizedForms = merged };
                foreach (var form in merged)
                    formToIndex[form] = i;
                return;
            }

            var created = new PartnerToken(display, forms.ToList());
            var newIdx = tokens.Count;
            tokens.Add(created);
            foreach (var form in created.NormalizedForms)
                formToIndex[form] = newIdx;
        }

        if (operatorTools is not null)
        {
            foreach (var tool in operatorTools)
                Add(tool);
        }

        if (string.IsNullOrWhiteSpace(rawBriefJson))
            return tokens;

        try
        {
            using var doc = JsonDocument.Parse(rawBriefJson);
            if (doc.RootElement.TryGetProperty("hierarchyPlan", out var plan)
                && plan.ValueKind == JsonValueKind.Object
                && plan.TryGetProperty("recommendedTools", out var tools)
                && tools.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in tools.EnumerateArray())
                {
                    if (t.ValueKind != JsonValueKind.Object) continue;
                    if (!t.TryGetProperty("name", out var n) || n.ValueKind != JsonValueKind.String)
                        continue;
                    Add(n.GetString(), ReadAliases(t));
                }
            }

            if (doc.RootElement.TryGetProperty("operatorTools", out var operatorToolsEl)
                && operatorToolsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in operatorToolsEl.EnumerateArray())
                {
                    if (t.ValueKind == JsonValueKind.String)
                        Add(t.GetString());
                    else if (t.ValueKind == JsonValueKind.Object
                             && t.TryGetProperty("name", out var n)
                             && n.ValueKind == JsonValueKind.String)
                        Add(n.GetString(), ReadAliases(t));
                }
            }
        }
        catch (JsonException)
        {
            // Brief parse failure → no additional tokens; operatorTools still apply.
        }

        return tokens;
    }

    public static IReadOnlyList<string> CollectGaps(
        GccV2WriteOutput output,
        IReadOnlyList<PartnerToken> partnerTokens)
    {
        if (partnerTokens.Count == 0) return [];

        var gaps = new List<string>();
        foreach (var section in output.AllSections)
        {
            if (section.Section is null) continue;
            var scan = BuildScanText(section.Section, section.Citations);
            if (string.IsNullOrWhiteSpace(scan)) continue;

            var hit = FindMention(scan, partnerTokens);
            if (hit is null) continue;

            var citations = section.Citations ?? [];
            var hasVerifiedPartner = citations.Any(c =>
                c.Verified == true
                && string.Equals((c.CrawlType ?? "").Trim(), "partner", StringComparison.OrdinalIgnoreCase));
            if (hasVerifiedPartner) continue;

            gaps.Add(
                $"partner-mention '{hit.DisplayLabel}' on section '{section.SectionKey}' lacks verified partner citation");
        }

        return gaps.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>Visible for unit tests — NFC + Unicode case-fold + whitespace collapse.</summary>
    public static string Normalize(string value)
    {
        var nfc = (value ?? "").Normalize(NormalizationForm.FormC);
        var folded = nfc.ToLowerInvariant();
        var sb = new StringBuilder(folded.Length);
        var pendingSpace = false;
        foreach (var ch in folded)
        {
            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }

            sb.Append(ch);
        }

        return sb.ToString().Trim();
    }

    internal static string BuildScanText(Section section, IReadOnlyList<RagCitationDto>? citations)
    {
        var parts = new List<string>();
        CollectScanParts(section, parts);
        var joined = string.Join("\n", parts.Where(p => p.Length > 0));
        if (citations is not null)
        {
            foreach (var citation in citations)
            {
                if (string.IsNullOrWhiteSpace(citation.Quote)) continue;
                joined = joined.Replace(citation.Quote, " ", StringComparison.Ordinal);
            }
        }

        joined = BareUrlPattern.Replace(joined, " ");
        return Normalize(joined);
    }

    internal static PartnerToken? FindMention(string normalizedScan, IReadOnlyList<PartnerToken> tokens)
    {
        foreach (var token in tokens)
        {
            foreach (var form in token.NormalizedForms)
            {
                if (form.Length == 0) continue;
                if (ContainsWholeToken(normalizedScan, form))
                    return token;
            }
        }

        return null;
    }

    internal static bool ContainsWholeToken(string haystackNormalized, string needleNormalized)
    {
        if (needleNormalized.Length == 0 || haystackNormalized.Length == 0) return false;

        if (IsPunctuationOrSymbolOnly(needleNormalized))
            return haystackNormalized.Contains(needleNormalized, StringComparison.Ordinal);

        var start = 0;
        while (start <= haystackNormalized.Length - needleNormalized.Length)
        {
            var idx = haystackNormalized.IndexOf(needleNormalized, start, StringComparison.Ordinal);
            if (idx < 0) return false;

            var beforeOk = idx == 0 || !IsTokenChar(haystackNormalized[idx - 1]);
            var afterIdx = idx + needleNormalized.Length;
            var afterOk = afterIdx >= haystackNormalized.Length || !IsTokenChar(haystackNormalized[afterIdx]);
            if (beforeOk && afterOk) return true;
            start = idx + 1;
        }

        return false;
    }

    private static void CollectScanParts(Section section, List<string> parts)
    {
        if (!string.IsNullOrWhiteSpace(section.Heading))
            parts.Add(section.Heading);

        foreach (var paragraph in section.Paragraphs)
        {
            switch (paragraph)
            {
                case TextParagraph text:
                    parts.Add(string.Join(" ", text.Runs.Select(r => r.Text)));
                    break;
                case ListParagraph list:
                    foreach (var item in list.Items)
                        parts.Add(string.Join(" ", item.Select(r => r.Text)));
                    break;
            }
        }

        foreach (var child in section.Children)
            CollectScanParts(child, parts);
    }

    private static IEnumerable<string>? ReadAliases(JsonElement tool)
    {
        if (tool.TryGetProperty("aliases", out var aliases) && aliases.ValueKind == JsonValueKind.Array)
        {
            return aliases.EnumerateArray()
                .Where(a => a.ValueKind == JsonValueKind.String)
                .Select(a => a.GetString() ?? "");
        }

        if (tool.TryGetProperty("alias", out var alias) && alias.ValueKind == JsonValueKind.String)
            return [alias.GetString() ?? ""];

        return null;
    }

    private static bool IsPunctuationOrSymbolOnly(string normalized)
    {
        foreach (var ch in normalized)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat is UnicodeCategory.UppercaseLetter
                or UnicodeCategory.LowercaseLetter
                or UnicodeCategory.TitlecaseLetter
                or UnicodeCategory.ModifierLetter
                or UnicodeCategory.OtherLetter
                or UnicodeCategory.DecimalDigitNumber
                or UnicodeCategory.LetterNumber
                or UnicodeCategory.OtherNumber)
                return false;
        }

        return normalized.Length > 0;
    }

    private static bool IsTokenChar(char ch)
    {
        if (ch is '_' or '-') return true;
        var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
        return cat is UnicodeCategory.UppercaseLetter
            or UnicodeCategory.LowercaseLetter
            or UnicodeCategory.TitlecaseLetter
            or UnicodeCategory.ModifierLetter
            or UnicodeCategory.OtherLetter
            or UnicodeCategory.DecimalDigitNumber
            or UnicodeCategory.LetterNumber
            or UnicodeCategory.OtherNumber;
    }
}
