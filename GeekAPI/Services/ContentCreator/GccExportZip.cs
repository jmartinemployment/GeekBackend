using System.IO.Compression;
using GeekAPI.Services.Workflow.Services.Export;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// An export's documents as one zip. One writer for the create's export and the project's, so the two
/// archives cannot come to differ in how a file is written.
/// </summary>
public static class GccExportZip
{
    public static async Task<byte[]> WriteAsync(IReadOnlyList<ExportedHtmlDocument> documents, CancellationToken ct)
    {
        using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var document in documents)
            {
                var entry = archive.CreateEntry(document.FileName, CompressionLevel.Optimal);
                await using var entryStream = entry.Open();
                if (document.BinaryContent is { Length: > 0 } bytes)
                {
                    await entryStream.WriteAsync(bytes, ct);
                }
                else
                {
                    await using var writer = new StreamWriter(entryStream);
                    await writer.WriteAsync(document.Content ?? string.Empty);
                }
            }
        }

        return zipStream.ToArray();
    }
}
