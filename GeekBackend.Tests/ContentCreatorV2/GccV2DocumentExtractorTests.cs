using System.IO.Compression;
using System.Text;
using GeekAPI.Services.ContentCreatorV2.Context;

namespace GeekBackend.Tests.ContentCreatorV2;

public sealed class GccV2DocumentExtractorTests
{
    private readonly GccV2DocumentExtractor _extractor = new(new GccV2DisabledLocalOcrEngine());

    [Fact]
    public async Task Extracts_docx_locally_with_bounded_text_coordinates()
    {
        var bytes = OfficeArchive(("word/document.xml",
            """<w:document xmlns:w="urn:w"><w:body><w:p><w:t>Approved handbook</w:t></w:p></w:body></w:document>"""));

        var result = await _extractor.ExtractAsync(
            new MemoryStream(bytes), Docx, bytes.Length, CancellationToken.None);

        Assert.Equal("Approved handbook", result.Text);
        Assert.Equal("OpenXml.Docx", result.ParserName);
        Assert.Contains(@"""kind"":""text""", result.CoordinatesJson);
    }

    [Fact]
    public async Task Extracts_slide_and_sheet_coordinates()
    {
        var slides = OfficeArchive(
            ("ppt/slides/slide2.xml", """<p:sld xmlns:p="urn:p"><p:t>Second slide</p:t></p:sld>"""),
            ("ppt/slides/slide1.xml", """<p:sld xmlns:p="urn:p"><p:t>First slide</p:t></p:sld>"""));
        var presentation = await _extractor.ExtractAsync(
            new MemoryStream(slides), Pptx, slides.Length, CancellationToken.None);
        Assert.Equal("First slide\nSecond slide", presentation.Text);
        Assert.Contains(@"""slide"":1", presentation.CoordinatesJson);
        Assert.Contains(@"""slide"":2", presentation.CoordinatesJson);

        var workbook = OfficeArchive(
            ("xl/sharedStrings.xml", """<sst><si><t>Product name</t></si></sst>"""),
            ("xl/worksheets/sheet1.xml", """<worksheet><c r="A1" t="s"><v>0</v></c><c r="B1"><v>42</v></c></worksheet>"""));
        var sheet = await _extractor.ExtractAsync(
            new MemoryStream(workbook), Xlsx, workbook.Length, CancellationToken.None);
        Assert.Equal("Product name\n42", sheet.Text);
        Assert.Contains(@"""cellRange"":""A1""", sheet.CoordinatesJson);
    }

    [Fact]
    public async Task Rejects_macro_parts_and_media_signature_spoofing()
    {
        var macro = OfficeArchive(
            ("word/document.xml", "<document><t>text</t></document>"),
            ("word/vbaProject.bin", "malware"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _extractor.ExtractAsync(
            new MemoryStream(macro), Docx, macro.Length, CancellationToken.None));

        var spoofed = Encoding.UTF8.GetBytes("not a PDF");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _extractor.ExtractAsync(
            new MemoryStream(spoofed), "application/pdf", spoofed.Length, CancellationToken.None));
    }

    [Fact]
    public async Task Images_fail_closed_when_local_ocr_is_disabled()
    {
        var png = MinimalPng();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _extractor.ExtractAsync(new MemoryStream(png), "image/png", png.Length, CancellationToken.None));
        Assert.Contains("OCR remains fail-closed", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Images_use_local_ocr_engine_when_available()
    {
        var png = MinimalPng();
        var extractor = new GccV2DocumentExtractor(new FakeOcrEngine("Governed handbook OCR"));
        var result = await extractor.ExtractAsync(
            new MemoryStream(png), "image/png", png.Length, CancellationToken.None);

        Assert.Equal("Governed handbook OCR", result.Text);
        Assert.Equal("FakeOcr", result.ParserName);
        Assert.Contains(@"""kind"":""image-ocr""", result.CoordinatesJson);
    }

    [Fact]
    public async Task Audio_and_video_stay_fail_closed()
    {
        var bytes = Encoding.UTF8.GetBytes("not really audio");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _extractor.ExtractAsync(new MemoryStream(bytes), "audio/mpeg", bytes.Length, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _extractor.ExtractAsync(new MemoryStream(bytes), "video/mp4", bytes.Length, CancellationToken.None));
    }

    [Fact]
    public void Image_signature_checks_reject_spoofed_png()
    {
        var spoofed = Encoding.UTF8.GetBytes("not a png");
        Assert.Throws<InvalidOperationException>(() =>
            GccV2TesseractCliOcrEngine.EnsureImageSignature(spoofed, "image/png"));
    }

    private static byte[] OfficeArchive(params (string Path, string Content)[] entries)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                var entry = archive.CreateEntry(path);
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                writer.Write(content);
            }
        }
        return output.ToArray();
    }

    /// <summary>1×1 transparent PNG.</summary>
    private static byte[] MinimalPng() =>
    [
        0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0x00, 0x00, 0x00, 0x0d, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00, 0x00, 0x1f, 0x15, 0xc4,
        0x89, 0x00, 0x00, 0x00, 0x0a, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9c, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0d, 0x0a, 0x2d, 0xb4, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4e, 0x44, 0xae,
        0x42, 0x60, 0x82,
    ];

    private sealed class FakeOcrEngine(string text) : IGccV2LocalOcrEngine
    {
        public bool IsAvailable => true;

        public Task<GccV2LocalOcrResult> RecognizeAsync(byte[] imageBytes, string mediaType, CancellationToken ct)
        {
            GccV2TesseractCliOcrEngine.EnsureImageSignature(
                imageBytes, GccV2LocalOcrEnv.NormalizeImageMediaType(mediaType));
            return Task.FromResult(new GccV2LocalOcrResult(text, "FakeOcr", "1"));
        }
    }

    private const string Docx =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private const string Pptx =
        "application/vnd.openxmlformats-officedocument.presentationml.presentation";
    private const string Xlsx =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
}
