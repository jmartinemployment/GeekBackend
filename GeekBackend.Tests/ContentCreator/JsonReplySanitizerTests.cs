using GeekAPI.Services.Workflow.Services;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// One definition of what is done to a model's JSON reply before it is parsed, each repair named and syntax-only.
/// Before this the repairs were a private list in the parser, applied without a trace, with a code-fence stripper
/// copied into four other places.
/// </summary>
public sealed class JsonReplySanitizerTests
{
    private const string Object = """{"a":"one","b":["two"]}""";

    private static string NamesFor(string raw, string parsedText) =>
        string.Join(
            "+",
            JsonReplySanitizer.Candidates(raw).First(c => c.Text == parsedText).Repairs);

    // ---- what each named repair does ------------------------------------------------------------

    [Fact]
    public void A_reply_that_is_well_formed_offers_itself_once_and_needs_no_repair()
    {
        var candidates = JsonReplySanitizer.Candidates(Object).ToList();

        var only = Assert.Single(candidates);
        Assert.Equal(Object, only.Text);
        Assert.Empty(only.Repairs);
    }

    [Fact]
    public void Whitespace_around_the_reply_is_not_a_repair()
    {
        var first = JsonReplySanitizer.Candidates("\n  " + Object + "  \n").First();

        Assert.Equal(Object, first.Text);
        Assert.Empty(first.Repairs);
    }

    [Theory]
    [InlineData("```json\n{\"a\":\"one\",\"b\":[\"two\"]}\n```")]
    [InlineData("```\n{\"a\":\"one\",\"b\":[\"two\"]}\n```")]
    public void A_code_fence_is_removed_and_named(string fenced)
    {
        Assert.Equal("code-fence", NamesFor(fenced, Object));
    }

    [Fact]
    public void Prose_around_the_object_is_cut_away_and_named()
    {
        const string reply = "Here is the JSON you asked for: " + Object + " Let me know if you want changes.";

        Assert.Equal("extract-object", NamesFor(reply, Object));
    }

    [Fact]
    public void A_raw_line_break_inside_a_string_is_escaped_and_named_and_nothing_else_is_credited()
    {
        const string reply = "{\"a\":\"line one\nline two\"}";

        var repaired = JsonReplySanitizer.Candidates(reply).Single(c => c.Repairs.Count > 0);

        Assert.Equal("{\"a\":\"line one\\nline two\"}", repaired.Text);
        Assert.Equal(["literal-newlines"], repaired.Repairs);
    }

    [Fact]
    public void A_stray_quote_before_an_object_after_a_comma_is_removed_and_named()
    {
        const string reply = """[{"text":"supported by "},"{"text":"Stampli","href":"https://x.test/"}]""";

        var repaired = JsonReplySanitizer.Candidates(reply).Single(c => c.Repairs.Contains("stray-quote-before-object"));

        Assert.Equal("""[{"text":"supported by "},{"text":"Stampli","href":"https://x.test/"}]""", repaired.Text);
        Assert.Equal(["stray-quote-before-object"], repaired.Repairs);
    }

    [Fact]
    public void Repairs_combine_and_every_name_in_the_chain_is_kept_in_the_order_applied()
    {
        const string reply = "```json\nHere it is:\n[{\"text\":\"by \"},\"{\"text\":\"X\"}]\n```";

        var repaired = JsonReplySanitizer.Candidates(reply).Last();

        Assert.Equal("""[{"text":"by "},{"text":"X"}]""", repaired.Text);
        Assert.Equal(["code-fence", "stray-quote-before-object", "extract-object"], repaired.Repairs);
    }

    // ---- what the list guarantees ---------------------------------------------------------------

    [Theory]
    [InlineData(Object)]
    [InlineData("```json\n{\"a\":\"one\",\"b\":[\"two\"]}\n```")]
    [InlineData("Intro {\"a\":\"line\none\"} outro")]
    [InlineData("""[{"text":"by "},"{"text":"X"}]""")]
    [InlineData("not json at all")]
    [InlineData("")]
    public void No_text_is_offered_twice(string reply)
    {
        var texts = JsonReplySanitizer.Candidates(reply).Select(c => c.Text).ToList();

        Assert.Equal(texts.Count, texts.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void A_candidate_with_no_repairs_is_only_ever_the_reply_as_written()
    {
        const string reply = "```json\n" + Object + "\n```";

        var unrepaired = JsonReplySanitizer.Candidates(reply).Where(c => c.Repairs.Count == 0).ToList();

        Assert.Equal(reply, Assert.Single(unrepaired).Text);
    }

    [Fact]
    public void The_repairs_tried_for_a_reply_name_only_those_that_changed_it()
    {
        Assert.Equal(
            "none (no named repair changed the reply)",
            JsonReplySanitizer.RepairsTried("not json at all"));
        Assert.Equal("code-fence", JsonReplySanitizer.RepairsTried("```json\n{\"a\":1}\n```"));
        Assert.Equal(
            "stray-quote-before-object",
            JsonReplySanitizer.RepairsTried("""[{"a":1},"{"b":2}]"""));
    }

    [Fact]
    public void A_repair_that_would_add_a_word_is_not_among_them()
    {
        // Every repair removes a character that carries no content, cuts away text that is not the object, or
        // escapes a break the model wrote. The words of the reply are the words of the candidate.
        const string reply = """Sure: [{"text":"alpha beta "},"{"text":"Gamma","href":"https://x.test/"}] Thanks.""";

        foreach (var candidate in JsonReplySanitizer.Candidates(reply).Where(c => c.Repairs.Count > 0))
        {
            var words = candidate.Text.Split(
                ['"', ',', ':', '{', '}', '[', ']', ' ', '\\'], StringSplitOptions.RemoveEmptyEntries);
            Assert.All(words, w => Assert.Contains(w, reply, StringComparison.Ordinal));
        }
    }
}
