using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GeekApplication.Models.Social;

/// <summary>
/// A platform access token, decrypted, on its way to a platform call. It is the only shape a
/// decrypted token takes: <see cref="ToString"/>, the debugger and JSON all print
/// <see cref="Redacted"/>, so a token placed in a log line, an exception message or a response body
/// by mistake still never prints. The value leaves only through <see cref="Reveal"/>, which the
/// platform clients call when they build an Authorization header and nothing else should.
/// </summary>
[JsonConverter(typeof(SocialTokenJsonConverter))]
[DebuggerDisplay("[redacted social token]")]
public sealed class SocialToken
{
    public const string Redacted = "[redacted social token]";

    private readonly string _value;

    public SocialToken(string value, DateTime? expiresAt = null)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A token value is required.", nameof(value));

        _value = value;
        ExpiresAt = expiresAt;
    }

    /// <summary>When the platform says the token stops working, in UTC, or null when it gives no expiry.</summary>
    public DateTime? ExpiresAt { get; }

    public string Reveal() => _value;

    public override string ToString() => Redacted;
}

/// <summary>Writes a token as <see cref="SocialToken.Redacted"/> and refuses to read one: a token is never carried in JSON.</summary>
public sealed class SocialTokenJsonConverter : JsonConverter<SocialToken>
{
    public override SocialToken Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new JsonException("A SocialToken is never read from JSON.");

    public override void Write(Utf8JsonWriter writer, SocialToken value, JsonSerializerOptions options) =>
        writer.WriteStringValue(SocialToken.Redacted);
}
