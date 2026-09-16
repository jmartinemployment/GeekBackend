using System.Reflection;

namespace GeekBackend.Tests.ContentCreatorV2;

/// <summary>
/// Every content type must have exactly one producer that owns it.
///
/// Before this roster there was a single producer, `writing`, for all seventeen types. Grid and Canvas
/// Projects existed largely to compensate — batching to get volume, and filing by a deliverable type
/// nothing actually produced (plans/agent-specialists.md §0).
/// </summary>
public sealed class GccV2SpecialistRosterTests
{
    private static readonly string[] ContentTypes =
        ["blog", "pillar", "tool", "comparison", "case-study", "guide", "alternatives", "tech-article",
            "listicle", "service", "local", "whitepaper", "email", "social", "image-prompt", "ads",
            "linkedin-document"];

    private static IReadOnlyList<(string Slug, IReadOnlyList<string> Types, string Role, string Instructions)> Roster()
    {
        var seeder = typeof(GeekAPI.Services.ContentCreatorV2.Generation.GccV2FirstPartyAgentSeeder);
        var definitions = seeder.GetMethod("Definitions", BindingFlags.NonPublic | BindingFlags.Static)!;
        var rows = new List<(string, IReadOnlyList<string>, string, string)>();
        foreach (var item in (System.Collections.IEnumerable)definitions.Invoke(null, null)!)
        {
            var t = item.GetType();
            string Get(string n) => (string)t.GetProperty(n)!.GetValue(item)!;
            var types = (IReadOnlyList<string>)t.GetProperty("ContentTypes")!.GetValue(item)!;
            var participation = (System.Collections.IEnumerable)t.GetProperty("Participation")!.GetValue(item)!;
            var role = "contributor-reviewer";
            foreach (var p in participation)
            {
                var pr = (string)p.GetType().GetProperty("Role")!.GetValue(p)!;
                if (pr == "producer") { role = "producer"; break; }
            }
            rows.Add((Get("Slug"), types, role, Get("Instructions")));
        }
        return rows;
    }

    [Fact]
    public void Every_content_type_has_exactly_one_producer()
    {
        var producers = Roster().Where(x => x.Role == "producer").ToList();

        foreach (var type in ContentTypes)
        {
            var owners = producers.Where(p => p.Types.Contains(type)).Select(p => p.Slug).ToList();
            Assert.True(owners.Count == 1,
                $"'{type}' has {owners.Count} producers: {string.Join(", ", owners)}");
        }
    }

    [Fact]
    public void Short_form_producers_exist_and_are_not_long_form_agents()
    {
        var producers = Roster().Where(x => x.Role == "producer").ToList();

        foreach (var shortForm in new[] { "email", "social", "ads", "image-prompt" })
        {
            var owner = Assert.Single(producers.Where(p => p.Types.Contains(shortForm)));
            Assert.DoesNotContain("pillar", owner.Types);
            Assert.DoesNotContain("blog", owner.Types);
        }
    }

    /// <summary>Craft rules were folded in from the deleted skill package layer.</summary>
    [Fact]
    public void Producers_carry_their_craft_rules_inline()
    {
        foreach (var producer in Roster().Where(x => x.Role == "producer"))
        {
            Assert.Contains("never invent a citation", producer.Instructions,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Email_producer_is_told_not_to_write_a_trimmed_article()
    {
        var email = Assert.Single(Roster().Where(x => x.Types.Contains("email") && x.Role == "producer"));
        Assert.Contains("not a trimmed article", email.Instructions, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Claims_reviewer_owns_the_gates_the_rewrite_built()
    {
        var reviewer = Assert.Single(Roster().Where(x => x.Slug == "claims-disclosure"));

        Assert.Contains("absence of", reviewer.Instructions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("verified citation", reviewer.Instructions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("jurisdiction", reviewer.Instructions, StringComparison.OrdinalIgnoreCase);
    }
}
