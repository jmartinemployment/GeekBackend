using System.Text.Json;
using System.Text.Json.Nodes;
using GeekApplication.Models.Voice;

namespace GeekAPI.Services.Voice.Storage;

/// <summary>
/// The least a business has to grant for its call records to be written, as something the owner can copy
/// (plans/client-and-app-plan.md: "a copy-pasteable minimal IAM policy (s3:PutObject or storage.objectCreator on a single bucket
/// prefix) with zero read, list, or delete rights").
///
/// It is built here, beside the writer, so the two cannot drift: the writer makes exactly one kind of request, a PUT of one object,
/// and this grants exactly the right to do that and nothing else. A test holds the pair together.
/// </summary>
public static class StorageSetupPolicy
{
    /// <summary>The one S3 action the writer ever performs.</summary>
    public const string S3Action = "s3:PutObject";

    /// <summary>The Google role that allows creating an object and nothing else: no read, no list, no delete, no overwrite.</summary>
    public const string GoogleRole = "roles/storage.objectCreator";

    /// <summary>The resource the grant covers: the business's own prefix inside its own bucket, and nothing outside it.</summary>
    public static string ResourceFor(string bucket, string keyPrefix)
    {
        var prefix = (keyPrefix ?? string.Empty).Trim('/');
        return string.IsNullOrEmpty(prefix) ? $"arn:aws:s3:::{bucket}/*" : $"arn:aws:s3:::{bucket}/{prefix}/*";
    }

    /// <summary>What the owner pastes: an IAM policy for Amazon, or the one role to grant for Google.</summary>
    public static string For(string provider, string bucket, string keyPrefix) =>
        provider == VoiceStorageProviders.GoogleCloudStorage ? GoogleInstructions(bucket, keyPrefix) : AmazonPolicy(bucket, keyPrefix);

    private static string AmazonPolicy(string bucket, string keyPrefix)
    {
        var policy = new JsonObject
        {
            ["Version"] = "2012-10-17",
            ["Statement"] = new JsonArray(
                new JsonObject
                {
                    ["Sid"] = "GeekReceptionistWriteOnly",
                    ["Effect"] = "Allow",
                    ["Action"] = S3Action,
                    ["Resource"] = ResourceFor(bucket, keyPrefix),
                }),
        };
        return policy.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static string GoogleInstructions(string bucket, string keyPrefix)
    {
        var prefix = (keyPrefix ?? string.Empty).Trim('/');
        var where = string.IsNullOrEmpty(prefix) ? bucket : $"{bucket} (records are written under {prefix}/)";
        return $"""
            Grant the service account {GoogleRole} on this bucket, and nothing else:

            gcloud storage buckets add-iam-policy-binding gs://{bucket} \
              --member=serviceAccount:YOUR-SERVICE-ACCOUNT \
              --role={GoogleRole}

            That role may create an object and may not read, list, delete or overwrite one.
            Then make an HMAC key for that service account and enter its access key and secret here.

            Bucket: {where}
            """;
    }
}
