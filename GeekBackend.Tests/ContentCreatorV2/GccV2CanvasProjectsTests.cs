using GeekRepository.Controllers.ContentCreatorV2;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreatorV2;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GeekBackend.Tests.ContentCreatorV2;

public sealed class GccV2CanvasProjectsTests
{
    [Fact]
    public async Task Create_project_and_append_version_are_durable()
    {
        await using var db = Db();
        var controller = new GccV2CanvasProjectsController(db);
        const string owner = "11111111-1111-1111-1111-111111111111";

        var created = Assert.IsType<GccV2CanvasProject>(Assert.IsType<CreatedAtActionResult>(
            (await controller.Create(new(owner, "Launch", "Desc", "planning"), default)).Result).Value);
        Assert.Equal(owner, created.OwnerUserId);
        Assert.Equal("Launch", created.Name);

        var listed = Assert.IsAssignableFrom<IReadOnlyList<GccV2CanvasProjectsController.CanvasProjectListItem>>(
            Assert.IsType<OkObjectResult>((await controller.List(owner, default)).Result).Value);
        Assert.Single(listed);
        Assert.Equal(0, listed[0].AssetCount);

        var asset = Assert.IsType<GccV2CanvasAsset>(Assert.IsType<CreatedAtActionResult>(
            (await controller.CreateAsset(created.Id, new(owner, "Brief", "brief"), default)).Result).Value);
        Assert.Equal(created.Id, asset.ProjectId);

        var v1 = Assert.IsType<GccV2CanvasAssetVersion>(Assert.IsType<CreatedAtActionResult>(
            (await controller.AppendVersion(created.Id, asset.Id, new(
                owner, "Jeff", "draft", "First draft", "[]", """{"origin":"human","note":"n"}"""),
                default)).Result).Value);
        Assert.Equal(1, v1.VersionNumber);

        var v2 = Assert.IsType<GccV2CanvasAssetVersion>(Assert.IsType<CreatedAtActionResult>(
            (await controller.AppendVersion(created.Id, asset.Id, new(
                owner, "Maya", "approved", "Approved", "[]", """{"origin":"mixed","note":"n"}"""),
                default)).Result).Value);
        Assert.Equal(2, v2.VersionNumber);

        var detail = Assert.IsType<GccV2CanvasProject>(Assert.IsType<OkObjectResult>(
            (await controller.Get(created.Id, owner, default)).Result).Value);
        Assert.Single(detail.Assets);
        Assert.Equal(2, detail.Assets[0].Versions.Count);
    }

    [Fact]
    public async Task Owner_isolation_returns_404_for_non_owner()
    {
        await using var db = Db();
        var controller = new GccV2CanvasProjectsController(db);
        const string owner = "11111111-1111-1111-1111-111111111111";
        const string other = "22222222-2222-2222-2222-222222222222";

        var created = Assert.IsType<GccV2CanvasProject>(Assert.IsType<CreatedAtActionResult>(
            (await controller.Create(new(owner, "Private"), default)).Result).Value);

        Assert.IsType<NotFoundResult>((await controller.Get(created.Id, other, default)).Result);
        Assert.IsType<NotFoundResult>((await controller.Patch(
            created.Id, new(other, Name: "Hacked"), default)).Result);
        Assert.IsType<NotFoundResult>((await controller.CreateAsset(
            created.Id, new(other, "Steal"), default)).Result);

        var otherList = Assert.IsAssignableFrom<IReadOnlyList<GccV2CanvasProjectsController.CanvasProjectListItem>>(
            Assert.IsType<OkObjectResult>((await controller.List(other, default)).Result).Value);
        Assert.Empty(otherList);
    }

    [Fact]
    public async Task Parent_assets_must_belong_to_same_project()
    {
        await using var db = Db();
        var controller = new GccV2CanvasProjectsController(db);
        const string owner = "11111111-1111-1111-1111-111111111111";

        var a = Assert.IsType<GccV2CanvasProject>(Assert.IsType<CreatedAtActionResult>(
            (await controller.Create(new(owner, "A"), default)).Result).Value);
        var b = Assert.IsType<GccV2CanvasProject>(Assert.IsType<CreatedAtActionResult>(
            (await controller.Create(new(owner, "B"), default)).Result).Value);
        var foreign = Assert.IsType<GccV2CanvasAsset>(Assert.IsType<CreatedAtActionResult>(
            (await controller.CreateAsset(b.Id, new(owner, "Foreign"), default)).Result).Value);

        Assert.IsType<BadRequestObjectResult>((await controller.CreateAsset(
            a.Id, new(owner, "Child", "article", [foreign.Id]), default)).Result);
    }

    private static ContentCreatorV2DbContext Db() => new(
        new DbContextOptionsBuilder<ContentCreatorV2DbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
}
