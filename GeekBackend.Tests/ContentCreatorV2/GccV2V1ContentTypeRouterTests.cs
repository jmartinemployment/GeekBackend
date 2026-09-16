using GeekAPI.Services.ContentCreatorV2.V1Restore;

namespace GeekBackend.Tests.ContentCreatorV2;

/// <summary>
/// The restore routes each Create content type to a v1 generator. The failure this guards against is
/// a type with no v1 generator quietly falling through to the pillar writer and returning a long-form
/// article where a social post or an ad was asked for - wrong output that still looks like success.
/// </summary>
public sealed class GccV2V1ContentTypeRouterTests
{
    [Theory]
    [InlineData("pillar")]
    [InlineData("tech-article")]
    [InlineData("comparison")]
    [InlineData("guide")]
    [InlineData("alternatives")]
    [InlineData("listicle")]
    [InlineData("case-study")]
    [InlineData("service")]
    [InlineData("local")]
    [InlineData("whitepaper")]
    [InlineData("PILLAR")]
    public void Long_form_types_use_the_pillar_generator(string contentType) =>
        Assert.Equal(GccV2V1Generator.Pillar, GccV2V1ContentTypeRouter.For(contentType));

    [Fact]
    public void Blog_and_tool_have_their_own_generators()
    {
        Assert.Equal(GccV2V1Generator.Blog, GccV2V1ContentTypeRouter.For("blog"));
        Assert.Equal(GccV2V1Generator.ToolPage, GccV2V1ContentTypeRouter.For("tool"));
    }

    [Theory]
    [InlineData("social")]
    [InlineData("email")]
    [InlineData("image-prompt")]
    [InlineData("ads")]
    [InlineData("linkedin-document")]
    public void Types_v1_cannot_write_are_unsupported_not_silently_pillar(string contentType)
    {
        Assert.Equal(GccV2V1Generator.Unsupported, GccV2V1ContentTypeRouter.For(contentType));

        // The message must name the type, so a failed job says which one could not be written.
        Assert.Contains(contentType, GccV2V1ContentTypeRouter.UnsupportedMessage(contentType), StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_content_type_defaults_to_long_form_rather_than_failing()
    {
        Assert.Equal(GccV2V1Generator.Pillar, GccV2V1ContentTypeRouter.For(null));
        Assert.Equal(GccV2V1Generator.Pillar, GccV2V1ContentTypeRouter.For("   "));
    }
}
