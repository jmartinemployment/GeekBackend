using System.IO.Compression;
using System.Text;
using GeekAPI.Services.ContentCreatorV2.Context;

namespace GeekBackend.Tests.ContentCreatorV2;

public sealed class GccV2DocumentExtractorTests
{
    private readonly GccV2DocumentExtractor _extractor = new();

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

    private const string Docx =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private const string Pptx =
        "application/vnd.openxmlformats-officedocument.presentationml.presentation";
    private const string Xlsx =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
}
