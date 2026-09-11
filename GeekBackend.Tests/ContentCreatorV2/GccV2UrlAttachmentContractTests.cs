using System.Reflection;
using GeekAPI.Controllers.ContentCreatorV2;
using GeekAPI.Services.ContentCreatorV2.Context;
using Microsoft.AspNetCore.Mvc;

namespace GeekBackend.Tests.ContentCreatorV2;

public sealed class GccV2UrlAttachmentContractTests
{
    [Fact]
    public void Controller_exposes_attachments_from_url_route()
    {
        var method = typeof(GccV2ContextController).GetMethod(
            nameof(GccV2ContextController.CreateAttachmentFromUrl));
        Assert.NotNull(method);
        var route = method!.GetCustomAttribute<HttpPostAttribute>();
        Assert.Equal("creates/{createId:guid}/attachments/from-url", route?.Template);
    }

    [Fact]
    public void Result_record_carries_run_scoped_fields()
    {
        var attachmentId = Guid.NewGuid();
        var createId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var result = new GccV2UrlAttachmentResult(
            attachmentId,
            createId,
            "https://example.com/a",
            "Title",
            "example-com.md",
            "full",
            200,
            42,
            "abc",
            "queued",
            jobId);

        Assert.Equal(attachmentId, result.AttachmentId);
        Assert.Equal(createId, result.CreateId);
        Assert.Equal(jobId, result.IngestionJobId);
        Assert.Equal("example-com.md", result.SafeFileName);
    }
}
