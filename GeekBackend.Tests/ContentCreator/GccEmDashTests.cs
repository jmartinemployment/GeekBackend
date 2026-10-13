using System.Text.Json;
using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.ContentCreator.Guardrail;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Services;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// No em dash in what a writer produces (Jeff, 2026-10-13: "a dead give away that writing is from AI").
/// Every writer is told, and what a reply carries anyway is taken out before the page is checked or
/// saved: a comma between words, nothing beside a mark that is already there. A quotation and a code
/// block keep theirs, being the source's words and not prose.
/// </summary>
public sealed class GccEmDashTests
{
    private const string D = "—";

    private static TextParagraph Text(params string[] runs) => new([.. runs.Select(r => new Run(r))]);

    private static Section H2(string heading, params Paragraph[] paragraphs) => new("h2", heading, paragraphs, null, []);

    [Theory]
    [InlineData("The close takes nine days — most of it waiting.", "The close takes nine days, most of it waiting.")]
    [InlineData("The close—most of it waiting—takes nine days.", "The close, most of it waiting, takes nine days.")]
    [InlineData("It failed. — Nobody noticed.", "It failed. Nobody noticed.")]
    [InlineData("Three things matter: — speed, cost and accuracy.", "Three things matter: speed, cost and accuracy.")]
    [InlineData("It ran late —, as usual.", "It ran late, as usual.")]
    [InlineData("The tool (— the new one) runs nightly.", "The tool (the new one) runs nightly.")]
    [InlineData("— A line that opens with one.", "A line that opens with one.")]
    [InlineData("Payroll Tax — What It Costs", "Payroll Tax, What It Costs")]
    public void A_dash_between_words_becomes_a_comma_and_one_beside_a_mark_is_dropped(string written, string saved)
    {
        var (text, removed) = GccEmDash.Clean(written);

        Assert.Equal(saved, text);
        Assert.Equal(written.Count(c => c == GccEmDash.Dash), removed);
        Assert.DoesNotContain(GccEmDash.Dash, text);
    }

    [Theory]
    [InlineData("A range of 5–7 days and a well-known tool.")]
    [InlineData("")]
    [InlineData("Nothing to change here.")]
    public void A_text_with_none_comes_back_as_it_was(string written)
    {
        var (text, removed) = GccEmDash.Clean(written);

        Assert.Equal(written, text);
        Assert.Equal(0, removed);
    }

    [Fact]
    public void A_page_keeps_none_in_a_heading_a_paragraph_a_list_or_a_definition()
    {
        var page = new ContentDocument(
            H2($"The opening {D} and why", Text($"Late invoices cost money {D} every month.")),
            [
                H2(
                    "A section",
                    Text("The bold part", $" {D} and what follows it."),
                    new ListParagraph(false, [[new Run($"First {D} the obvious one.")], [new Run("Second.")]]),
                    new DefinitionParagraph([new DefinitionItem([new Run($"DSO {D} defined")], [new Run($"Days {D} on average.")])])),
                new Section("h2", "A parent", [Text("Prose.")], null, [H2($"A child {D} deeper", Text($"Nested {D} text."))]),
            ]);

        var (cleaned, removed) = GccEmDash.Clean(page);

        Assert.Equal(8, removed);
        Assert.DoesNotContain(GccEmDash.Dash, JsonSerializer.Serialize(cleaned, GccDocumentJson.Options));
        Assert.Equal("The opening, and why", cleaned.Lede.Heading);
        // A run that follows another keeps its own leading space's job: the comma joins it to the words before.
        var joined = string.Concat(((TextParagraph)cleaned.Sections[0].Paragraphs[0]).Runs.Select(r => r.Text));
        Assert.Equal("The bold part, and what follows it.", joined);
    }

    [Fact]
    public void A_quotation_and_a_code_block_keep_theirs()
    {
        var quote = new QuoteParagraph([new Run($"We closed in two days {D} not nine.")], "https://partner.test/page");
        var code = new CodeParagraph($"total = a {D} b");
        var page = new ContentDocument(H2("The opening", Text("Prose.")), [H2("A section", quote, code)]);

        var (cleaned, removed) = GccEmDash.Clean(page);

        Assert.Equal(0, removed);
        Assert.Same(page, cleaned);
    }

    [Fact]
    public void A_formatted_or_linked_run_keeps_its_formatting_and_its_link()
    {
        var run = new Run($"the scheduler {D} here", Bold: true, Href: "#consultationAppointment2xl");
        var page = new ContentDocument(H2("The opening", new TextParagraph([run])), []);

        var cleaned = (TextParagraph)GccEmDash.Clean(page).Document.Lede.Paragraphs[0];

        Assert.Equal(run with { Text = "the scheduler, here" }, cleaned.Runs[0]);
    }

    /// <summary>
    /// Every string of every reply record a page's title and summaries come back in, found by
    /// reflection: each is filled with a dash, and none comes back with one. A field added to one of
    /// these records later is covered without a line anywhere.
    /// </summary>
    [Fact]
    public void Every_string_of_a_title_and_summaries_reply_is_read()
    {
        var article = new ArticleMetadataDraft($"A {D} title", $"A {D} description", [$"a {D} keyword"], [$"A {D} heading"], $"A {D} standfirst");
        var blog = new BlogMetadataDraft($"A {D} title", $"A {D} description", [$"a {D} keyword"], [$"A {D} heading"], $"A {D} standfirst");
        var tool = new ToolMetadataDraft($"a {D} b", $"a {D} b", $"a {D} b", $"a {D} b", $"a {D} b", $"a {D} b", $"a {D} b", $"a {D} b", $"a {D} b");

        var (cleanArticle, articleRemoved) = GccEmDash.Strings(article);
        var (cleanBlog, blogRemoved) = GccEmDash.Strings(blog);
        var (cleanTool, toolRemoved) = GccEmDash.Strings(tool);

        Assert.Equal(5, articleRemoved);
        Assert.Equal(5, blogRemoved);
        Assert.Equal(9, toolRemoved);
        Assert.DoesNotContain(GccEmDash.Dash, JsonSerializer.Serialize(cleanArticle));
        Assert.DoesNotContain(GccEmDash.Dash, JsonSerializer.Serialize(cleanBlog));
        Assert.DoesNotContain(GccEmDash.Dash, JsonSerializer.Serialize(cleanTool));
        Assert.Equal("A, title", cleanArticle.Title);
        // The reply that was parsed is left as it came.
        Assert.Equal($"A {D} title", article.Title);
    }

    [Fact]
    public void A_reply_with_none_is_the_same_reply()
    {
        var blog = new BlogMetadataDraft("A title", "A description", ["k"], ["A heading"], "A standfirst");

        var (cleaned, removed) = GccEmDash.Strings(blog);

        Assert.Equal(0, removed);
        Assert.Same(blog, cleaned);
    }

    [Fact]
    public void The_voice_every_channel_shares_states_the_rule_and_uses_none_itself()
    {
        foreach (var voice in new[] { BrandTones.ForWebpages(), BrandTones.ForEmail(), BrandTones.ForLinkedIn(), BrandTones.ForFacebook() })
        {
            Assert.Contains(GccEmDash.Instruction, voice, StringComparison.Ordinal);
            Assert.DoesNotContain(GccEmDash.Dash, voice.Replace(GccEmDash.Instruction, string.Empty, StringComparison.Ordinal));
        }
    }
}
