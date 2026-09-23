using GeekAPI.Services.ContentCreator;

namespace GeekBackend.Tests.ContentCreator;

public sealed class GccRequiredToolMentionsAnchorLookupTests
{
    /// <summary>
    /// A brief row carries the URL and the operator's spelling together, so it wins outright over
    /// the name a host can be capitalised into. This is the whole reason the lookup is built here
    /// rather than at the caller.
    /// </summary>
    [Fact]
    public void AnchorLookup_prefersBriefRowSpelling_overHostDerivedName()
    {
        const string brief = """
        {
          "hierarchyPlan": {
            "recommendedTools": [
              { "name": "Zone & Co", "url": "https://www.zoneandco.com/", "source": "operator" }
            ]
          }
        }
        """;

        var lookup = GccRequiredToolMentions.AnchorLookup(brief, ["https://zoneandco.com/pricing"]);

        Assert.Equal("Zone & Co", lookup["zoneandco.com"]);
        Assert.Equal("Zone & Co", lookup["ZONEANDCO.COM"]);
    }

    /// <summary>
    /// With no brief row for a partner URL, the host-derived name stands -- the create still
    /// declared that partner, and an unlabelled chunk is worse than a plainly-spelled one.
    /// </summary>
    [Fact]
    public void AnchorLookup_fallsBackToHostDerivedName_whenBriefNamesNothing()
    {
        var lookup = GccRequiredToolMentions.AnchorLookup(null, ["https://medius.com/ap-automation"]);

        Assert.Equal("Medius", lookup["medius.com"]);
    }

    /// <summary>A create with no partners yields no labels, which is not an error.</summary>
    [Fact]
    public void AnchorLookup_withNoPartners_isEmpty()
    {
        Assert.Empty(GccRequiredToolMentions.AnchorLookup(null, null));
        Assert.Empty(GccRequiredToolMentions.AnchorLookup("{}", []));
        Assert.Empty(GccRequiredToolMentions.AnchorLookup(null, ["not-a-url", "mailto:x@y.com"]));
    }
}
