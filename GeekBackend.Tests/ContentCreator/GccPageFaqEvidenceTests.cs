using GeekAPI.Services.ContentCreator;
using GeekApplication.Models.ContentCreator;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// What a pillar's or a blog's question is answered from: the passages every partner's crawl gave
/// for it, read together, and sent in calls whose size is bounded by code.
/// </summary>
public class GccPageFaqEvidenceTests
{
    private const string Question = "What is automated payment execution?";

    private static GccQuoteablePage Page(string host, params string[] passages) =>
        new($"https://{host}/a", $"{host} page", [], passages);

    [Fact]
    public void A_question_is_found_across_every_partner_searched_for_it()
    {
        GccFaqEvidence[] searched =
        [
            new("chaser.test", Question, [Page("chaser.test", "Chaser says.")]),
            new("upflow.test", Question, [Page("upflow.test", "Upflow says.")]),
            new("upflow.test", "Another question?", [Page("upflow.test", "Something else.")]),
        ];

        var found = GccPageFaqEvidence.FoundFor(searched, Question);

        Assert.Equal(["Chaser says.", "Upflow says."], found!.SelectMany(p => p.Paragraphs));
    }

    [Fact]
    public void Not_searched_found_nothing_and_found_are_three_answers()
    {
        // Research that carries no searches, and a question no search was made for: not searched.
        Assert.Null(GccPageFaqEvidence.FoundFor(null, Question));
        Assert.Null(GccPageFaqEvidence.FoundFor([new GccFaqEvidence("chaser.test", "Another question?", [])], Question));
        // Searched in two crawls and neither had anything: found nothing.
        var nothing = GccPageFaqEvidence.FoundFor(
            [new GccFaqEvidence("chaser.test", Question, []), new GccFaqEvidence("upflow.test", Question, [])], Question);
        Assert.NotNull(nothing);
        Assert.Empty(nothing);
    }

    [Fact]
    public void A_question_is_matched_as_written()
    {
        GccFaqEvidence[] searched = [new("chaser.test", Question, [Page("chaser.test", "Chaser says.")])];

        Assert.Null(GccPageFaqEvidence.FoundFor(searched, Question.ToUpperInvariant()));
    }

    private static (string Question, IReadOnlyList<GccQuoteablePage> Pages) Asked(int number, int passageChars) =>
        ($"Question {number}?", [Page("chaser.test", new string('x', passageChars))]);

    [Fact]
    public void Small_questions_go_eight_to_a_call_in_the_order_given()
    {
        var toAsk = Enumerable.Range(1, 20).Select(n => Asked(n, 100)).ToList();

        var calls = GccPageFaqEvidence.Calls(toAsk, maxQuestions: 8);

        Assert.Equal([8, 8, 4], calls.Select(c => c.Count));
        Assert.Equal(toAsk.Select(a => a.Question), calls.SelectMany(c => c).Select(a => a.Question));
    }

    /// <summary>
    /// A question brings passages from every partner, so eight of them can outgrow the model's limit on
    /// a project with many partners or long passages. On 2026-10-10 a pillar's body call did exactly
    /// that and nothing in code knew. Here the call is closed before it is too large.
    /// </summary>
    [Fact]
    public void A_call_is_closed_before_its_evidence_passes_the_limit()
    {
        var each = GccPageFaqEvidence.MaxEvidenceCharsPerCall / 3;
        var toAsk = Enumerable.Range(1, 7).Select(n => Asked(n, each)).ToList();

        var calls = GccPageFaqEvidence.Calls(toAsk, maxQuestions: 8);

        // Three would pass the limit once their titles and addresses are counted, so two to a call.
        Assert.Equal([2, 2, 2, 1], calls.Select(c => c.Count));
        Assert.All(calls, call => Assert.True(
            call.Sum(a => a.Pages.Sum(p => p.Title.Length + p.Url.Length + p.Paragraphs.Sum(x => x.Length)))
                <= GccPageFaqEvidence.MaxEvidenceCharsPerCall));
    }

    [Fact]
    public void A_question_larger_than_the_limit_is_a_call_by_itself_and_is_not_divided()
    {
        var toAsk = new[]
        {
            Asked(1, 100),
            Asked(2, GccPageFaqEvidence.MaxEvidenceCharsPerCall + 1),
            Asked(3, 100),
        };

        var calls = GccPageFaqEvidence.Calls(toAsk, maxQuestions: 8);

        Assert.Equal([["Question 1?"], ["Question 2?"], ["Question 3?"]], calls.Select(c => c.Select(a => a.Question)));
    }

    [Fact]
    public void No_questions_is_no_call()
    {
        Assert.Empty(GccPageFaqEvidence.Calls([], maxQuestions: 8));
    }
}
