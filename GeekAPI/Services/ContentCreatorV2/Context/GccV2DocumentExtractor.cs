using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using HtmlAgilityPack;
using UglyToad.PdfPig;

namespace GeekAPI.Services.ContentCreatorV2.Context;

public sealed record GccV2ExtractedDocument(
    string Text, string ParserName, string ParserVersion, string CoordinatesJson);

public sealed class GccV2DocumentExtractor(IGccV2LocalOcrEngine ocr)
{
    private const int MaxCharacters = 2_000_000;
    private const int MaxPdfPages = 500;
    private const int MaxOfficeParts = 2_000;
    private const long MaxExpandedBytes = 50L * 1024 * 1024;

    public async Task<GccV2ExtractedDocument> ExtractAsync(
        Stream source, string mediaType, long expectedBytes, CancellationToken ct)
    {
        if (expectedBytes > 10 * 1024 * 1024)
            throw new InvalidOperationException("File exceeds extraction limit.");
        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();
        if (bytes.Length != expectedBytes)
            throw new InvalidOperationException("Object size changed after finalization.");
        if (bytes.AsSpan().StartsWith(new byte[] { 0x4d, 0x5a })
            || bytes.AsSpan().StartsWith(new byte[] { 0x7f, 0x45, 0x4c, 0x46 }))
            throw new InvalidOperationException("Executable content is not accepted.");

        var normalizedMediaType = mediaType.Split(';', 2)[0].Trim().ToLowerInvariant();
        if (normalizedMediaType is "image/jpg") normalizedMediaType = "image/jpeg";
        if (normalizedMediaType is "image/tif") normalizedMediaType = "image/tiff";

        if (GccV2LocalOcrEnv.IsImageMediaType(normalizedMediaType))
            return await ExtractImageAsync(bytes, normalizedMediaType, ct);

        if (normalizedMediaType.StartsWith("audio/", StringComparison.Ordinal)
            || normalizedMediaType.StartsWith("video/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Audio and video remain fail-closed until local transcription/keyframe models are configured.");
        }

        return normalizedMediaType switch
        {
            "text/plain" or "text/markdown" or "text/html" =>
                ExtractText(bytes, normalizedMediaType),
            "application/pdf" => ExtractPdf(bytes),
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" =>
                ExtractOpenXml(bytes, "word/document.xml", "OpenXml.Docx", "text"),
            "application/vnd.openxmlformats-officedocument.presentationml.presentation" =>
                ExtractSlides(bytes),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" =>
                ExtractSheets(bytes),
            _ => throw new InvalidOperationException("Media type has no approved local parser."),
        };
    }

    private async Task<GccV2ExtractedDocument> ExtractImageAsync(
        byte[] bytes, string mediaType, CancellationToken ct)
    {
        if (!ocr.IsAvailable)
        {
            throw new InvalidOperationException(
                "Image OCR remains fail-closed until GEEK_CC_OCR_COMMAND and GEEK_CC_OCR_DATA_PROCESSING_APPROVED=true are set.");
        }

        var result = await ocr.RecognizeAsync(bytes, mediaType, ct);
        var text = result.Text;
        EnsureCharacterLimit(text.Length);
        return Complete(new StringBuilder(text), result.EngineName, result.EngineVersion,
        [
            new { kind = "image-ocr", mediaType, startChar = 0, endChar = text.Length },
        ]);
    }

    private static GccV2ExtractedDocument ExtractText(byte[] bytes, string mediaType)
    {
        if (bytes.Take(512).Any(x => x == 0))
            throw new InvalidOperationException("Binary content is not accepted as text.");
        string value;
        try { value = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { throw new InvalidOperationException("Input is not valid UTF-8."); }
        if (mediaType == "text/html")
        {
            var document = new HtmlDocument();
            document.LoadHtml(value);
            document.DocumentNode.SelectNodes("//script|//style|//noscript|//iframe")?.ToList()
                .ForEach(x => x.Remove());
            value = HtmlEntity.DeEntitize(document.DocumentNode.InnerText);
        }
        value = Normalize(value);
        return new(value, mediaType == "text/html" ? "HtmlAgilityPack" : "Utf8Text",
            "1", JsonSerializer.Serialize(new[]
            {
                new { kind = "text", startChar = 0, endChar = value.Length },
            }));
    }

    private static GccV2ExtractedDocument ExtractPdf(byte[] bytes)
    {
        if (!bytes.AsSpan().StartsWith("%PDF-"u8))
            throw new InvalidOperationException("PDF signature does not match the declared media type.");
        using var document = PdfDocument.Open(bytes);
        if (document.NumberOfPages is < 1 or > MaxPdfPages)
            throw new InvalidOperationException($"PDF must contain 1..{MaxPdfPages} pages.");
        var text = new StringBuilder();
        var coordinates = new List<object>();
        foreach (var page in document.GetPages())
        {
            var pageText = Normalize(page.Text);
            if (pageText.Length == 0) continue;
            if (text.Length > 0) text.Append('\n');
            var start = text.Length;
            text.Append(pageText);
            coordinates.Add(new { kind = "page", page = page.Number,
                startChar = start, endChar = start + pageText.Length });
            EnsureCharacterLimit(text.Length);
        }
        return Complete(text, "PdfPig", "0.1", coordinates);
    }

    private static GccV2ExtractedDocument ExtractOpenXml(
        byte[] bytes, string partName, string parser, string coordinateKind)
    {
        using var archive = OpenSafeArchive(bytes);
        var entry = archive.GetEntry(partName)
            ?? throw new InvalidOperationException("Required Office document part is missing.");
        var value = TextFromXml(entry);
        return Complete(new StringBuilder(value), parser, "1",
            [new { kind = coordinateKind, startChar = 0, endChar = value.Length }]);
    }

    private static GccV2ExtractedDocument ExtractSlides(byte[] bytes)
    {
        using var archive = OpenSafeArchive(bytes);
        var slides = archive.Entries
            .Where(x => x.FullName.StartsWith("ppt/slides/slide", StringComparison.Ordinal)
                && x.FullName.EndsWith(".xml", StringComparison.Ordinal))
            .OrderBy(x => NumericSuffix(x.Name, "slide")).ToList();
        if (slides.Count is < 1 or > 500)
            throw new InvalidOperationException("Presentation slide count is outside approved limits.");
        var text = new StringBuilder();
        var coordinates = new List<object>();
        for (var index = 0; index < slides.Count; index++)
        {
            var value = TextFromXml(slides[index]);
            if (value.Length == 0) continue;
            if (text.Length > 0) text.Append('\n');
            var start = text.Length;
            text.Append(value);
            coordinates.Add(new { kind = "slide", slide = index + 1,
                startChar = start, endChar = start + value.Length });
            EnsureCharacterLimit(text.Length);
        }
        return Complete(text, "OpenXml.Pptx", "1", coordinates);
    }

    private static GccV2ExtractedDocument ExtractSheets(byte[] bytes)
    {
        using var archive = OpenSafeArchive(bytes);
        var shared = archive.GetEntry("xl/sharedStrings.xml") is { } sharedEntry
            ? XDocument.Load(sharedEntry.Open()).Descendants().Where(x => x.Name.LocalName == "si")
                .Select(x => string.Concat(x.Descendants().Where(y => y.Name.LocalName == "t")
                    .Select(y => y.Value))).ToList()
            : [];
        var sheets = archive.Entries
            .Where(x => x.FullName.StartsWith("xl/worksheets/sheet", StringComparison.Ordinal)
                && x.FullName.EndsWith(".xml", StringComparison.Ordinal))
            .OrderBy(x => NumericSuffix(x.Name, "sheet")).ToList();
        if (sheets.Count is < 1 or > 200)
            throw new InvalidOperationException("Workbook sheet count is outside approved limits.");
        var text = new StringBuilder();
        var coordinates = new List<object>();
        for (var index = 0; index < sheets.Count; index++)
        {
            using var entryStream = sheets[index].Open();
            var document = XDocument.Load(entryStream, LoadOptions.None);
            foreach (var cell in document.Descendants().Where(x => x.Name.LocalName == "c"))
            {
                var raw = cell.Descendants().FirstOrDefault(x => x.Name.LocalName == "v")?.Value;
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var type = cell.Attribute("t")?.Value;
                var value = type == "s" && int.TryParse(raw, out var sharedIndex)
                    && sharedIndex >= 0 && sharedIndex < shared.Count ? shared[sharedIndex] : raw;
                value = Normalize(value);
                if (value.Length == 0) continue;
                if (text.Length > 0) text.Append('\n');
                var start = text.Length;
                text.Append(value);
                coordinates.Add(new
                {
                    kind = "sheet",
                    sheet = $"sheet{index + 1}",
                    cellRange = cell.Attribute("r")?.Value,
                    startChar = start,
                    endChar = start + value.Length,
                });
                EnsureCharacterLimit(text.Length);
            }
        }
        return Complete(text, "OpenXml.Xlsx", "1", coordinates);
    }

    private static ZipArchive OpenSafeArchive(byte[] bytes)
    {
        if (!bytes.AsSpan().StartsWith("PK"u8))
            throw new InvalidOperationException("Office signature does not match the declared media type.");
        var archive = new ZipArchive(new MemoryStream(bytes, writable: false), ZipArchiveMode.Read);
        if (archive.Entries.Count > MaxOfficeParts
            || archive.Entries.Any(x => x.FullName.Contains("..", StringComparison.Ordinal)
                || x.FullName.StartsWith("/", StringComparison.Ordinal)
                || ((x.ExternalAttributes >> 16) & 0xF000) == 0xA000
                || x.FullName.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase)))
        {
            archive.Dispose();
            throw new InvalidOperationException("Office archive contains forbidden parts.");
        }
        var expanded = archive.Entries.Sum(x => x.Length);
        if (expanded > MaxExpandedBytes || (bytes.Length > 0 && expanded / bytes.Length > 100))
        {
            archive.Dispose();
            throw new InvalidOperationException("Office archive exceeds expansion limits.");
        }
        return archive;
    }

    private static string TextFromXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        var document = XDocument.Load(stream, LoadOptions.None);
        return Normalize(string.Join(" ", document.Descendants()
            .Where(x => x.Name.LocalName is "t" or "tab" or "br")
            .Select(x => x.Name.LocalName == "t" ? x.Value : "\n")));
    }

    private static int NumericSuffix(string name, string prefix)
    {
        var value = Path.GetFileNameWithoutExtension(name).Replace(prefix, "", StringComparison.Ordinal);
        return int.TryParse(value, out var number) ? number : int.MaxValue;
    }

    private static GccV2ExtractedDocument Complete(
        StringBuilder text, string parser, string version, IReadOnlyList<object> coordinates)
    {
        var value = Normalize(text.ToString());
        if (value.Length == 0) throw new InvalidOperationException("Document has no extractable text.");
        EnsureCharacterLimit(value.Length);
        return new(value, parser, version, JsonSerializer.Serialize(coordinates));
    }

    private static string Normalize(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n').Normalize(NormalizationForm.FormC).Trim();

    private static void EnsureCharacterLimit(int length)
    {
        if (length > MaxCharacters)
            throw new InvalidOperationException("Extracted text exceeds character limit.");
    }
}
