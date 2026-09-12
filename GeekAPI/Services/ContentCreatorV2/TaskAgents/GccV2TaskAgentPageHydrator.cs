using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using GeekApplication.Models.ContentCreator;
using GeekAPI.Services.ContentCreatorV2.Hierarchy;
using GeekAPI.Services.ContentCreatorV2.Partner;

namespace GeekAPI.Services.ContentCreatorV2.TaskAgents;

/// <summary>
/// SSRF-gated page hydrate for Knowledge, run attachments, and task-agent forms.
/// Tries plain HTTP first; when extraction is empty/thin and a rendered HTML source is configured,
/// falls back to mobile Playwright (Pixel 7) without changing the SSRF gate.
/// </summary>
public sealed class GccV2TaskAgentPageHydrator(
    HttpClient http,
    IGccV2RenderedHtmlSource? renderedHtml = null)
{
    public const string ContractVersion = "gcc-task-agent-page-hydrate.v1";
    public const int MaxRedirects = 5;
    public const int ThinContentChars = 120;
    public const int MaxVisibleContentChars = GccPartnerResearchCaps.MaxCharsPerPage;
    public const string EngineHttp = "http";
    public const string EnginePlaywright = "playwright";

    private readonly HttpClient _http = http ?? throw new ArgumentNullException(nameof(http));
    private readonly IGccV2RenderedHtmlSource? _renderedHtml = renderedHtml;

    public async Task<GccV2TaskAgentPageHydrateOutcome> HydrateAsync(
        string? url,
        CancellationToken ct,
        GccV2SafeOutboundUrl.HostResolver? resolve = null)
    {
        if (!GccV2SafeOutboundUrl.TryValidate(url, out var safeUri, out var rejectionReason, resolve))
        {
            return Fail("ssrf", rejectionReason ?? "URL is not allowed.", statusCode: null, HttpStatusCode.BadRequest);
        }

        var httpOutcome = await HydrateHttpAsync(safeUri, ct, resolve).ConfigureAwait(false);
        if (!ShouldTryPlaywright(httpOutcome) || _renderedHtml is null)
            return httpOutcome;

        var rendered = await TryHydratePlaywrightAsync(safeUri.AbsoluteUri, ct, resolve).ConfigureAwait(false);
        if (rendered is null)
            return httpOutcome;

        return PreferBetter(httpOutcome, rendered);
    }

    internal static bool ShouldTryPlaywright(GccV2TaskAgentPageHydrateOutcome httpOutcome)
    {
        if (httpOutcome.Ok)
            return string.Equals(httpOutcome.ContentCompleteness, "partial", StringComparison.OrdinalIgnoreCase);

        return httpOutcome.ErrorCode is "empty" or "extract";
    }

    internal static GccV2TaskAgentPageHydrateOutcome PreferBetter(
        GccV2TaskAgentPageHydrateOutcome httpOutcome,
        GccV2TaskAgentPageHydrateOutcome playwrightOutcome)
    {
        if (!playwrightOutcome.Ok)
            return httpOutcome;
        if (!httpOutcome.Ok)
            return playwrightOutcome;

        var httpLen = httpOutcome.VisibleContent?.Length ?? 0;
        var pwLen = playwrightOutcome.VisibleContent?.Length ?? 0;
        var httpFull = string.Equals(httpOutcome.ContentCompleteness, "full", StringComparison.OrdinalIgnoreCase);
        var pwFull = string.Equals(playwrightOutcome.ContentCompleteness, "full", StringComparison.OrdinalIgnoreCase);
        if (pwFull && !httpFull) return playwrightOutcome;
        if (pwLen > httpLen + 40) return playwrightOutcome;
        return httpOutcome;
    }

    private async Task<GccV2TaskAgentPageHydrateOutcome> HydrateHttpAsync(
        Uri safeUri,
        CancellationToken ct,
        GccV2SafeOutboundUrl.HostResolver? resolve)
    {
        var sw = Stopwatch.StartNew();
        Uri current = safeUri;
        HttpResponseMessage? response = null;
        var truncatedHtml = false;

        try
        {
            for (var hop = 0; hop <= MaxRedirects; hop++)
            {
                ct.ThrowIfCancellationRequested();
                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                request.Headers.UserAgent.ParseAdd(GccPartnerResearchCaps.UserAgent);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.8));

                response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                    .ConfigureAwait(false);

                if (IsRedirect(response.StatusCode))
                {
                    var location = response.Headers.Location;
                    response.Dispose();
                    response = null;
                    if (location is null)
                        return Fail("http", "Redirect response missing Location.", null, HttpStatusCode.BadGateway);

                    var next = location.IsAbsoluteUri
                        ? location
                        : new Uri(current, location);
                    if (!GccV2SafeOutboundUrl.TryValidate(next.AbsoluteUri, out var nextSafe, out var redirectReason, resolve))
                    {
                        return Fail(
                            "ssrf",
                            redirectReason ?? "Redirect target is not allowed.",
                            null,
                            HttpStatusCode.BadRequest);
                    }

                    if (hop == MaxRedirects)
                    {
                        return Fail(
                            "http",
                            $"Too many redirects (max {MaxRedirects}).",
                            null,
                            HttpStatusCode.BadGateway);
                    }

                    current = nextSafe;
                    continue;
                }

                break;
            }

            if (response is null)
                return Fail("http", "No HTTP response.", null, HttpStatusCode.BadGateway);

            var statusCode = (int)response.StatusCode;
            var media = response.Content.Headers.ContentType?.MediaType ?? "";
            if (media.Length > 0
                && !media.Contains("html", StringComparison.OrdinalIgnoreCase)
                && !media.Contains("text/plain", StringComparison.OrdinalIgnoreCase)
                && !media.Contains("xml", StringComparison.OrdinalIgnoreCase))
            {
                return Fail(
                    "contentType",
                    $"Unsupported content type '{media}'.",
                    statusCode,
                    HttpStatusCode.UnsupportedMediaType);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var limited = new LimitedReadStream(stream, GccPartnerResearchCaps.MaxHtmlBytes);
            using var reader = new StreamReader(limited, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var html = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
            truncatedHtml = limited.Truncated;

            sw.Stop();
            var finalUrl = response.RequestMessage?.RequestUri?.AbsoluteUri ?? current.AbsoluteUri;
            if (!GccV2SafeOutboundUrl.TryValidate(finalUrl, out _, out var finalReason, resolve))
            {
                return Fail("ssrf", finalReason ?? "Final URL is not allowed.", statusCode, HttpStatusCode.BadRequest);
            }

            return BuildFromHtml(
                html,
                finalUrl,
                statusCode,
                Math.Max(1, (long)sw.Elapsed.TotalMilliseconds),
                truncatedHtml,
                EngineHttp);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            return Fail("timeout", "Page fetch timed out.", null, HttpStatusCode.GatewayTimeout);
        }
        catch (HttpRequestException ex)
        {
            return Fail("http", ex.Message, null, HttpStatusCode.BadGateway);
        }
        finally
        {
            response?.Dispose();
        }
    }

    private async Task<GccV2TaskAgentPageHydrateOutcome?> TryHydratePlaywrightAsync(
        string url,
        CancellationToken ct,
        GccV2SafeOutboundUrl.HostResolver? resolve)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            var rendered = await _renderedHtml!.TryFetchAsync(url, ct).ConfigureAwait(false);
            sw.Stop();
            if (rendered is null || string.IsNullOrWhiteSpace(rendered.Html))
                return null;

            if (!GccV2SafeOutboundUrl.TryValidate(rendered.FinalUrl, out _, out var finalReason, resolve))
            {
                return Fail(
                    "ssrf",
                    finalReason ?? "Playwright final URL is not allowed.",
                    rendered.StatusCode,
                    HttpStatusCode.BadRequest);
            }

            return BuildFromHtml(
                rendered.Html,
                rendered.FinalUrl,
                rendered.StatusCode,
                Math.Max(1, (long)sw.Elapsed.TotalMilliseconds),
                truncatedHtml: false,
                EnginePlaywright);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static GccV2TaskAgentPageHydrateOutcome BuildFromHtml(
        string html,
        string finalUrl,
        int statusCode,
        long loadTimeMs,
        bool truncatedHtml,
        string engine)
    {
        if (string.IsNullOrWhiteSpace(html))
            return Fail("empty", "Page body was empty.", statusCode, HttpStatusCode.UnprocessableEntity);

        var extracted = GccV2ArticleHtmlExtractor.ExtractPartnerPage(finalUrl, html);
        if (GccV2ArticleHtmlExtractor.IsEmpty(extracted)
            && string.IsNullOrWhiteSpace(extracted.Title))
        {
            return Fail("extract", "Could not extract visible page content.", statusCode, HttpStatusCode.UnprocessableEntity);
        }

        var visible = FormatVisibleContent(extracted);
        if (string.IsNullOrWhiteSpace(visible))
        {
            return Fail("extract", "Could not extract visible page content.", statusCode, HttpStatusCode.UnprocessableEntity);
        }

        var truncatedContent = visible.Length >= MaxVisibleContentChars || truncatedHtml;
        var thin = visible.Length < ThinContentChars;
        var completeness = truncatedContent || thin || statusCode is < 200 or >= 300
            ? "partial"
            : "full";

        return new GccV2TaskAgentPageHydrateOutcome(
            Ok: true,
            ErrorCode: null,
            ErrorMessage: null,
            HttpStatus: HttpStatusCode.OK,
            FinalUrl: finalUrl,
            Title: extracted.Title,
            VisibleContent: visible,
            StatusCode: statusCode,
            LoadTimeMs: loadTimeMs,
            ContentCompleteness: completeness,
            Crawlable: statusCode is >= 200 and < 400,
            Engine: engine);
    }

    public static string FormatVisibleContent(GccQuoteablePage page)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(page.Title)
            && !string.Equals(page.Title, page.Url, StringComparison.Ordinal))
        {
            sb.Append("# ").AppendLine(page.Title.Trim());
            sb.AppendLine();
        }

        foreach (var heading in page.Headings)
        {
            var level = Math.Clamp(heading.Level, 1, 6);
            sb.Append(new string('#', level)).Append(' ').AppendLine(heading.Text.Trim());
            sb.AppendLine();
        }

        foreach (var paragraph in page.Paragraphs)
        {
            sb.AppendLine(paragraph.Trim());
            sb.AppendLine();
        }

        var text = sb.ToString().Trim();
        if (text.Length <= MaxVisibleContentChars) return text;
        return text[..MaxVisibleContentChars].TrimEnd() + "\n…";
    }

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.Moved
            or HttpStatusCode.Redirect
            or HttpStatusCode.RedirectMethod
            or HttpStatusCode.RedirectKeepVerb
            or HttpStatusCode.PermanentRedirect
            or (HttpStatusCode)308;

    private static GccV2TaskAgentPageHydrateOutcome Fail(
        string code,
        string message,
        int? statusCode,
        HttpStatusCode httpStatus) =>
        new(
            Ok: false,
            ErrorCode: code,
            ErrorMessage: message,
            HttpStatus: httpStatus,
            FinalUrl: null,
            Title: null,
            VisibleContent: null,
            StatusCode: statusCode,
            LoadTimeMs: null,
            ContentCompleteness: null,
            Crawlable: null,
            Engine: null);

    /// <summary>Stops reading after <paramref name="maxBytes"/> and records truncation.</summary>
    private sealed class LimitedReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _maxBytes;
        private long _read;

        public LimitedReadStream(Stream inner, long maxBytes)
        {
            _inner = inner;
            _maxBytes = maxBytes;
        }

        public bool Truncated { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => _read;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_read >= _maxBytes)
            {
                Truncated = true;
                return 0;
            }

            var remaining = (int)Math.Min(count, _maxBytes - _read);
            if (remaining <= 0)
            {
                Truncated = true;
                return 0;
            }

            var n = _inner.Read(buffer, offset, remaining);
            if (n > 0)
            {
                _read += n;
                if (_read >= _maxBytes)
                    Truncated = true;
            }

            return n;
        }

        public override async Task<int> ReadAsync(
            byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (_read >= _maxBytes)
            {
                Truncated = true;
                return 0;
            }

            var remaining = (int)Math.Min(count, _maxBytes - _read);
            if (remaining <= 0)
            {
                Truncated = true;
                return 0;
            }

            var n = await _inner.ReadAsync(buffer.AsMemory(offset, remaining), cancellationToken)
                .ConfigureAwait(false);
            if (n > 0)
            {
                _read += n;
                if (_read >= _maxBytes)
                    Truncated = true;
            }

            return n;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}

public sealed record GccV2TaskAgentPageHydrateOutcome(
    bool Ok,
    string? ErrorCode,
    string? ErrorMessage,
    HttpStatusCode HttpStatus,
    string? FinalUrl,
    string? Title,
    string? VisibleContent,
    int? StatusCode,
    long? LoadTimeMs,
    string? ContentCompleteness,
    bool? Crawlable,
    string? Engine = null);
