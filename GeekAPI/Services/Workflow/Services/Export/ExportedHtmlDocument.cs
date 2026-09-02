namespace GeekAPI.Services.Workflow.Services.Export;

public sealed record ExportedHtmlDocument(string FileName, string? Content = null, byte[]? BinaryContent = null);
