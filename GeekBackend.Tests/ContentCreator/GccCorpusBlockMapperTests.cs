using System.Text.Json;
using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.Workflow.Domain.Entities;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// Kind for kind: the crawler's seven typed block kinds must survive into the document model.
/// Flattening them is what RAG's plaintext projection does, and what this mapper exists to avoid.
/// </summary>
public class GccCorpusBlockMapperTests
{
    private static JsonElement Blocks(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void AQuoteBecomesAQuoteParagraphCarryingItsSource()
    {
        var blocks = Blocks("""[{"kind":"quote","text":"Latency fell by half."}]""");

        var result = GccCorpusBlockMapper.MapBlocks(blocks, "https://partner.test/page");

        var quote = Assert.IsType<QuoteParagraph>(Assert.Single(result));
        Assert.Equal("Latency fell by half.", quote.Runs[0].Text);
        Assert.Equal("https://partner.test/page", quote.Cite);
    }

    [Fact]
    public void CodeBecomesCodeAndIsNotTreatedAsProse()
    {
        var blocks = Blocks("""[{"kind":"code","text":"SELECT 1;"}]""");

        var result = GccCorpusBlockMapper.MapBlocks(blocks, null);

        Assert.Equal("SELECT 1;", Assert.IsType<CodeParagraph>(Assert.Single(result)).Code);
    }

    [Fact]
    public void TermAndDefinitionPairIntoOneDefinitionParagraph()
    {
        var blocks = Blocks("""
        [{"kind":"term","text":"RAG"},
         {"kind":"definition","text":"Retrieval and verification."},
         {"kind":"term","text":"AEO"},
         {"kind":"definition","text":"Answer engine optimisation."}]
        """);

        var result = GccCorpusBlockMapper.MapBlocks(blocks, null);

        var definitions = Assert.IsType<DefinitionParagraph>(Assert.Single(result));
        Assert.Equal(2, definitions.Items.Count);
        Assert.Equal("RAG", definitions.Items[0].Term[0].Text);
        Assert.Equal("Retrieval and verification.", definitions.Items[0].Definition[0].Text);
    }

    [Fact]
    public void ATermWithNoDefinitionIsKeptRatherThanDropped()
    {
        var blocks = Blocks("""[{"kind":"term","text":"Orphan"}]""");

        var result = GccCorpusBlockMapper.MapBlocks(blocks, null);

        var definitions = Assert.IsType<DefinitionParagraph>(Assert.Single(result));
        Assert.Equal("Orphan", definitions.Items[0].Term[0].Text);
        Assert.Empty(definitions.Items[0].Definition);
    }

    [Fact]
    public void ConsecutiveListItemsBecomeOneList()
    {
        var blocks = Blocks("""
        [{"kind":"listItem","text":"One"},
         {"kind":"listItem","text":"Two"},
         {"kind":"listItem","text":"Three"}]
        """);

        var result = GccCorpusBlockMapper.MapBlocks(blocks, null);

        Assert.Equal(3, Assert.IsType<ListParagraph>(Assert.Single(result)).Items.Count);
    }

    [Fact]
    public void OrderedAndUnorderedItemsDoNotMergeIntoOneList()
    {
        var blocks = Blocks("""
        [{"kind":"listItem","text":"One","ordered":true},
         {"kind":"listItem","text":"Bullet","ordered":false}]
        """);

        var result = GccCorpusBlockMapper.MapBlocks(blocks, null);

        Assert.Equal(2, result.Count);
        Assert.True(((ListParagraph)result[0]).Ordered);
        Assert.False(((ListParagraph)result[1]).Ordered);
    }

    [Fact]
    public void AParagraphInterruptingAListClosesIt()
    {
        var blocks = Blocks("""
        [{"kind":"listItem","text":"One"},
         {"kind":"paragraph","text":"Interlude."},
         {"kind":"listItem","text":"Two"}]
        """);

        var result = GccCorpusBlockMapper.MapBlocks(blocks, null);

        Assert.Equal(3, result.Count);
        Assert.IsType<ListParagraph>(result[0]);
        Assert.IsType<TextParagraph>(result[1]);
        Assert.IsType<ListParagraph>(result[2]);
    }

    [Fact]
    public void ASoleAnchorLabellingTheWholeTextBecomesAnHref()
    {
        var blocks = Blocks("""
        [{"kind":"paragraph","text":"Acme Docs",
          "anchors":[{"label":"Acme Docs","href":"https://acme.test/docs"}]}]
        """);

        var result = GccCorpusBlockMapper.MapBlocks(blocks, null);

        Assert.Equal("https://acme.test/docs", ((TextParagraph)result[0]).Runs[0].Href);
    }

    [Fact]
    public void APartialAnchorIsNotGuessedIntoPlace()
    {
        // Anchors carry no offsets, so placing a partial link would be guessing at structure.
        var blocks = Blocks("""
        [{"kind":"paragraph","text":"See Acme Docs for details",
          "anchors":[{"label":"Acme Docs","href":"https://acme.test/docs"}]}]
        """);

        var result = GccCorpusBlockMapper.MapBlocks(blocks, null);

        Assert.Null(((TextParagraph)result[0]).Runs[0].Href);
    }

    [Fact]
    public void ATableRowKeepsItsCellsRatherThanVanishing()
    {
        var blocks = Blocks("""[{"kind":"row","cells":["Plan","Price"]}]""");

        var result = GccCorpusBlockMapper.MapBlocks(blocks, null);

        Assert.Equal("Plan | Price", ((TextParagraph)Assert.Single(result)).Runs[0].Text);
    }

    [Fact]
    public void HeadingsBoundSectionsAndAreReadableAsSuch()
    {
        var block = Blocks("""{"kind":"heading","level":3,"text":"Pricing"}""");

        var heading = GccCorpusBlockMapper.ReadHeading(block);

        Assert.NotNull(heading);
        Assert.Equal("Pricing", heading!.Value.Text);
        Assert.Equal(3, heading.Value.Level);
    }

    [Fact]
    public void ANonHeadingBlockReadsAsNoHeading()
    {
        Assert.Null(GccCorpusBlockMapper.ReadHeading(Blocks("""{"kind":"paragraph","text":"x"}""")));
    }

    [Fact]
    public void AbsentOrNonArrayBlocksYieldNothingRatherThanThrowing()
    {
        Assert.Empty(GccCorpusBlockMapper.MapBlocks(null, null));
        Assert.Empty(GccCorpusBlockMapper.MapBlocks(Blocks("""{"kind":"paragraph"}"""), null));
    }

    [Fact]
    public void AllSevenKindsSurviveOnePage()
    {
        // The regression this guards is the one that mattered: seven kinds in, one kind out.
        var blocks = Blocks("""
        [{"kind":"heading","level":2,"text":"H"},
         {"kind":"paragraph","text":"P"},
         {"kind":"listItem","text":"L"},
         {"kind":"quote","text":"Q"},
         {"kind":"code","text":"C"},
         {"kind":"term","text":"T"},
         {"kind":"definition","text":"D"}]
        """);

        var result = GccCorpusBlockMapper.MapBlocks(blocks, "https://p.test");

        Assert.Collection(result,
            p => Assert.IsType<TextParagraph>(p),
            p => Assert.IsType<ListParagraph>(p),
            p => Assert.IsType<QuoteParagraph>(p),
            p => Assert.IsType<CodeParagraph>(p),
            p => Assert.IsType<DefinitionParagraph>(p));
    }
}
