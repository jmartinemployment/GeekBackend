using System.Text.Json.Nodes;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services;
using GeekAPI.Services.Workflow.Services.PromptBuilders;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// Bold and italic are not the writer's to set (Jeff, 2026-10-07: "That sounds acceptable"). Nothing in any prompt
/// asks for them, and in the 2026-10-07 run the writer copied a linked name's bold onto the runs around it: the
/// run before "BILL", the run before and the 50 words after "Ramp", and in the Blog's opening a 59-word run that
/// took the link too, which cost the page. The contract and the provider schema no longer offer them, and a reply
/// that carries them anyway has them dropped, by name, in the run's record. A link is untouched: every declared
/// partner tool is linked to its tool page, so <c>href</c> stays.
/// </summary>
public sealed class GccWriterFormattingTests
{
    private static readonly ArticleMetadataDraft Article = new("Title", "Meta", ["ai"], ["a", "b"]);
    private static readonly BlogMetadataDraft BlogMeta = new("Title", "Meta", ["ai"], ["a", "b"]);

    private static string Prompt(ChatCompletionRequest r) => string.Join("\n", r.Messages.Select(m => m.Content));

    // ---- what the writer is offered -------------------------------------------------------------

    public static TheoryData<string> EveryContract => new() { "articleLede", "blogLede", "blogBody" };

    [Theory]
    [MemberData(nameof(EveryContract))]
    public void The_run_the_writer_is_shown_has_text_and_a_link_and_nothing_to_format_with(string which)
    {
        var builder = new ContentPromptBuilder();
        var context = GccOpeningAsksNothingTests.Context();
        var request = which switch
        {
            "articleLede" => builder.BuildArticleLedePrompt(context, Article),
            "blogLede" => builder.BuildStandaloneBlogLedePrompt(context, BlogMeta),
            _ => builder.BuildStandaloneBlogBodyPrompt(context, BlogMeta),
        };

        var prompt = Prompt(request);

        Assert.Contains("\"href\": string?", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("\"bold\"", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("\"italic\"", prompt, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("section")]
    [InlineData("sections")]
    public void The_provider_schema_cannot_produce_bold_or_italic_and_still_requires_text_and_href_on_every_run(string which)
    {
        var json = which == "section" ? ContentSectionJsonSchema.SectionSchema : ContentSectionJsonSchema.SectionsArraySchema;
        var runs = RunObjects(JsonNode.Parse(json)!).ToList();

        Assert.NotEmpty(runs);
        foreach (var run in runs)
        {
            var properties = ((JsonObject)run["properties"]!).Select(p => p.Key).ToList();
            Assert.Equal(["text", "href"], properties);
            Assert.Equal(["text", "href"], ((JsonArray)run["required"]!).Select(n => n!.GetValue<string>()).ToList());
        }
    }

    /// <summary>Every schema object that describes a run: the one with a <c>text</c> and an <c>href</c> property.</summary>
    private static IEnumerable<JsonObject> RunObjects(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj["properties"] is JsonObject props && props.ContainsKey("text") && props.ContainsKey("href"))
                {
                    yield return obj;
                }

                foreach (var child in obj.Select(p => p.Value).Where(v => v is not null))
                {
                    foreach (var run in RunObjects(child!)) yield return run;
                }

                break;

            case JsonArray array:
                foreach (var item in array.Where(i => i is not null))
                {
                    foreach (var run in RunObjects(item!)) yield return run;
                }

                break;
        }
    }

    // ---- what is kept of a reply that carries them anyway ---------------------------------------

    private const string Formatted =
        """{"text":"Tools like ","bold":true,"italic":true},{"text":"Ramp","bold":true,"href":"https://geek.test/tools/ramp"},{"text":" cut the time.","italic":true}""";

    private static string SectionJson() =>
        $$"""{"tag":"h2","heading":"H","paragraphs":[{"type":"text","runs":[{{Formatted}}]},{"type":"list","ordered":false,"items":[[{"text":"one","bold":true}]]},{"type":"quote","runs":[{"text":"said","italic":true}],"cite":null,"candidate":null}],"children":[{"tag":"h3","heading":"C","paragraphs":[{"type":"text","runs":[{"text":"child","bold":true}]}],"children":[]}]}""";

    private static IEnumerable<Run> AllRuns(Section section) =>
        section.Paragraphs.SelectMany(p => p switch
        {
            TextParagraph t => t.Runs,
            ListParagraph l => l.Items.SelectMany(i => i),
            QuoteParagraph q => q.Runs,
            _ => [],
        }).Concat(section.Children.SelectMany(AllRuns));

    private static void AssertNoFormatting(Section section)
    {
        var runs = AllRuns(section).ToList();
        Assert.NotEmpty(runs);
        Assert.All(runs, r => { Assert.False(r.Bold); Assert.False(r.Italic); });
    }

    [Fact]
    public void A_section_reply_keeps_its_words_and_its_links_and_loses_only_the_formatting()
    {
        using var scope = JsonRepairTrace.Begin();

        var section = LlmResponseJsonParser.ParseSection(SectionJson(), "h2", "tool page 'Ramp' section");

        AssertNoFormatting(section);
        var runs = ((TextParagraph)section.Paragraphs[0]).Runs;
        Assert.Equal(["Tools like ", "Ramp", " cut the time."], runs.Select(r => r.Text));
        Assert.Equal("https://geek.test/tools/ramp", runs[1].Href);
        Assert.Null(runs[0].Href);
        var note = Assert.Single(scope.Drain());
        Assert.Equal("tool page 'Ramp' section", note.Label);
        Assert.Equal(["drop-writer-formatting"], note.Repairs);
    }

    [Fact]
    public void A_sections_reply_has_every_section_cleaned_and_is_noted_once()
    {
        using var scope = JsonRepairTrace.Begin();

        var sections = LlmResponseJsonParser.ParseSections($$"""{"sections":[{{SectionJson()}},{{SectionJson()}}]}""", "pillar body");

        Assert.Equal(2, sections.Count);
        Assert.All(sections, AssertNoFormatting);
        Assert.Equal(["drop-writer-formatting"], Assert.Single(scope.Drain()).Repairs);
    }

    [Fact]
    public void A_lede_reply_loses_the_bold_that_leaked_onto_its_neighbours_and_keeps_the_link_where_it_was_written()
    {
        // The Ramp opening of 2026-10-07: the run before the name and the 50 words after it came back bold.
        using var scope = JsonRepairTrace.Begin();
        var reply = $$"""{"ledeType":"anecdotal","heading":"H","paragraphs":[{"type":"text","runs":[{{Formatted}}]}]}""";

        var (lede, _) = LlmResponseJsonParser.ParseLede(reply, "tool page 'Ramp' lede");

        AssertNoFormatting(lede);
        Assert.Equal(["drop-writer-formatting"], Assert.Single(scope.Drain()).Repairs);
    }

    [Fact]
    public void The_blogs_opening_loses_its_bold_but_its_59_word_link_is_still_there_to_be_refused_by_the_link_check()
    {
        // Bold is not what cost the page: the link was. Dropping formatting does not hide it.
        const string reply =
            """{"ledeType":"directAddress","heading":"H","paragraphs":[{"type":"text","runs":[{"text":"For small businesses looking to implement artificial intelligence, automated approval workflows are a game-changer, as outlined on ","href":"https://geekatyourspot.com/","bold":true},{"text":"Geek @ Your Spot's","href":"https://geekatyourspot.com/","bold":true},{"text":" site."}]}]}""";

        var (lede, _) = LlmResponseJsonParser.ParseLede(reply, "blog lede");

        AssertNoFormatting(lede);
        var runs = ((TextParagraph)lede.Paragraphs[0]).Runs;
        Assert.Equal("https://geekatyourspot.com/", runs[0].Href);
        Assert.Equal("https://geekatyourspot.com/", runs[1].Href);
    }

    [Fact]
    public void A_lede_with_an_introduction_has_both_cleaned()
    {
        using var scope = JsonRepairTrace.Begin();
        const string reply =
            """{"lede":{"ledeType":"sceneSetting","heading":"H","paragraphs":[{"type":"text","runs":[{"text":"opening","bold":true}]}]},"introduction":{"paragraphs":[{"type":"text","runs":[{"text":"continues","italic":true}]}],"children":[]}}""";

        var (lede, _, introduction) = LlmResponseJsonParser.ParseLedeAndIntroduction(reply, "pillar lede");

        AssertNoFormatting(lede);
        AssertNoFormatting(introduction);
        Assert.Equal(["drop-writer-formatting"], Assert.Single(scope.Drain()).Repairs);
    }

    [Fact]
    public void A_reply_with_no_formatting_has_nothing_dropped_and_nothing_noted()
    {
        using var scope = JsonRepairTrace.Begin();

        LlmResponseJsonParser.ParseLede(
            """{"ledeType":"anecdotal","heading":"H","paragraphs":[{"type":"text","runs":[{"text":"plain"},{"text":"linked","href":"https://x.test/"}]}]}""",
            "lede");

        Assert.Empty(scope.Drain());
    }

    [Fact]
    public void A_repair_and_a_dropped_formatting_are_both_named_in_the_order_they_happened()
    {
        using var scope = JsonRepairTrace.Begin();
        const string reply =
            """{"ledeType":"anecdotal","heading":"H","paragraphs":[{"type":"text","runs":[{"text":"by ","bold":true},"{"text":"Stampli"}]}]}""";

        LlmResponseJsonParser.ParseLede(reply, "tool page 'Stampli' lede");

        Assert.Equal(["stray-quote-before-object", "drop-writer-formatting"], Assert.Single(scope.Drain()).Repairs);
    }
}
