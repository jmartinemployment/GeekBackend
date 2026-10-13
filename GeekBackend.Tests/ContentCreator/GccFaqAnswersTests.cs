using System.Text.Json;
using GeekAPI.Services.ContentCreator;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// The operator's FAQ as the Brief saves it: each question with the answer entered for it (Jeff,
/// 2026-10-13: "I have answers to questions that I am suppling ... no longer query for answer
/// passages"). One reader for the pillar's People Also Ask, the blog's FAQ and a tool page's FAQ,
/// and the division of a page's questions into requests.
/// </summary>
public class GccFaqAnswersTests
{
    private static IReadOnlyList<GccFaqEntry> Read(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return GccFaqAnswers.Read(doc.RootElement);
    }

    [Fact]
    public void Rows_of_question_and_answer_are_read_as_entered_and_trimmed()
    {
        var read = Read("""
            [
              {"question":"  Does it file state taxes?  ","answer":"  Yes, in all fifty states.\n\nIt files each quarter.  "},
              {"question":"What does it cost?","answer":""}
            ]
            """);

        Assert.Equal(
        [
            new GccFaqEntry("Does it file state taxes?", "Yes, in all fifty states.\n\nIt files each quarter."),
            new GccFaqEntry("What does it cost?", string.Empty),
        ], read);
        Assert.True(read[0].HasAnswer);
        Assert.False(read[1].HasAnswer);
    }

    /// <summary>A brief saved before 2026-10-13 holds questions alone, in either of two shapes. Each is a question with no answer.</summary>
    [Theory]
    [InlineData("""  "Does it file state taxes?\r\n\r\n  What does it cost?  "  """)]
    [InlineData("""  ["Does it file state taxes?", "  What does it cost?  ", "   "]  """)]
    public void Questions_saved_alone_are_questions_with_no_answer(string json)
    {
        Assert.Equal(
        [
            new GccFaqEntry("Does it file state taxes?", string.Empty),
            new GccFaqEntry("What does it cost?", string.Empty),
        ], Read(json));
    }

    [Fact]
    public void A_row_with_no_question_is_not_a_row_and_a_question_is_read_once()
    {
        var read = Read("""
            [
              {"question":"","answer":"An answer to nothing."},
              {"answer":"Another."},
              {"question":"What does it cost?","answer":"The first answer."},
              {"question":"What does it cost?","answer":"A second answer to the same question."},
              {"Question":"Is it secure?","Answer":"Yes."},
              {"question":"Unanswered?","answer":null},
              42
            ]
            """);

        Assert.Equal(
        [
            new GccFaqEntry("What does it cost?", "The first answer."),
            new GccFaqEntry("Is it secure?", "Yes."),
            new GccFaqEntry("Unanswered?", string.Empty),
        ], read);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("7")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("\"\"")]
    public void Anything_else_holds_no_question(string json)
    {
        Assert.Empty(Read(json));
    }

    private static GccFaqEntry Entry(int number, int answerChars) =>
        new($"Question {number}?", new string('a', answerChars));

    [Fact]
    public void A_request_takes_at_most_the_questions_it_is_allowed()
    {
        var calls = GccFaqAnswers.Calls([.. Enumerable.Range(1, 19).Select(i => Entry(i, 100))], maxQuestions: 8);

        Assert.Equal([8, 8, 3], calls.Select(c => c.Count).ToArray());
        Assert.Equal(
            Enumerable.Range(1, 19).Select(i => $"Question {i}?").ToList(),
            calls.SelectMany(c => c).Select(e => e.Question).ToList());
    }

    [Fact]
    public void A_request_closes_before_its_answers_pass_the_limit_and_no_answer_is_divided_or_left_out()
    {
        var half = GccFaqAnswers.MaxAnswerCharsPerCall / 2;
        GccFaqEntry[] entries =
        [
            Entry(1, half), Entry(2, half),          // exactly the limit: one request
            Entry(3, half + 1), Entry(4, half),      // one character over: two requests
            Entry(5, GccFaqAnswers.MaxAnswerCharsPerCall * 2), // longer than the limit: a request by itself
            Entry(6, 10),
        ];

        var calls = GccFaqAnswers.Calls(entries, maxQuestions: 8);

        Assert.Equal(
            new[] { "1,2", "3", "4", "5", "6" },
            calls.Select(c => string.Join(",", c.Select(e => e.Question[9..^1]))).ToArray());
    }

    [Fact]
    public void No_questions_are_no_requests()
    {
        Assert.Empty(GccFaqAnswers.Calls([], maxQuestions: 8));
    }

    [Fact]
    public void A_request_shows_each_question_under_its_label_with_the_answer_beneath_it()
    {
        var rendered = GccFaqAnswers.Render(
        [
            new GccFaqEntry("Does it file state taxes?", "Yes, in all fifty states."),
            new GccFaqEntry("What does it cost?", "From $40 a month.\nPlus $6 a person."),
        ]);

        Assert.Equal(
            string.Join(Environment.NewLine,
                "Q1: Does it file state taxes?",
                "The operator's answer to Q1:",
                "Yes, in all fifty states.",
                string.Empty,
                "Q2: What does it cost?",
                "The operator's answer to Q2:",
                "From $40 a month.\nPlus $6 a person."),
            rendered);
    }

    [Fact]
    public void Questions_with_no_answer_are_reported_in_one_line_that_names_the_box()
    {
        Assert.Equal(
            "FAQ: no answer is entered for \"Does it fly?\", and the question was left out. "
            + "Its answer goes beside it under \"Tool page FAQ\" on the Brief.",
            GccFaqAnswers.NoAnswerEntered("FAQ", GccFaqAnswers.ToolBox, ["Does it fly?"]));

        Assert.Equal(
            "the blog's FAQ: no answer is entered for 2 questions, and all 2 were left out. "
            + "Their answers go beside them under \"Blog FAQ (optional)\" on the Brief. "
            + "The questions: \"Does it fly?\"; \"Does it swim?\".",
            GccFaqAnswers.NoAnswerEntered("the blog's FAQ", GccFaqAnswers.BlogBox, ["Does it fly?", "Does it swim?"]));
    }
}
