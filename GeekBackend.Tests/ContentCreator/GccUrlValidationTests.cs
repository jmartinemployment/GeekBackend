using GeekApplication.Validation;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// Every URL a project declares becomes a crawl seed and, downstream, a lookup key for grounding
/// (GccGroundingResolver in GeekAPI). Malformed input has to be rejected here, at the field, rather
/// than surface later as a confusing refusal or a silent non-match.
/// </summary>
public class GccUrlValidationTests
{
    [Theory]
    [InlineData("https://partner.test")]
    [InlineData("http://partner.test")]
    [InlineData("https://partner.test/path?q=1")]
    [InlineData("  https://partner.test  ")]
    public void ValidHttpAndHttpsUrlsPass(string url) => Assert.True(GccUrlValidation.IsValid(url));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("partner.test")]
    [InlineData("ftp://partner.test")]
    [InlineData("mailto:ops@partner.test")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/relative/path")]
    [InlineData("http://")]
    public void EverythingElseIsRejected(string? url) => Assert.False(GccUrlValidation.IsValid(url));

    [Fact]
    public void FirstInvalidReturnsNullWhenEveryUrlValidates()
    {
        var result = GccUrlValidation.FirstInvalid(["https://a.test", "https://b.test"]);
        Assert.Null(result);
    }

    [Fact]
    public void FirstInvalidNamesTheOffendingUrl()
    {
        var result = GccUrlValidation.FirstInvalid(["https://a.test", "not a url", "https://b.test"]);
        Assert.Equal("not a url", result);
    }

    [Fact]
    public void FirstInvalidIgnoresBlankEntries()
    {
        // Blank entries are the CleanUrls/repository layer's job to drop; this validator's job is
        // format, not presence.
        Assert.Null(GccUrlValidation.FirstInvalid(["https://a.test", "  ", ""]));
    }

    [Fact]
    public void AnEmptyOrNullListValidatesBecauseTheseFieldsAreOptional()
    {
        Assert.Null(GccUrlValidation.FirstInvalid(null));
        Assert.Null(GccUrlValidation.FirstInvalid([]));
    }
}
