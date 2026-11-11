using System.Globalization;
using System.Text;
using GeekApplication.Models.Crm;

namespace GeekAPI.Services.Crm;

/// <summary>
/// An Apollo "Export contacts" CSV to <see cref="ImportProspect"/> rows. Columns are found by their
/// header name, so Apollo adding or reordering columns does not break the import; a missing column
/// is simply empty.
/// </summary>
public static class ApolloCsv
{
    public static IReadOnlyList<ImportProspect> Parse(string csv)
    {
        var rows = Csv.Read(csv);
        if (rows.Count < 2) return [];
        var header = rows[0].Select((name, i) => (name: name.Trim().TrimStart('﻿'), i))
            .GroupBy(x => x.name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().i, StringComparer.OrdinalIgnoreCase);

        var prospects = new List<ImportProspect>();
        foreach (var row in rows.Skip(1))
        {
            if (row.All(string.IsNullOrWhiteSpace)) continue;
            string? Get(string column) =>
                header.TryGetValue(column, out var i) && i < row.Count && !string.IsNullOrWhiteSpace(row[i]) ? row[i].Trim() : null;

            var phones = new List<ImportPhone>();
            void AddPhone(string column, string label)
            {
                if (CrmText.ToE164(Get(column)) is { } e164 && phones.All(p => p.E164 != e164))
                    phones.Add(new ImportPhone(e164, label));
            }
            AddPhone("Mobile Phone", ContactPointLabels.Mobile);
            AddPhone("Work Direct Phone", ContactPointLabels.Work);
            AddPhone("Home Phone", ContactPointLabels.Home);
            AddPhone("Other Phone", ContactPointLabels.Other);

            var (line1, postal) = SplitCompanyAddress(Get("Company Address"));

            prospects.Add(new ImportProspect(
                FirstName: Get("First Name"),
                LastName: Get("Last Name"),
                JobTitle: Get("Title"),
                Email: Get("Email"),
                Phones: phones,
                City: Get("City"),
                Region: Get("State"),
                Country: Get("Country"),
                LinkedInUrl: Get("Person Linkedin Url"),
                CompanyName: Get("Company Name"),
                CompanyWebsite: Get("Website"),
                CompanyPhoneE164: CrmText.ToE164(Get("Company Phone")) ?? CrmText.ToE164(Get("Corporate Phone")),
                CompanyAddressLine1: line1,
                CompanyCity: Get("Company City"),
                CompanyRegion: Get("Company State"),
                CompanyPostalCode: postal,
                CompanyCountry: Get("Company Country"),
                IndustryLabel: Get("Industry"),
                NaicsCode: Get("NAICS Codes"),
                SicCode: Get("SIC Codes"),
                EmployeeCount: int.TryParse(Get("# Employees"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null,
                AnnualRevenueBand: RevenueBand(Get("Annual Revenue")),
                PersonRoleAtCompany: Get("Seniority"),
                DoNotCall: string.Equals(Get("Do Not Call"), "true", StringComparison.OrdinalIgnoreCase),
                ExternalId: Get("Apollo Contact Id")));
        }
        return prospects;
    }

    /// <summary>"20423 State Road 7, Boca Raton, Florida, United States, 33498" to ("20423 State Road 7", "33498").</summary>
    internal static (string? Line1, string? Postal) SplitCompanyAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return (null, null);
        var parts = address.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var postal = parts.LastOrDefault(p => p.Length is >= 5 and <= 10 && p.Count(char.IsDigit) >= 5);
        var line1 = parts.Length > 1 && parts[0].Any(char.IsDigit) ? parts[0] : null;
        return (line1, postal);
    }

    /// <summary>"1500000" or "1.5M" to a revenue band.</summary>
    internal static string? RevenueBand(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim().Replace("$", "", StringComparison.Ordinal).Replace(",", "", StringComparison.Ordinal).ToUpperInvariant();
        var multiplier = 1m;
        if (s.EndsWith('K')) { multiplier = 1_000m; s = s[..^1]; }
        else if (s.EndsWith('M')) { multiplier = 1_000_000m; s = s[..^1]; }
        else if (s.EndsWith('B')) { multiplier = 1_000_000_000m; s = s[..^1]; }
        if (!decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)) return null;
        value *= multiplier;
        return value switch
        {
            < 250_000m => "under_250k",
            < 1_000_000m => "250k_1m",
            < 5_000_000m => "1m_5m",
            < 25_000_000m => "5m_25m",
            _ => "over_25m",
        };
    }
}

/// <summary>Minimal RFC 4180 reader: quoted fields, doubled quotes, commas and newlines inside quotes.</summary>
internal static class Csv
{
    public static List<List<string>> Read(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
                continue;
            }

            switch (c)
            {
                case '"': inQuotes = true; break;
                case ',': row.Add(field.ToString()); field.Clear(); break;
                case '\r': break;
                case '\n':
                    row.Add(field.ToString()); field.Clear();
                    rows.Add(row); row = new List<string>();
                    break;
                default: field.Append(c); break;
            }
        }
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }
        return rows;
    }
}
