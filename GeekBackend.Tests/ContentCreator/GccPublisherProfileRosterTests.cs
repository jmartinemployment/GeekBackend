using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreator;
using GeekApplication.Models.ContentCreator;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// The home page's own roster of tools is not read by the writer (Jeff, 2026-10-07: "You may remove the
/// home page reference to Top Tools for X, as they are now entered in the brief form"). The 2026-10-06
/// run named Tipalti, a partner on another project, because the site's roster was printed beside the
/// rule not to name it.
/// </summary>
public sealed class GccPublisherProfileRosterTests
{
    private static readonly Guid RunId = Guid.NewGuid();

    private static async Task<GccPublisherProfileResolver.PublisherProfile> Resolve(string blocksJson)
    {
        var projectId = Guid.NewGuid();
        var project = new GccProjectDto(
            projectId, Guid.NewGuid(), "Acme", null, null, "active", "https://geekatyourspot.com", RunId, "accounting",
            [], [], DateOnly.FromDateTime(DateTime.UtcNow), null, null, null, null, null, DateTime.UtcNow, DateTime.UtcNow);
        var home = new GeekCrawlerPageDto(
            Guid.NewGuid(), RunId, "https://geekatyourspot.com", "https://geekatyourspot.com", "https://geekatyourspot.com",
            200, true, null, null, DateTimeOffset.UtcNow, Blocks: JsonDocument.Parse(blocksJson).RootElement.Clone());
        var resolver = new GccPublisherProfileResolver(
            new GccCompetitorAnalysisResolverTests.FakeProjects(project),
            new GccCompetitorAnalysisResolverTests.FakePages([home]),
            NullLogger<GccPublisherProfileResolver>.Instance);
        return await resolver.ResolveAsync(projectId);
    }

    [Fact]
    public async Task The_roster_label_and_the_names_under_it_are_not_read_and_the_rest_of_the_page_is()
    {
        var profile = await Resolve("""
            [
              {"type":"heading","level":1,"text":"AI implementation for small businesses"},
              {"type":"paragraph","text":"We work through a four-phase methodology."},
              {"type":"paragraph","text":"Top 5 Automated Approval Workflow Tools:"},
              {"type":"paragraph","text":"Tipalti, ApprovalMax, Ramp, Bill, Stampli"},
              {"type":"heading","level":2,"text":"The Methodology"},
              {"type":"paragraph","text":"Discover, define, build, support."}
            ]
            """);

        Assert.Equal(["AI implementation for small businesses", "The Methodology"], profile.Headings);
        Assert.Equal(
            ["We work through a four-phase methodology.", "Discover, define, build, support."],
            profile.Paragraphs);
        var everything = string.Join(' ', profile.Headings.Concat(profile.Paragraphs));
        Assert.DoesNotContain("Tipalti", everything, StringComparison.Ordinal);
        Assert.DoesNotContain("Top 5", everything, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_roster_that_is_a_heading_with_a_list_under_it_goes_up_to_the_next_heading()
    {
        var profile = await Resolve("""
            [
              {"type":"heading","level":2,"text":"Top Tools for Automated Approval Workflows"},
              {"type":"listItem","text":"Tipalti"},
              {"type":"listItem","text":"Ramp"},
              {"type":"heading","level":2,"text":"Free consultation"},
              {"type":"paragraph","text":"Book a call."}
            ]
            """);

        Assert.Equal(["Free consultation"], profile.Headings);
        Assert.Equal(["Book a call."], profile.Paragraphs);
    }

    [Fact]
    public async Task A_long_sentence_that_happens_to_say_best_and_tools_is_prose_and_is_kept()
    {
        var sentence = "Our clients tell us the best thing about working with us is that the AI tools we set up "
            + "are chosen for their business and explained in plain language, so nobody is left guessing what runs where.";
        Assert.True(sentence.Length > 120);

        var profile = await Resolve($$"""
            [
              {"type":"heading","level":2,"text":"Why clients stay"},
              {"type":"paragraph","text":"{{sentence}}"},
              {"type":"paragraph","text":"Another line of the page."}
            ]
            """);

        Assert.Equal([sentence, "Another line of the page."], profile.Paragraphs);
    }

    [Fact]
    public async Task A_page_with_no_roster_is_read_exactly_as_it_was()
    {
        var profile = await Resolve("""
            [
              {"type":"heading","level":1,"text":"Geek @ Your Spot"},
              {"type":"paragraph","text":"An AI implementation consultancy."},
              {"type":"listItem","text":"West Palm Beach"}
            ]
            """);

        Assert.Equal(["Geek @ Your Spot"], profile.Headings);
        Assert.Equal(["An AI implementation consultancy.", "West Palm Beach"], profile.Paragraphs);
    }
}
