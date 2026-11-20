using System.Text;
using GeekAPI.Services.Voice.Storage;

namespace GeekBackend.Tests.Voice;

/// <summary>
/// The signature both Amazon S3 and Google Cloud Storage's S3-compatible API check. The expected values come from AWS's own published
/// algorithm and its documented example credentials, worked independently of the code under test.
/// </summary>
public sealed class AwsSignatureV4Tests
{
    private const string AccessKeyId = "AKIDEXAMPLE";
    private const string Secret = "wJalrXUtnFEMI/K7MDENG+bPxRfiCYEXAMPLEKEY";
    private static readonly DateTimeOffset Signed = new(2015, 8, 30, 12, 36, 0, TimeSpan.Zero);

    [Fact]
    public void One_put_signs_to_the_value_aws_own_algorithm_gives()
    {
        var header = AwsSignatureV4.AuthorizationHeader(
            AccessKeyId, Secret, "us-east-1", "s3",
            "example-bucket.s3.us-east-1.amazonaws.com", "/2026/11/19/call-1.md",
            Encoding.UTF8.GetBytes("hello"), Signed);

        Assert.Equal(
            "AWS4-HMAC-SHA256 Credential=AKIDEXAMPLE/20150830/us-east-1/s3/aws4_request, "
            + "SignedHeaders=host;x-amz-content-sha256;x-amz-date, "
            + "Signature=5cded6cfbf12d59880882a538bb8f4aacfe18deb317b49fd2ca3b4dbfa3a0acd",
            header);
    }

    [Fact]
    public void The_payload_hash_is_the_one_s3_wants_in_its_own_header()
    {
        Assert.Equal(
            "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824",
            AwsSignatureV4.HashHex(Encoding.UTF8.GetBytes("hello")));
    }

    [Fact]
    public void An_empty_body_hashes_to_the_documented_empty_value()
    {
        Assert.Equal(
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            AwsSignatureV4.HashHex([]));
    }

    [Fact]
    public void The_signing_key_is_derived_through_date_region_service_and_terminator()
    {
        var key = AwsSignatureV4.SigningKey(Secret, "20150830", "us-east-1", "s3");

        Assert.Equal(32, key.Length);
        Assert.NotEqual(Encoding.UTF8.GetBytes("AWS4" + Secret), key);
        // The same inputs give the same key, and any one input changing gives another.
        Assert.Equal(key, AwsSignatureV4.SigningKey(Secret, "20150830", "us-east-1", "s3"));
        Assert.NotEqual(key, AwsSignatureV4.SigningKey(Secret, "20150831", "us-east-1", "s3"));
        Assert.NotEqual(key, AwsSignatureV4.SigningKey(Secret, "20150830", "us-west-2", "s3"));
    }

    [Fact]
    public void A_key_is_encoded_segment_by_segment_so_its_slashes_stay_slashes()
    {
        Assert.Equal("/2026/11/19/call-1.md", AwsSignatureV4.CanonicalPath("/2026/11/19/call-1.md"));
        Assert.Equal("/a%20b/c%2Bd", AwsSignatureV4.CanonicalPath("/a b/c+d"));
    }

    [Fact]
    public void A_different_body_signs_differently_so_the_record_cannot_be_swapped_in_flight()
    {
        var one = AwsSignatureV4.AuthorizationHeader(
            AccessKeyId, Secret, "us-east-1", "s3", "b.s3.amazonaws.com", "/k", Encoding.UTF8.GetBytes("one"), Signed);
        var other = AwsSignatureV4.AuthorizationHeader(
            AccessKeyId, Secret, "us-east-1", "s3", "b.s3.amazonaws.com", "/k", Encoding.UTF8.GetBytes("other"), Signed);

        Assert.NotEqual(one, other);
    }

    [Fact]
    public void The_same_request_at_another_time_signs_differently_so_one_cannot_be_replayed_a_day_later()
    {
        var now = AwsSignatureV4.AuthorizationHeader(
            AccessKeyId, Secret, "us-east-1", "s3", "b.s3.amazonaws.com", "/k", Encoding.UTF8.GetBytes("x"), Signed);
        var tomorrow = AwsSignatureV4.AuthorizationHeader(
            AccessKeyId, Secret, "us-east-1", "s3", "b.s3.amazonaws.com", "/k", Encoding.UTF8.GetBytes("x"), Signed.AddDays(1));

        Assert.NotEqual(now, tomorrow);
    }

    [Fact]
    public void The_secret_never_appears_in_what_is_sent()
    {
        var header = AwsSignatureV4.AuthorizationHeader(
            AccessKeyId, Secret, "us-east-1", "s3", "b.s3.amazonaws.com", "/k", Encoding.UTF8.GetBytes("x"), Signed);

        Assert.DoesNotContain(Secret, header, StringComparison.Ordinal);
        Assert.Contains(AccessKeyId, header, StringComparison.Ordinal);
    }

    [Fact]
    public void The_date_header_matches_the_one_the_signature_was_made_with()
    {
        Assert.Equal("20150830T123600Z", AwsSignatureV4.AmzDate(Signed));
        Assert.Equal("20150830T123600Z", AwsSignatureV4.AmzDate(Signed.ToOffset(TimeSpan.FromHours(5))));
    }
}
