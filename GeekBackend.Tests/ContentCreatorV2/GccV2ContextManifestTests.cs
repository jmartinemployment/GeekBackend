using GeekAPI.Services.ContentCreatorV2.Context;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace GeekBackend.Tests.ContentCreatorV2;

public sealed class GccV2ContextManifestTests
{
    [Fact]
    public void Canonical_json_orders_object_properties_but_preserves_array_order()
    {
        var left = GccV2CanonicalJson.Serialize(new { z = 1, nested = new { b = 2, a = 1 }, values = new[] { 2, 1 } });
        var right = GccV2CanonicalJson.Serialize(new { values = new[] { 2, 1 }, nested = new { a = 1, b = 2 }, z = 1 });

        Assert.Equal("""{"nested":{"a":1,"b":2},"values":[2,1],"z":1}""", left);
        Assert.Equal(left, right);
        Assert.Equal(GccV2CanonicalJson.Sha256(left), GccV2CanonicalJson.Sha256(right));
    }

    [Fact]
    public void Signer_verifies_in_fixed_contract_and_rejects_tampering_and_unknown_keys()
    {
        var key = Convert.ToBase64String(Enumerable.Range(0, 32).Select(x => (byte)x).ToArray());
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ContentCreatorV2:ContextManifestSigning:ActiveKeyId"] = "2026-09",
                ["ContentCreatorV2:ContextManifestSigning:Keys:2026-09"] = key,
            }).Build();
        var signer = new GccV2ContextManifestSigner(configuration);
        var digest = GccV2CanonicalJson.Sha256("""{"manifest":"v1"}""");
        var signature = signer.Sign(digest);

        Assert.True(signer.Verify(digest, signature, "2026-09"));
        Assert.False(signer.Verify(GccV2CanonicalJson.Sha256("""{"manifest":"tampered"}"""), signature, "2026-09"));
        Assert.False(signer.Verify(digest, signature, "retired"));
    }

    [Fact]
    public void Signer_fails_closed_without_configured_key()
    {
        var signer = new GccV2ContextManifestSigner(new ConfigurationBuilder().Build());

        Assert.Throws<InvalidOperationException>(() =>
            signer.Sign(new string('0', 64)));
    }

    [Fact]
    public void Shared_cross_language_manifest_fixture_matches_digest_and_signature()
    {
        var fixture = JsonSerializer.Deserialize<ManifestFixture>(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "ContentCreatorV2", "Fixtures",
                "run-context-manifest.v1.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ContentCreatorV2:ContextManifestSigning:ActiveKeyId"] = fixture.KeyId,
                [$"ContentCreatorV2:ContextManifestSigning:Keys:{fixture.KeyId}"] = fixture.KeyBase64,
            }).Build();
        var signer = new GccV2ContextManifestSigner(configuration);

        Assert.Equal(fixture.Sha256, GccV2CanonicalJson.Sha256(fixture.CanonicalJson));
        Assert.Equal(fixture.Signature, signer.Sign(fixture.Sha256));
        Assert.True(signer.Verify(fixture.Sha256, fixture.Signature, fixture.KeyId));
    }

    [Fact]
    public void S3_store_issues_short_lived_length_bound_private_upload()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ContentCreatorV2:ContextObjectStore:Endpoint"] = "https://objects.example.test",
                ["ContentCreatorV2:ContextObjectStore:Bucket"] = "private-context",
                ["ContentCreatorV2:ContextObjectStore:Region"] = "us-east-1",
                ["ContentCreatorV2:ContextObjectStore:AccessKeyId"] = "access",
                ["ContentCreatorV2:ContextObjectStore:SecretAccessKey"] = "secret",
            }).Build();
        var store = new GccV2S3ContextObjectStore(new HttpClient(), configuration);

        var grant = store.IssuePut("owner/asset/original", 123, "text/plain", TimeSpan.FromMinutes(10));

        Assert.Equal("https", grant.UploadUrl.Scheme);
        Assert.Equal("private-context.objects.example.test", grant.UploadUrl.Host);
        Assert.Equal("/owner/asset/original", grant.UploadUrl.AbsolutePath);
        Assert.Contains("X-Amz-Signature=", grant.UploadUrl.Query);
        Assert.Contains("content-length%3Bcontent-type%3Bhost", grant.UploadUrl.Query);
        Assert.Equal("text/plain", grant.RequiredHeaders["Content-Type"]);
        Assert.DoesNotContain("secret", grant.UploadUrl.ToString(), StringComparison.Ordinal);
    }

    private sealed record ManifestFixture(
        string KeyId, string KeyBase64, string CanonicalJson, string Sha256, string Signature);
}
