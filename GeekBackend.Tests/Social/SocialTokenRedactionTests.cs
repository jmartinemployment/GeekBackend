using System.Globalization;
using System.Text.Json;
using GeekApplication.Models.Social;
using Xunit;

namespace GeekBackend.Tests.Social;

/// <summary>
/// A decrypted token must not reach a log line, an exception message or a response body by accident.
/// Each test below puts one in a place a careless line of code would, and checks the secret is not
/// in the text that comes out.
/// </summary>
public class SocialTokenRedactionTests
{
    private const string Secret = "EAAB-super-secret-token-value-123";

    private static SocialToken Token() => new(Secret, new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void ToString_and_string_interpolation_print_the_redaction()
    {
        var token = Token();

        Assert.Equal(SocialToken.Redacted, token.ToString());
        Assert.DoesNotContain(Secret, $"calling with {token}");
        Assert.DoesNotContain(Secret, string.Format(CultureInfo.InvariantCulture, "calling with {0}", token));
    }

    [Fact]
    public void Serializing_a_token_writes_the_redaction()
    {
        var json = JsonSerializer.Serialize(Token());

        Assert.Equal($"\"{SocialToken.Redacted}\"", json);
        Assert.DoesNotContain(Secret, json);
    }

    [Fact]
    public void A_token_inside_a_model_serializes_redacted()
    {
        var account = new SocialConnectedAccount
        {
            Platform = SocialPlatforms.Facebook,
            PlatformAccountId = "123",
            DisplayName = "Test Page",
            Token = Token(),
        };

        var json = JsonSerializer.Serialize(account);

        Assert.DoesNotContain(Secret, json);
        Assert.Contains(SocialToken.Redacted, json);
    }

    [Fact]
    public void A_token_inside_a_collection_serializes_redacted()
    {
        var connection = new SocialConnection
        {
            Accounts =
            {
                new SocialConnectedAccount { Platform = SocialPlatforms.Instagram, PlatformAccountId = "9", Token = Token() },
            },
        };

        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(connection));
    }

    [Fact]
    public void A_token_is_never_read_back_from_JSON()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SocialToken>($"\"{Secret}\""));
    }

    [Fact]
    public void An_exception_that_names_the_token_does_not_leak_it()
    {
        var token = Token();

        Action failingCall = () => throw new InvalidOperationException($"call failed for {token}");

        var thrown = Assert.Throws<InvalidOperationException>(failingCall);

        Assert.DoesNotContain(Secret, thrown.Message);
        Assert.DoesNotContain(Secret, thrown.ToString());
    }

    [Fact]
    public void Reveal_is_the_only_way_to_read_the_value()
    {
        Assert.Equal(Secret, Token().Reveal());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_token_without_a_value_cannot_be_made(string? value)
    {
        var thrown = Assert.ThrowsAny<ArgumentException>(() => new SocialToken(value!));

        Assert.Contains("token value is required", thrown.Message);
    }

    [Fact]
    public void The_expiry_is_carried_beside_the_value()
    {
        Assert.Equal(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), Token().ExpiresAt);
        Assert.Null(new SocialToken(Secret).ExpiresAt);
    }
}
