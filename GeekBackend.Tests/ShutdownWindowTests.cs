using GeekAPI.Services;

namespace GeekBackend.Tests;

/// <summary>A deploy waits for live calls for Railway's drain period less a margin, and changes nothing when the period is not set.</summary>
public sealed class ShutdownWindowTests
{
    private static TimeSpan? Window(string? value) =>
        ShutdownWindow.From(name => name == ShutdownWindow.DrainVariable ? value : null);

    [Fact]
    public void The_plans_600_second_drain_gives_the_host_590_seconds() =>
        Assert.Equal(TimeSpan.FromSeconds(590), Window("600"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ten minutes")]
    [InlineData("10")]
    [InlineData("0")]
    [InlineData("-5")]
    public void An_unset_unreadable_or_too_short_drain_leaves_the_default(string? value) =>
        Assert.Null(Window(value));

    [Fact]
    public void Surrounding_spaces_are_ignored() =>
        Assert.Equal(TimeSpan.FromSeconds(290), Window(" 300 "));
}
