using System.Text.RegularExpressions;

namespace GeekBackend.Tests;

/// <summary>
/// Railway Postgres is for OAuth state and nothing else. Jeff, 2026-09-29:
/// <i>"POSTGRES ON RAILWAY IS ONLY FOR OAUTH USE, PERIOD"</i>, and
/// <i>"NO POSSIBLITY OF RAILWAY POSTGRES USE BEING RESTORED"</i>.
///
/// <para>
/// That second requirement is why this file exists. Deleting code does not prevent it coming back,
/// and neither does rewriting git history — the objects survive in any clone, fork or reflog. What
/// does prevent it is a test that fails the build when the removed surface reappears, which is the
/// same mechanism <c>GccV2FallbackCorrectnessTests</c> uses to keep RAG generate from being revived.
/// </para>
///
/// <para>
/// These assertions sweep <b>every</b> <c>.cs</c> and <c>.sql</c> file in the solution rather than a
/// list of known paths, so the guard cannot be sidestepped by putting the code in a new file. Each
/// covers a removal that has actually happened; a rule asserted here with live code contradicting it
/// would be the defect <c>AGENTS.md</c> forbids, so **do not add an assertion for the seven non-auth
/// EF contexts until they are gone** — that work is listed in <c>AGENTS.md</c> § "What Postgres is
/// for" as outstanding, not done.
/// </para>
/// </summary>
public class PostgresIsOAuthOnlyTests
{
    private static string SolutionRoot => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", ".."));

    private static IEnumerable<string> SourceFiles(params string[] extensions)
    {
        foreach (var ext in extensions)
        {
            foreach (var path in Directory.EnumerateFiles(SolutionRoot, ext, SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(SolutionRoot, path).Replace('\\', '/');
                // Build output and this guard itself, which necessarily names what it forbids.
                if (rel.Contains("/obj/", StringComparison.Ordinal)
                    || rel.Contains("/bin/", StringComparison.Ordinal)
                    || rel.StartsWith("obj/", StringComparison.Ordinal)
                    || rel.StartsWith("bin/", StringComparison.Ordinal)
                    // Migrations name a table in order to create or drop it, and EF's .Designer and
                    // ModelSnapshot files are generated records of models as they were. A drop
                    // migration has to say what it drops, so banning the name here would forbid the
                    // removal itself. What matters is live code.
                    || rel.Contains("/Migrations/", StringComparison.Ordinal)
                    // This guard necessarily names everything it forbids.
                    || rel.EndsWith("PostgresIsOAuthOnlyTests.cs", StringComparison.Ordinal))
                {
                    continue;
                }

                yield return rel;
            }
        }
    }

    private static void AssertAbsent(string needle, string why, params string[] extensions)
    {
        var offenders = new List<string>();
        foreach (var rel in SourceFiles(extensions))
        {
            if (File.ReadAllText(Path.Combine(SolutionRoot, rel))
                .Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                offenders.Add(rel);
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"\"{needle}\" is back in {offenders.Count} file(s): {string.Join(", ", offenders.Take(10))}."
            + Environment.NewLine + why);
    }

    [Fact]
    public void Crawl_data_never_reaches_Postgres()
    {
        // The geek_crawler schema and its EF layer were removed in 6662184. It had survived three
        // documents calling it dead -- AGENTS.md, GeekCrawlerDbContext's own comments, and
        // GeekCrawlerIngestLimits -- while Program.cs registered the context against Npgsql,
        // migrated the schema on every boot, and ran a backfill that read and wrote crawl_runs.
        // Not the bare name: "geek_crawler" is also the Mongo DATABASE, named legitimately by
        // MongoGeekCrawlerService, GeekCrawlerOptions and HttpGeekCrawlerRagClient. What is
        // forbidden is the Postgres SCHEMA, which only appears in these two spellings.
        AssertAbsent("HasDefaultSchema(\"geek_crawler\")", "Mongo is the crawl store. A Postgres "
            + "schema for crawl data is what was removed; see AGENTS.md § \"What Postgres is for\".",
            "*.cs");
        AssertAbsent("schema: \"geek_crawler\"", "Same: no Postgres table for crawl data.", "*.cs");
        AssertAbsent("geek_crawler.crawl_", "No SQL against a Postgres crawl table.", "*.cs", "*.sql");
        AssertAbsent("CREATE SCHEMA IF NOT EXISTS geek_crawler", "No Postgres crawl schema.", "*.sql");

        AssertAbsent("GeekCrawlerDbContext", "Deleted in 6662184 together with its design-time "
            + "factory, GeekCrawlerSeedKeyBackfill, Migrations/GeekCrawler and three Program.cs "
            + "sites. SeedKey arrives with the create command; nothing needs a backfill.", "*.cs");
    }

    [Fact]
    public void Project_site_crawl_pages_are_not_a_Postgres_table()
    {
        // Raw page HTML in a text column, in GeekAPI's own store, for a crawl type the shared Mongo
        // store already supports as CrawlTypes.ProjectSite. The source's own comment said why it had
        // to go: "GeekAPI is a service layer and should not own a crawl corpus, least of all raw
        // page HTML in a text column."
        AssertAbsent("gcc_v2_project_site_crawl", "Project-site crawls belong in the shared "
            + "geek_crawler Mongo store like every other crawl type.", "*.cs", "*.sql");

        AssertAbsent("GccV2PostgresProjectSitePageSource", "The retired read path. Mongo is the only "
            + "source; GccV2MongoProjectSitePageSource returns a strict superset of the same DTO.",
            "*.cs");
    }

    [Fact]
    public void The_glossary_has_no_Postgres_surface()
    {
        // Removed in 48c9f0d, schema dropped by 0040. Source of record is the site repo's
        // src/data/glossary/terms.ts: "Editing a term is a code change and a deploy, not a database
        // update." 0040 names the schema in order to drop it, so it is allowed to.
        var offenders = SourceFiles("*.cs", "*.sql")
            .Where(rel => !rel.EndsWith("0040_drop_geek_glossary.sql", StringComparison.Ordinal))
            .Where(rel => File.ReadAllText(Path.Combine(SolutionRoot, rel))
                .Contains("geek_glossary", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"geek_glossary is back in: {string.Join(", ", offenders)}. The glossary is served from "
            + "the site repo's terms.ts, not from a database.");

        AssertAbsent("IGlossaryRepository", "The whole read/write chain went in 48c9f0d.", "*.cs");
    }

    [Fact]
    public void GeekAPI_cannot_reach_Postgres_at_all()
    {
        // Jeff, 2026-09-29: "Postgres is never called directly, all calls go throught Geek-API ->
        // Geek-Repository -> Supabase", then "UNAUTHORIZED DIRECT CALLS TO SUPABASE ARE TO BE
        // DELETED". GeekAPI had three of them, plus a shared library carrying two more: a Dapper
        // SELECT against sa2.site_profiles behind api/seo/internal/site-profiles, and two services
        // opening their own connections to LISTEN. It also held two Postgres credentials of its own,
        // SITE_ANALYZER2_DATABASE_URL and GCC_V2_LISTEN_DATABASE_URL.
        //
        // This asserts on the PROJECT FILE rather than on source, because that is the level where
        // the capability lives: with no Npgsql, no Dapper and no GeekSa2Read reference, code that
        // opens a connection does not compile. A source-level ban can be worked around by a new
        // file; this cannot be worked around without editing the csproj, which is the point.
        var csproj = File.ReadAllText(Path.Combine(SolutionRoot, "GeekAPI", "GeekAPI.csproj"));

        foreach (var forbidden in new[] { "Npgsql", "Dapper", "GeekSa2Read" })
        {
            Assert.False(
                csproj.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                $"GeekAPI.csproj references {forbidden}. GeekAPI is the gateway, not a data plane: "
                + "every Postgres read and write goes GeekAPI -> GeekRepository -> Supabase. "
                + "If you need data here, add a route in GeekRepository and call it over the "
                + "existing named HttpClient(\"GeekRepository\").");
        }

        // And the two credentials it used to hold. GeekRepository may name its own; GeekAPI may not.
        foreach (var rel in SourceFiles("*.cs").Where(r => r.StartsWith("GeekAPI/", StringComparison.Ordinal)))
        {
            var source = File.ReadAllText(Path.Combine(SolutionRoot, rel));
            Assert.False(
                source.Contains("SITE_ANALYZER2_DATABASE_URL", StringComparison.Ordinal)
                || source.Contains("GCC_V2_LISTEN_DATABASE_URL", StringComparison.Ordinal),
                $"{rel} names a Postgres connection string. GeekAPI holds no database credential.");
        }
    }

    [Fact]
    public void RAG_generate_stays_removed()
    {
        // RAG is Library-only. Asserted here as well as in GccV2FallbackCorrectnessTests because
        // that test names specific files; this sweeps the tree, so a new caller cannot slip in.
        // Prohibition strings are allowed -- what is forbidden is a call.
        var offenders = new List<string>();
        var call = new Regex(@"(PostAsJsonAsync|PostAsync|GetAsync|SendAsync)[^;\r\n]{0,200}v1/generate",
            RegexOptions.IgnoreCase);
        foreach (var rel in SourceFiles("*.cs"))
        {
            if (call.IsMatch(File.ReadAllText(Path.Combine(SolutionRoot, rel))))
                offenders.Add(rel);
        }

        Assert.True(
            offenders.Count == 0,
            $"Something calls RAG /v1/generate: {string.Join(", ", offenders)}. RAG retrieves and "
            + "verifies; GeekAPI drafts. POST /v1/generate was removed and must never be revived.");
    }
}
