using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace GeekAPI.Services.Voice.Storage;

/// <summary>
/// Signs one request with AWS Signature Version 4, which is what both Amazon S3 and Google Cloud Storage's S3-compatible XML API
/// accept. It is written out here rather than taken from a vendor SDK because exactly one operation is ever signed — putting one
/// object — and the whole of that is below; a full SDK would bring a client that can also read, list and delete, which is precisely
/// what the plan's minimal policy refuses to grant.
///
/// The derived signing key is never the secret itself and is thrown away with the request. Nothing here logs.
/// </summary>
public static class AwsSignatureV4
{
    public const string Algorithm = "AWS4-HMAC-SHA256";
    public const string Terminator = "aws4_request";

    /// <summary>The headers that must be signed for an object write, lowest first as the canonical form requires them sorted.</summary>
    public const string SignedHeaders = "host;x-amz-content-sha256;x-amz-date";

    /// <summary>The hex SHA-256 of a payload, which S3 wants in a header as well as in the signature.</summary>
    public static string HashHex(ReadOnlySpan<byte> payload) => Convert.ToHexStringLower(SHA256.HashData(payload));

    /// <summary>
    /// The Authorization header for putting <paramref name="payload"/> at <paramref name="path"/> on <paramref name="host"/>.
    /// <paramref name="path"/> is the object's key with a leading slash, and is encoded segment by segment so a slash in it stays a
    /// slash, as S3 reads it.
    /// </summary>
    public static string AuthorizationHeader(
        string accessKeyId, string secretAccessKey, string region, string service, string host, string path,
        ReadOnlySpan<byte> payload, DateTimeOffset now)
    {
        var amzDate = now.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var dateStamp = amzDate[..8];
        var payloadHash = HashHex(payload);

        var canonicalRequest =
            $"PUT\n{CanonicalPath(path)}\n\nhost:{host}\nx-amz-content-sha256:{payloadHash}\nx-amz-date:{amzDate}\n\n{SignedHeaders}\n{payloadHash}";

        var scope = $"{dateStamp}/{region}/{service}/{Terminator}";
        var stringToSign = $"{Algorithm}\n{amzDate}\n{scope}\n{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest)))}";

        var signature = Convert.ToHexStringLower(Hmac(SigningKey(secretAccessKey, dateStamp, region, service), stringToSign));
        return $"{Algorithm} Credential={accessKeyId}/{scope}, SignedHeaders={SignedHeaders}, Signature={signature}";
    }

    /// <summary>The date and time this header set is signed for, in the form the x-amz-date header takes.</summary>
    public static string AmzDate(DateTimeOffset now) =>
        now.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    /// <summary>
    /// The object key as the canonical request spells it: every segment percent-encoded, the separating slashes left alone. S3 encodes
    /// the path once; a segment with a space or a plus in it signs and sends the same way or the signature will not match.
    /// </summary>
    internal static string CanonicalPath(string path)
    {
        var segments = path.Split('/').Select(Uri.EscapeDataString);
        return string.Join('/', segments);
    }

    /// <summary>The key the signature is made with: the secret taken through the date, the region, the service and the terminator.</summary>
    internal static byte[] SigningKey(string secretAccessKey, string dateStamp, string region, string service)
    {
        var key = Hmac(Encoding.UTF8.GetBytes("AWS4" + secretAccessKey), dateStamp);
        key = Hmac(key, region);
        key = Hmac(key, service);
        return Hmac(key, Terminator);
    }

    private static byte[] Hmac(byte[] key, string value) => HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(value));
}
