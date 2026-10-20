using GeekAPI.Services.Gcw;

namespace GeekBackend.Tests.Gcw;

/// <summary>
/// Sentence length and reading ease, held to Jeff's seo-content and geo-content skills: an average of 15
/// to 20 words a sentence, and a Flesch Reading Ease of 60 to 70. geekatyourspot's GEO and SEO audit of
/// 2026-10-20 put the published pillars and tool pages at 11 to 33, against 62 to 66 on /methodology.
/// </summary>
public sealed class GcwPolishReadabilityTests
{
    private static string Body(string text) => $$"""
        {"lede":{"tag":"h2","heading":"Opening","paragraphs":[{"type":"text","runs":[{"text":"{{text}}"}]}],"href":null,"children":[]},
         "sections":[]}
        """;

    // From the operator's own /methodology page, which the audit scores 62 to 66.
    private const string Plain =
        "You are not against automation. You are against handing your books to software you cannot see inside. "
        + "Our methodology works the other way around. It runs in four phases, and each phase ends with something "
        + "you can see and approve.";

    private const string Dense =
        "Leveraging comprehensive automation capabilities, organizations can systematically streamline multifaceted "
        + "accounts receivable operations, substantially minimizing administrative inefficiencies while simultaneously "
        + "optimizing organizational liquidity management across geographically distributed operational environments.";

    [Theory]
    [InlineData("the", 1)]
    [InlineData("make", 1)]
    [InlineData("free", 1)]
    [InlineData("table", 2)]
    [InlineData("invoice", 2)]
    [InlineData("automation", 4)]
    [InlineData("2026", 1)]
    public void Syllables_are_counted_as_runs_of_vowels(string word, int syllables) =>
        Assert.Equal(syllables, GcwPolishAnalyzer.Syllables(word));

    [Fact]
    public void Plain_writing_passes_and_dense_writing_is_listed()
    {
        var plain = GcwPolishAnalyzer.Analyze(Body(Plain));
        var dense = GcwPolishAnalyzer.Analyze(Body(Dense));

        Assert.True(Assert.Single(plain.Checks, c => c.Id == "readability").Passed);
        Assert.True(Assert.Single(plain.Checks, c => c.Id == "sentence-clarity").Passed);

        var readability = Assert.Single(dense.Checks, c => c.Id == "readability");
        Assert.False(readability.Passed);
        Assert.Equal("advisory", readability.Severity);
        Assert.Contains("aim for 60–70", readability.Detail, StringComparison.Ordinal);
        Assert.False(Assert.Single(dense.Checks, c => c.Id == "sentence-clarity").Passed);
    }

    [Fact]
    public void An_average_over_twenty_words_a_sentence_is_listed()
    {
        // 21 words, one sentence: over the skills' 15 to 20, under the 28 this passed until 2026-10-20.
        const string sentence = "The team sends every invoice on the day the work ends and checks the ledger each Friday before the bank closes.";

        var report = GcwPolishAnalyzer.Analyze(Body(sentence));

        Assert.Equal(21, report.WordCount);
        Assert.False(Assert.Single(report.Checks, c => c.Id == "sentence-clarity").Passed);
    }

    [Fact]
    public void Readability_is_never_critical_so_it_never_holds_a_page_back() =>
        Assert.True(GcwPolishAnalyzer.Analyze(Body(Dense)).ShipReady);
}
