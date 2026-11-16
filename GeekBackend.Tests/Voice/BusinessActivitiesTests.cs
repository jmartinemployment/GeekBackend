using GeekAPI.Services.Voice.Onboarding;

namespace GeekBackend.Tests.Voice;

/// <summary>The IRS chart inside GeekAPI: all of it is there, each code once, and a code is found only exactly.</summary>
public sealed class BusinessActivitiesTests
{
    [Fact]
    public void The_2025_Schedule_C_chart_is_there_whole_with_each_code_once()
    {
        Assert.Equal(303, BusinessActivities.All.Count);
        Assert.Equal(BusinessActivities.All.Count, BusinessActivities.All.Select(a => a.Code).Distinct().Count());
        Assert.Contains("Schedule C (Form 1040) (2025)", BusinessActivities.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void A_code_is_found_with_its_name_and_sector()
    {
        var activity = BusinessActivities.Find("541510");

        Assert.NotNull(activity);
        Assert.Equal("Computer systems design & related services", activity.Name);
        Assert.Equal("Professional, Scientific, & Technical Services", activity.Sector);
        Assert.Equal(activity, BusinessActivities.Find(" 541510 "));
    }

    [Theory]
    [InlineData("54151")]
    [InlineData("5415100")]
    [InlineData("Computer systems design & related services")]
    public void Anything_but_an_exact_code_is_not_found(string code) => Assert.Null(BusinessActivities.Find(code));
}
