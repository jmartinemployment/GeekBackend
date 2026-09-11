using GeekAPI.Controllers.ContentCreatorV2;

namespace GeekBackend.Tests.ContentCreatorV2;

public sealed class GccV2CanvasToGridContractTests
{
    [Fact]
    public void ConvertToGridRequest_accepts_optional_target_grid_id()
    {
        var createNew = new GccV2CanvasProjectsController.ConvertToGridRequest();
        Assert.Null(createNew.TargetGridId);

        var target = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var append = new GccV2CanvasProjectsController.ConvertToGridRequest(
            Capability: "faq-generator",
            CreatedBy: "You",
            TargetGridId: target);
        Assert.Equal(target, append.TargetGridId);
        Assert.Equal("faq-generator", append.Capability);
    }
}
