using GeekAPI.Services.ContentCreatorV2.Plan;
using Xunit;

namespace GeekBackend.Tests;

public class GccV2OutlinePutValidatorTests
{
    [Fact]
    public void ValidatePutOutlineSections_allows_legacy_outlines_without_jobs()
    {
        var error = GccV2OutlinePutValidator.ValidatePutOutlineSections([null, "", "  "]);

        Assert.Null(error);
    }

    [Fact]
    public void ValidatePutOutlineSections_requires_exactly_one_problem()
    {
        Assert.Equal(
            "Outline must include exactly one problem section.",
            GccV2OutlinePutValidator.ValidatePutOutlineSections(["advance", "advance"]));

        Assert.Equal(
            "Outline must include exactly one problem section.",
            GccV2OutlinePutValidator.ValidatePutOutlineSections(["problem", "problem", "advance"]));
    }

    [Fact]
    public void ValidatePutOutlineSections_requires_problem_on_first_row()
    {
        Assert.Equal(
            "First outline section must be the problem role.",
            GccV2OutlinePutValidator.ValidatePutOutlineSections(["advance", "problem", "faq"]));
    }

    [Fact]
    public void ValidatePutOutlineSections_accepts_one_problem_plus_advance_rows()
    {
        var error = GccV2OutlinePutValidator.ValidatePutOutlineSections(
            ["problem", "advance", "advance", "advance", "faq"]);

        Assert.Null(error);
    }

    [Fact]
    public void ValidatePutOutlineSections_requires_two_options_for_comparison()
    {
        Assert.Equal(
            "Comparison and alternatives outlines need at least two option sections.",
            GccV2OutlinePutValidator.ValidatePutOutlineSections(
                ["problem", "advance", "faq"],
                "comparison"));
    }

    [Fact]
    public void ValidatePutOutlineSections_requires_two_steps_for_guide()
    {
        Assert.Equal(
            "Guide outlines need at least two step sections.",
            GccV2OutlinePutValidator.ValidatePutOutlineSections(
                ["problem", "advance", "faq"],
                "guide"));
    }
}
