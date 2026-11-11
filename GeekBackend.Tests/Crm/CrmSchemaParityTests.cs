using System.Text.RegularExpressions;
using GeekApplication.Models.Crm;

namespace GeekBackend.Tests.Crm;

/// <summary>
/// What 0050_geek_crm_schema.sql seeds. The CHECK lists, columns and mapping are held to the
/// canonical XSD by SchemaContractTests (script equals XSD, XSD equals the C# vocabularies and the EF
/// mapping), so they are not repeated here.
/// </summary>
public class CrmSchemaParityTests
{
    private static readonly string SqlPath = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..",
        "GeekRepository", "Migrations", "Sql", "0050_geek_crm_schema.sql"));

    private static string Sql => Regex.Replace(File.ReadAllText(SqlPath), @"--[^\n]*", string.Empty);

    [Fact]
    public void The_seeded_tenant_is_the_one_the_code_defaults_to()
    {
        Assert.Contains($"'{CrmSeed.GeekAtYourSpotTenantId}', 'Geek @ Your Spot'", Sql);
    }

    [Fact]
    public void The_seeded_pipeline_has_the_agreed_stages_in_order()
    {
        var names = Regex.Matches(Sql, @"'[0-9a-f-]{36}',\s*'[0-9a-f-]{36}',\s*'[0-9a-f-]{36}',\s*'([^']+)',\s*(\d+),")
            .Select(m => (m.Groups[1].Value, int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)))
            .OrderBy(x => x.Item2).Select(x => x.Item1).ToArray();
        Assert.Equal(["New", "Qualified", "Meeting booked", "Proposal sent", "Won", "Lost"], names);
    }
}
