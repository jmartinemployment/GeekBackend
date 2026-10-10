using GeekAPI.Services.ContentCreator.ContentTypes;
using GeekAPI.Services.Workflow.Domain.Entities;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

public sealed class GccLongFormTypesTests
{
    [Theory]
    [InlineData("comparison", true, true, "comparison")]
    [InlineData("case-study", true, false, "case-studies")]
    [InlineData("guide", true, true, "guides")]
    [InlineData("alternatives", true, true, "alternatives")]
    [InlineData("tech-article", true, true, "tech-articles")]
    [InlineData("listicle", true, true, "listicles")]
    [InlineData("service", true, false, "services")]
    [InlineData("local", true, true, "local")]
    [InlineData("whitepaper", true, false, "whitepapers")]
    [InlineData("email", false, false, "articles")]
    public void Registry_metadata_for_tier_types(
        string contentType,
        bool isLongForm,
        bool expectsFaq,
        string exportFolder)
    {
        Assert.Equal(isLongForm, GccLongFormTypes.IsLongForm(contentType));
        Assert.Equal(expectsFaq, GccLongFormTypes.ExpectsFaqSection(contentType));
        Assert.Equal(exportFolder, GccLongFormTypes.ExportFolder(contentType));
    }
}
