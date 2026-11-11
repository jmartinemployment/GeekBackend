using System.Globalization;
using System.Text.RegularExpressions;

namespace GeekBackend.Tests.Crm;

/// <summary>
/// 0052_geek_crm_naics_load.sql against the Census Bureau's 2022 NAICS list it was generated from:
/// 2,125 codes, split 20 / 96 / 308 / 689 / 1,012 by level, every code in the form the XSD allows,
/// no code twice, every title present, and the load idempotent.
/// </summary>
public class NaicsLoadTests
{
    private static readonly string SqlPath = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..",
        "GeekRepository", "Migrations", "Sql", "0052_geek_crm_naics_load.sql"));

    private static readonly Regex Row = new(@"^\s+\('(?<code>[^']+)', '(?<title>(?:[^']|'')+)', (?<level>\d)\),?$", RegexOptions.Multiline);

    private static List<(string Code, string Title, int Level)> Rows() =>
        Row.Matches(File.ReadAllText(SqlPath))
            .Select(m => (m.Groups["code"].Value, m.Groups["title"].Value.Replace("''", "'"), int.Parse(m.Groups["level"].Value, CultureInfo.InvariantCulture)))
            .ToList();

    [Fact]
    public void The_script_loads_every_code_of_the_census_2022_list_and_no_other()
    {
        var rows = Rows();
        Assert.Equal(2125, rows.Count);
        Assert.Equal(rows.Count, rows.Select(r => r.Code).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal([(2, 20), (3, 96), (4, 308), (5, 689), (6, 1012)],
            rows.GroupBy(r => r.Level).OrderBy(g => g.Key).Select(g => (g.Key, g.Count())).ToArray());
    }

    [Fact]
    public void Every_code_has_the_form_the_xsd_allows_a_title_and_the_level_its_digits_say()
    {
        var pattern = new Regex(@"^(?:[0-9]{2}-[0-9]{2}|[0-9]{2,6})$");
        foreach (var (code, title, level) in Rows())
        {
            Assert.Matches(pattern, code);
            Assert.False(string.IsNullOrWhiteSpace(title), $"{code} has no title.");
            Assert.Equal(code.Contains('-', StringComparison.Ordinal) ? 2 : code.Length, level);
        }
        Assert.Equal(["31-33", "44-45", "48-49"], Rows().Select(r => r.Code).Where(c => c.Contains('-', StringComparison.Ordinal)).ToArray());
    }

    [Fact]
    public void The_load_is_idempotent_and_names_its_source()
    {
        var sql = File.ReadAllText(SqlPath);
        Assert.Contains("ON CONFLICT (code) DO UPDATE SET title = EXCLUDED.title, level = EXCLUDED.level;", sql, StringComparison.Ordinal);
        Assert.Contains("https://www.census.gov/naics/2022NAICS/2-6%20digit_2022_Codes.xlsx", sql, StringComparison.Ordinal);
    }
}
