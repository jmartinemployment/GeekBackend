using System.Text.Json;
using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// Fetches partner/tool destination URLs and extracts full usable page content for v2 WRITE.
/// Soft per-URL failures — never throws for a single bad href.
/// </summary>
public sealed class GccPartnerUrlResearchService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly ILogger<GccPartnerUrlResearchService> _logger;

    public GccPartnerUrlResearchService(HttpClient http, ILogger<GccPartnerUrlResearchService> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>
    /// Collect unique http(s) hrefs from hierarchyPlan.recommendedTools + operatorTools on the brief.
    /// </summary>
    public static IReadOnlyList<string> CollectPartnerHrefs(string? rawBriefJson)
    {
        if (string.IsNullOrWhiteSpace(rawBriefJson)) return [];

        try
        {
            using var doc = JsonDocument.Parse(rawBriefJson);
            var root = doc.RootElement;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var urls = new List<string>();

            void Add(string? raw)
            {
                if (urls.Count >= GccPartnerResearchCaps.MaxUrls) return;
                if (!TryNormalizeHttpUrl(raw, out var url)) return;
                if (!seen.Add(url)) return;
                urls.Add(url);
            }

            if (TryGetPropertyIgnoreCase(root, "hierarchyPlan", out var plan)
                && plan.ValueKind == JsonValueKind.Object
                && TryGetPropertyIgnoreCase(plan, "recommendedTools", out var tools)
                && tools.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in tools.EnumerateArray())
                {
                    if (t.ValueKind != JsonValueKind.Object) continue;
                    if (TryGetPropertyIgnoreCase(t, "href", out var h) && h.ValueKind == JsonValueKind.String)
                        Add(h.GetString());
                    else if (TryGetPropertyIgnoreCase(t, "url", out var u) && u.ValueKind == JsonValueKind.String)
                        Add(u.GetString());
                }
            }

            if (TryGetPropertyIgnoreCase(root, "operatorTools", out var ops)
                && ops.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in ops.EnumerateArray())
                {
                    if (t.ValueKind == JsonValueKind.String)
                    {
                        Add(t.GetString());
                        continue;
                    }

                    if (t.ValueKind != JsonValueKind.Object) continue;
                    if (TryGetPropertyIgnoreCase(t, "url", out var u) && u.ValueKind == JsonValueKind.String)
                        Add(u.GetString());
                    else if (TryGetPropertyIgnoreCase(t, "href", out var h) && h.ValueKind == JsonValueKind.String)
                        Add(h.GetString());
                }
            }

            return urls;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public async Task<IReadOnlyList<GccQuoteablePage>> FetchAsync(
        IReadOnlyList<string> urls,
        CancellationToken ct)
    {
        if (urls.Count == 0) return [];

        var gate = new SemaphoreSlim(GccPartnerResearchCaps.MaxConcurrency);
        var tasks = urls.Select(async url =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await FetchOneAsync(url, ct).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        });

        var pages = await Task.WhenAll(tasks).ConfigureAwait(false);
        return pages.Where(p => p is not null).Cast<GccQuoteablePage>().ToList();
    }

    /// <summary>Writes <c>partnerResearch</c> onto the brief JSON (replaces any prior value).</summary>
    public static string? MergePartnerResearchIntoBriefJson(
        string? rawBriefJson,
        IReadOnlyList<GccQuoteablePage> pages)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(rawBriefJson) ? "{}" : rawBriefJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return rawBriefJson;

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (string.Equals(prop.Name, "partnerResearch", StringComparison.OrdinalIgnoreCase))
                        continue;
                    prop.WriteTo(writer);
                }

                writer.WritePropertyName("partnerResearch");
                JsonSerializer.Serialize(writer, pages, JsonOpts);
                writer.WriteEndObject();
            }

            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            return rawBriefJson;
        }
    }

    private async Task<GccQuoteablePage?> FetchOneAsync(string url, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation(
                "Accept", "text/html,application/xhtml+xml;q=0.9,*/*;q=0.8");

            using var response = await _http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Partner URL research skipped {Url}: HTTP {Status}",
                    url, (int)response.StatusCode);
                return null;
            }

            var media = response.Content.Headers.ContentType?.MediaType ?? "";
            if (media.Length > 0
                && !media.Contains("html", StringComparison.OrdinalIgnoreCase)
                && !media.Contains("text/plain", StringComparison.OrdinalIgnoreCase)
                && !media.Contains("xml", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Partner URL research skipped {Url}: content-type {Media}", url, media);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var limited = new LimitedReadStream(stream, GccPartnerResearchCaps.MaxHtmlBytes);
            using var reader = new StreamReader(limited);
            var html = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(html))
            {
                _logger.LogWarning("Partner URL research skipped {Url}: empty body", url);
                return null;
            }

            var page = GccArticleHtmlExtractor.ExtractPartnerPage(url, html);
            if (GccArticleHtmlExtractor.IsEmpty(page))
            {
                _logger.LogWarning("Partner URL research skipped {Url}: no extractable content", url);
                return null;
            }

            _logger.LogInformation(
                "Partner URL research ok {Url}: {HeadingCount} headings, {ParagraphCount} paragraphs",
                url, page.Headings.Count, page.Paragraphs.Count);
            return page;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Partner URL research failed for {Url}", url);
            return null;
        }
    }

    private static bool TryNormalizeHttpUrl(string? raw, out string url)
    {
        url = "";
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var trimmed = raw.Trim();
        if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (trimmed.StartsWith("//"))
                trimmed = "https:" + trimmed;
            else
                return false;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
        if (string.IsNullOrWhiteSpace(uri.Host)) return false;
        url = uri.GetLeftPart(UriPartial.Query);
        if (url.EndsWith('/') && uri.AbsolutePath == "/")
            url = url.TrimEnd('/');
        return true;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement obj, string name, out JsonElement value)
    {
        if (obj.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        foreach (var prop in obj.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>Stops reading after <paramref name="maxBytes"/> to avoid huge downloads.</summary>
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
            if (_read >= _maxBytes) return 0;
            var remaining = (int)Math.Min(count, _maxBytes - _read);
            var n = _inner.Read(buffer, offset, remaining);
            _read += n;
            return n;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (_read >= _maxBytes) return 0;
            var remaining = (int)Math.Min(count, _maxBytes - _read);
            var n = await _inner.ReadAsync(buffer.AsMemory(offset, remaining), cancellationToken).ConfigureAwait(false);
            _read += n;
            return n;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_read >= _maxBytes) return 0;
            var remaining = (int)Math.Min(buffer.Length, _maxBytes - _read);
            var n = await _inner.ReadAsync(buffer[..remaining], cancellationToken).ConfigureAwait(false);
            _read += n;
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
