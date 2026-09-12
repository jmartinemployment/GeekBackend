namespace GeekAPI.Services.ContentCreatorV2.Context;

/// <summary>
/// Local image OCR gate. Requires an on-box CLI (typically Tesseract) and explicit
/// data-processing approval — never routes images to a hosted parser.
/// </summary>
public static class GccV2LocalOcrEnv
{
    public static readonly HashSet<string> ImageMediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "image/jpg",
        "image/webp",
        "image/tiff",
        "image/tif",
        "image/gif",
    };

    public static string Command =>
        (Environment.GetEnvironmentVariable("GEEK_CC_OCR_COMMAND") ?? "").Trim();

    public static string Language =>
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GEEK_CC_OCR_LANG"))
            ? "eng"
            : Environment.GetEnvironmentVariable("GEEK_CC_OCR_LANG")!.Trim();

    public static bool DataProcessingApproved =>
        string.Equals(
            (Environment.GetEnvironmentVariable("GEEK_CC_OCR_DATA_PROCESSING_APPROVED") ?? "").Trim(),
            "true",
            StringComparison.OrdinalIgnoreCase);

    public static int TimeoutSeconds
    {
        get
        {
            var raw = (Environment.GetEnvironmentVariable("GEEK_CC_OCR_TIMEOUT_SECONDS") ?? "").Trim();
            return int.TryParse(raw, out var seconds) && seconds is > 0 and <= 120 ? seconds : 30;
        }
    }

    public static long MaxImageBytes
    {
        get
        {
            var raw = (Environment.GetEnvironmentVariable("GEEK_CC_OCR_MAX_IMAGE_BYTES") ?? "").Trim();
            return long.TryParse(raw, out var bytes) && bytes is > 0 and <= 10L * 1024 * 1024
                ? bytes
                : 8L * 1024 * 1024;
        }
    }

    public static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Command) && DataProcessingApproved;

    public static bool IsImageMediaType(string? mediaType)
    {
        var normalized = (mediaType ?? "").Split(';', 2)[0].Trim();
        if (normalized.Equals("image/jpg", StringComparison.OrdinalIgnoreCase))
            normalized = "image/jpeg";
        if (normalized.Equals("image/tif", StringComparison.OrdinalIgnoreCase))
            normalized = "image/tiff";
        return ImageMediaTypes.Contains(normalized);
    }

    public static string NormalizeImageMediaType(string mediaType)
    {
        var normalized = mediaType.Split(';', 2)[0].Trim().ToLowerInvariant();
        return normalized switch
        {
            "image/jpg" => "image/jpeg",
            "image/tif" => "image/tiff",
            _ => normalized,
        };
    }
}
