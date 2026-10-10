using GeekAPI.Services.ContentCreatorV2.ContentTypes;
using GeekAPI.Services.Workflow.Domain.Entities;
using Xunit;

namespace GeekBackend.Tests.ContentCreatorV2.LongForm;

public sealed class GccV2LongFormTypesTests
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
        Assert.Equal(isLongForm, GccV2LongFormTypes.IsLongForm(contentType));
        Assert.Equal(expectsFaq, GccV2LongFormTypes.ExpectsFaqSection(contentType));
        Assert.Equal(exportFolder, GccV2LongFormTypes.ExportFolder(contentType));
    }
}
