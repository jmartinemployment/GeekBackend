using GeekAPI.Services.Voice.Onboarding;

namespace GeekBackend.Tests.Voice;

/// <summary>The approved caller question set is whole: one opening question and fourteen areas of three real questions each.</summary>
public sealed class CompanyCallerQuestionsTests
{
    [Fact]
    public void The_opening_question_is_the_approved_wording()
    {
        Assert.Equal(
            "Which area would you like help with: paying bills, getting paid, cash flow, taxes or payroll, advertising, marketing content, email marketing, finding leads, or a website chatbot or phone receptionist?",
            CompanyCallerQuestions.OpeningQuestion);
    }

    [Fact]
    public void There_are_fourteen_named_areas_each_with_three_questions()
    {
        Assert.Equal(14, CompanyCallerQuestions.Areas.Count);
        Assert.Equal(14, CompanyCallerQuestions.Areas.Select(a => a.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.All(CompanyCallerQuestions.Areas, area =>
        {
            Assert.False(string.IsNullOrWhiteSpace(area.Name));
            Assert.Equal(3, area.Questions.Count);
            Assert.All(area.Questions, q => Assert.False(string.IsNullOrWhiteSpace(q)));
        });
    }

    [Fact]
    public void Every_question_ends_with_a_question_mark()
    {
        Assert.EndsWith("?", CompanyCallerQuestions.OpeningQuestion, StringComparison.Ordinal);
        Assert.All(CompanyCallerQuestions.Areas.SelectMany(a => a.Questions), q => Assert.EndsWith("?", q, StringComparison.Ordinal));
    }

    [Fact]
    public void The_stored_shape_holds_the_opening_question_and_every_area()
    {
        var json = CompanyCallerQuestions.ToJson();
        Assert.Equal(CompanyCallerQuestions.OpeningQuestion, json.GetProperty("openingQuestion").GetString());
        var areas = json.GetProperty("areas").EnumerateArray().ToList();
        Assert.Equal(CompanyCallerQuestions.Areas.Select(a => a.Name), areas.Select(a => a.GetProperty("name").GetString()));
        Assert.Equal(
            CompanyCallerQuestions.Areas.SelectMany(a => a.Questions),
            areas.SelectMany(a => a.GetProperty("questions").EnumerateArray().Select(q => q.GetString())));
    }

    [Fact]
    public void A_paying_tenant_starts_with_no_qualifying_questions()
    {
        var config = ReceptionistDefaults.ConfigFor(new ReceptionistScript("Hello.", "Prompt."));
        Assert.Equal("[]", config.QualifyingQuestions.GetRawText());
    }
}
