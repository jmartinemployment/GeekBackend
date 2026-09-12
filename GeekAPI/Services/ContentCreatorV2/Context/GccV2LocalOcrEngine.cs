using System.Diagnostics;
using System.Text;

namespace GeekAPI.Services.ContentCreatorV2.Context;

public sealed record GccV2LocalOcrResult(string Text, string EngineName, string EngineVersion);

public interface IGccV2LocalOcrEngine
{
    bool IsAvailable { get; }
    Task<GccV2LocalOcrResult> RecognizeAsync(byte[] imageBytes, string mediaType, CancellationToken ct);
}

/// <summary>No-op OCR used when local OCR is not configured.</summary>
public sealed class GccV2DisabledLocalOcrEngine : IGccV2LocalOcrEngine
{
    public bool IsAvailable => false;

    public Task<GccV2LocalOcrResult> RecognizeAsync(byte[] imageBytes, string mediaType, CancellationToken ct) =>
        throw new InvalidOperationException(
            "Image OCR remains fail-closed until GEEK_CC_OCR_COMMAND and GEEK_CC_OCR_DATA_PROCESSING_APPROVED=true are set.");
}

/// <summary>
/// Invokes a local OCR CLI (Tesseract-compatible: <c>cmd inputPath stdout -l lang</c>).
/// Temp files are written under the process temp directory and deleted after recognition.
/// </summary>
public sealed class GccV2TesseractCliOcrEngine : IGccV2LocalOcrEngine
{
    private const int MaxOcrCharacters = 500_000;

    public bool IsAvailable => GccV2LocalOcrEnv.IsConfigured;

    public async Task<GccV2LocalOcrResult> RecognizeAsync(
        byte[] imageBytes, string mediaType, CancellationToken ct)
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException(
                "Image OCR remains fail-closed until GEEK_CC_OCR_COMMAND and GEEK_CC_OCR_DATA_PROCESSING_APPROVED=true are set.");
        }

        if (imageBytes.Length == 0)
            throw new InvalidOperationException("Image OCR input is empty.");
        if (imageBytes.Length > GccV2LocalOcrEnv.MaxImageBytes)
            throw new InvalidOperationException(
                $"Image exceeds OCR byte budget ({GccV2LocalOcrEnv.MaxImageBytes} bytes).");

        var normalized = GccV2LocalOcrEnv.NormalizeImageMediaType(mediaType);
        EnsureImageSignature(imageBytes, normalized);

        var extension = ExtensionFor(normalized);
        var inputPath = Path.Combine(Path.GetTempPath(), $"gcc-ocr-{Guid.NewGuid():N}{extension}");
        try
        {
            await File.WriteAllBytesAsync(inputPath, imageBytes, ct);

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = GccV2LocalOcrEnv.Command,
                    ArgumentList = { inputPath, "stdout", "-l", GccV2LocalOcrEnv.Language },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                },
            };

            if (!process.Start())
                throw new InvalidOperationException("Failed to start local OCR process.");

            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(GccV2LocalOcrEnv.TimeoutSeconds));

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                TryKill(process);
                throw new InvalidOperationException(
                    $"Local OCR timed out after {GccV2LocalOcrEnv.TimeoutSeconds}s.");
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            if (process.ExitCode != 0)
            {
                var detail = string.IsNullOrWhiteSpace(stderr) ? $"exit {process.ExitCode}" : Trim(stderr);
                throw new InvalidOperationException($"Local OCR failed: {detail}");
            }

            var text = NormalizeOcrText(stdout);
            if (text.Length == 0)
                throw new InvalidOperationException("Local OCR returned no text.");
            if (text.Length > MaxOcrCharacters)
                throw new InvalidOperationException($"OCR output exceeds {MaxOcrCharacters} characters.");

            return new GccV2LocalOcrResult(text, "TesseractCli", "1");
        }
        finally
        {
            try { File.Delete(inputPath); } catch { /* best effort */ }
        }
    }

    internal static void EnsureImageSignature(byte[] bytes, string mediaType)
    {
        var ok = mediaType switch
        {
            "image/png" => bytes.AsSpan().StartsWith(new byte[] { 0x89, 0x50, 0x4e, 0x47 }),
            "image/jpeg" => bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff,
            "image/gif" => bytes.AsSpan().StartsWith("GIF87a"u8) || bytes.AsSpan().StartsWith("GIF89a"u8),
            "image/webp" => bytes.Length >= 12
                && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8)
                && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8),
            "image/tiff" => bytes.AsSpan().StartsWith(new byte[] { 0x49, 0x49, 0x2a, 0x00 })
                || bytes.AsSpan().StartsWith(new byte[] { 0x4d, 0x4d, 0x00, 0x2a }),
            _ => false,
        };
        if (!ok)
            throw new InvalidOperationException("Image signature does not match the declared media type.");
    }

    private static string ExtensionFor(string mediaType) => mediaType switch
    {
        "image/png" => ".png",
        "image/jpeg" => ".jpg",
        "image/gif" => ".gif",
        "image/webp" => ".webp",
        "image/tiff" => ".tiff",
        _ => ".bin",
    };

    private static string NormalizeOcrText(string value)
    {
        var normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
        while (normalized.Contains("\n\n\n", StringComparison.Ordinal))
            normalized = normalized.Replace("\n\n\n", "\n\n", StringComparison.Ordinal);
        return normalized;
    }

    private static string Trim(string value) =>
        value.Length <= 240 ? value.Trim() : value.AsSpan(0, 240).ToString().Trim() + "…";

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch
        {
            // best effort
        }
    }
}
