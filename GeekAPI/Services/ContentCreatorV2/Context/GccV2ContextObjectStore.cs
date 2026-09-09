using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace GeekAPI.Services.ContentCreatorV2.Context;

public sealed record GccV2ObjectUploadGrant(
    Uri UploadUrl, IReadOnlyDictionary<string, string> RequiredHeaders, DateTimeOffset ExpiresAtUtc);
public sealed record GccV2VerifiedObject(long ByteSize, string Sha256, string? MediaType);

public interface IGccV2ContextObjectStore
{
    GccV2ObjectUploadGrant IssuePut(string objectKey, long contentLength, string mediaType, TimeSpan lifetime);
    Task<GccV2VerifiedObject> VerifyAsync(string objectKey, long maximumBytes, CancellationToken ct);
    Task<Stream> OpenReadAsync(string objectKey, CancellationToken ct);
    Task PutAsync(string objectKey, Stream content, string mediaType, CancellationToken ct);
    Task DeleteAsync(string objectKey, CancellationToken ct);
}

public sealed class GccV2S3ContextObjectStore : IGccV2ContextObjectStore
{
    private const string Algorithm = "AWS4-HMAC-SHA256";
    private readonly HttpClient _http;
    private readonly Uri _endpoint;
    private readonly string _bucket;
    private readonly string _region;
    private readonly string _accessKey;
    private readonly string _secretKey;

    public GccV2S3ContextObjectStore(HttpClient http, IConfiguration configuration)
    {
        _http = http;
        var section = configuration.GetSection("ContentCreatorV2:ContextObjectStore");
        _bucket = Required(section["Bucket"], "ContentCreatorV2:ContextObjectStore:Bucket");
        var endpoint = new Uri(
            Required(section["Endpoint"], "ContentCreatorV2:ContextObjectStore:Endpoint")
                .TrimEnd('/') + "/");
        _endpoint = new UriBuilder(endpoint)
        {
            Host = $"{_bucket}.{endpoint.Host}",
            Path = "/",
        }.Uri;
        _region = section["Region"]?.Trim() is { Length: > 0 } region ? region : "us-east-1";
        _accessKey = Required(section["AccessKeyId"], "ContentCreatorV2:ContextObjectStore:AccessKeyId");
        _secretKey = Required(section["SecretAccessKey"], "ContentCreatorV2:ContextObjectStore:SecretAccessKey");
    }

    public GccV2ObjectUploadGrant IssuePut(string objectKey, long contentLength, string mediaType, TimeSpan lifetime)
    {
        if (contentLength <= 0) throw new ArgumentOutOfRangeException(nameof(contentLength));
        var now = DateTimeOffset.UtcNow;
        var expires = Math.Clamp((int)lifetime.TotalSeconds, 60, 900);
        var uri = Presign(HttpMethod.Put, objectKey, now, expires, contentLength, mediaType);
        return new GccV2ObjectUploadGrant(uri,
            new Dictionary<string, string> { ["Content-Type"] = mediaType }, now.AddSeconds(expires));
    }

    public async Task<GccV2VerifiedObject> VerifyAsync(string objectKey, long maximumBytes, CancellationToken ct)
    {
        await using var stream = await OpenReadAsync(objectKey, ct);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await stream.ReadAsync(buffer, ct);
            if (read == 0) break;
            total += read;
            if (total > maximumBytes)
                throw new InvalidOperationException("Stored object exceeds its authorized size.");
            sha.AppendData(buffer, 0, read);
        }
        return new GccV2VerifiedObject(total, Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant(), null);
    }

    public async Task<Stream> OpenReadAsync(string objectKey, CancellationToken ct)
    {
        var request = SignedRequest(HttpMethod.Get, objectKey, DateTimeOffset.UtcNow);
        var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        return new ResponseOwnedStream(await response.Content.ReadAsStreamAsync(ct), response);
    }

    public async Task PutAsync(string objectKey, Stream content, string mediaType, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();
        var payloadHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        using var request = SignedRequest(HttpMethod.Put, objectKey, DateTimeOffset.UtcNow, payloadHash);
        request.Content = new ByteArrayContent(bytes);
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(mediaType);
        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteAsync(string objectKey, CancellationToken ct)
    {
        using var response = await _http.SendAsync(SignedRequest(HttpMethod.Delete, objectKey, DateTimeOffset.UtcNow), ct);
        response.EnsureSuccessStatusCode();
    }

    private Uri Presign(
        HttpMethod method, string objectKey, DateTimeOffset now, int expires,
        long contentLength, string mediaType)
    {
        var date = now.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var timestamp = now.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var scope = $"{date}/{_region}/s3/aws4_request";
        var path = CanonicalPath(objectKey);
        var query = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["X-Amz-Algorithm"] = Algorithm,
            ["X-Amz-Credential"] = $"{_accessKey}/{scope}",
            ["X-Amz-Date"] = timestamp,
            ["X-Amz-Expires"] = expires.ToString(CultureInfo.InvariantCulture),
            ["X-Amz-SignedHeaders"] = "content-length;content-type;host",
        };
        var canonicalQuery = string.Join("&", query.Select(x => $"{Encode(x.Key)}={Encode(x.Value)}"));
        var host = _endpoint.IsDefaultPort ? _endpoint.Host : _endpoint.Authority;
        var canonicalRequest =
            $"{method.Method}\n{path}\n{canonicalQuery}\ncontent-length:{contentLength}\ncontent-type:{mediaType.Trim()}\nhost:{host}\n\ncontent-length;content-type;host\nUNSIGNED-PAYLOAD";
        var stringToSign = $"{Algorithm}\n{timestamp}\n{scope}\n{Hash(canonicalRequest)}";
        query["X-Amz-Signature"] = Convert.ToHexString(
            Hmac(SigningKey(date), stringToSign)).ToLowerInvariant();
        return new Uri(_endpoint, path.TrimStart('/') + "?" +
            string.Join("&", query.Select(x => $"{Encode(x.Key)}={Encode(x.Value)}")));
    }

    private HttpRequestMessage SignedRequest(
        HttpMethod method, string objectKey, DateTimeOffset now, string? payloadHashOverride = null)
    {
        var date = now.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var timestamp = now.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var scope = $"{date}/{_region}/s3/aws4_request";
        var path = CanonicalPath(objectKey);
        var host = _endpoint.IsDefaultPort ? _endpoint.Host : _endpoint.Authority;
        var payloadHash = payloadHashOverride ?? Hash("");
        var canonicalRequest = $"{method.Method}\n{path}\n\nhost:{host}\nx-amz-content-sha256:{payloadHash}\nx-amz-date:{timestamp}\n\nhost;x-amz-content-sha256;x-amz-date\n{payloadHash}";
        var signature = Convert.ToHexString(Hmac(SigningKey(date),
            $"{Algorithm}\n{timestamp}\n{scope}\n{Hash(canonicalRequest)}")).ToLowerInvariant();
        var request = new HttpRequestMessage(method, new Uri(_endpoint, path.TrimStart('/')));
        request.Headers.Host = host;
        request.Headers.TryAddWithoutValidation("x-amz-date", timestamp);
        request.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);
        request.Headers.Authorization = new AuthenticationHeaderValue(Algorithm,
            $"Credential={_accessKey}/{scope}, SignedHeaders=host;x-amz-content-sha256;x-amz-date, Signature={signature}");
        return request;
    }

    private string CanonicalPath(string objectKey)
    {
        if (string.IsNullOrWhiteSpace(objectKey) || objectKey.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException("Invalid object key.", nameof(objectKey));
        return "/" + string.Join(
            "/", objectKey.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Encode));
    }

    private byte[] SigningKey(string date)
    {
        var dateKey = Hmac(Encoding.UTF8.GetBytes("AWS4" + _secretKey), date);
        var regionKey = Hmac(dateKey, _region);
        var serviceKey = Hmac(regionKey, "s3");
        return Hmac(serviceKey, "aws4_request");
    }

    private static byte[] Hmac(byte[] key, string value) =>
        HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(value));
    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string Encode(string value) =>
        Uri.EscapeDataString(value).Replace("%7E", "~", StringComparison.Ordinal);
    private static string Required(string? value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new InvalidOperationException($"{name} is required.") : value.Trim();

    private sealed class ResponseOwnedStream(Stream inner, HttpResponseMessage response) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
            await inner.ReadAsync(buffer, ct);
        protected override void Dispose(bool disposing)
        {
            if (disposing) { inner.Dispose(); response.Dispose(); }
            base.Dispose(disposing);
        }
        public override async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync();
            response.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
