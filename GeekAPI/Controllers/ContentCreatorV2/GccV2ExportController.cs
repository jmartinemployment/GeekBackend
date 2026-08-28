using System.IO.Compression;
using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Publish;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services.Export;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.ContentCreatorV2;

[ApiController]
[Route("api/geek-content-creator-v2/creates/{createId:guid}")]
public class GccV2ExportController : ControllerBase
{
    private readonly ICurrentUserContext _user;
    private readonly HttpGccV2Repository _repo;
    private readonly GccV2HtmlExportService _export;
    private readonly IGeekatyourspotCommitService _commit;
    private readonly ILogger<GccV2ExportController> _logger;

    public GccV2ExportController(
        ICurrentUserContext user,
        HttpGccV2Repository repo,
        GccV2HtmlExportService export,
        IGeekatyourspotCommitService commit,
        ILogger<GccV2ExportController> logger)
    {
        _user = user;
        _repo = repo;
        _export = export;
        _commit = commit;
        _logger = logger;
    }

    [HttpGet("export/html")]
    public async Task<IActionResult> ExportHtml(Guid createId, CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();
        if (!await OwnsCreateAsync(createId, ct)) return Forbid();

        try
        {
            var documents = await _export.ExportCreateAsync(createId, ct);
            using var zipStream = new MemoryStream();
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var document in documents)
                {
                    var entry = archive.CreateEntry(document.FileName, CompressionLevel.Optimal);
                    await using var entryStream = entry.Open();
                    await using var writer = new StreamWriter(entryStream);
                    await writer.WriteAsync(document.Content);
                }
            }

            zipStream.Position = 0;
            return File(zipStream.ToArray(), "application/zip", $"{createId}-html-export.zip");
        }
        catch (ContentGenerationException ex)
        {
            _logger.LogWarning(ex, "HTML export failed for create {CreateId}", createId);
            return Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest, title: "Export failed");
        }
    }

    [HttpPost("export/html/commit")]
    public async Task<IActionResult> CommitHtmlExport(Guid createId, CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();
        if (!await OwnsCreateAsync(createId, ct)) return Forbid();

        try
        {
            var documents = await _export.ExportCreateAsync(createId, ct);
            var result = await _commit.CommitDocumentsAsync(
                documents,
                $"Content Creator v2 export: create {createId} ({documents.Count} file(s))",
                ct);
            return Ok(new { commitSha = result.CommitSha, commitUrl = result.CommitUrl, filePaths = result.FilePaths });
        }
        catch (ContentGenerationException ex)
        {
            _logger.LogWarning(ex, "Commit export failed for create {CreateId}", createId);
            return Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest, title: "Commit failed");
        }
    }

    private async Task<bool> OwnsCreateAsync(Guid createId, CancellationToken ct)
    {
        var create = await _repo.GetCreateAsync(createId, ct);
        return create is not null
               && _user.IsAuthenticated
               && string.Equals(create.OwnerUserId, _user.UserId.ToString("D"), StringComparison.OrdinalIgnoreCase);
    }
}
