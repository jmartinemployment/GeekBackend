using GeekAPI.Services.ContentCreator;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// A competitor's identity is kept out of what the writer reads: its host and its host-derived name,
/// as whole words, whatever their case. Nothing else in the text changes.
/// </summary>
public sealed class GccCompetitorNamesTests
{
    [Fact]
    public void The_host_and_its_label_are_the_names_longest_first_and_short_labels_are_not()
    {
        var names = GccCompetitorNames.FromUrls(
            ["https://www.tipalti.com/pricing", "https://ap.example.org/x", "not a url", null, "https://tipalti.com/other"]);

        Assert.Equal(["ap.example.org", "tipalti.com", "tipalti"], names);
    }

    [Fact]
    public void Every_whole_word_occurrence_goes_and_ordinary_words_stay()
    {
        var names = GccCompetitorNames.FromUrls(["https://tipalti.com/"]);

        var redacted = GccCompetitorNames.Redact(
            "Tipalti automates AP. See tipalti.com/pricing; TIPALTI's plans and the Tipaltis of this world. Tip the staff.",
            names);

        Assert.Equal(
            "a competitor automates AP. See a competitor/pricing; a competitor's plans and the Tipaltis of this world. Tip the staff.",
            redacted);
    }

    [Fact]
    public void No_names_leaves_the_text_as_it_was()
    {
        Assert.Equal("Tipalti stays.", GccCompetitorNames.Redact("Tipalti stays.", []));
        Assert.Equal(string.Empty, GccCompetitorNames.Redact(string.Empty, ["tipalti"]));
    }
}
