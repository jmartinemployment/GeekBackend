using GeekAPI.Services.Voice.Onboarding;

namespace GeekBackend.Tests.Voice;

/// <summary>A US phone written any of the ways people write one is stored as +1 and ten digits; anything that is not a US number is refused.</summary>
public sealed class UsPhoneTests
{
    [Theory]
    [InlineData("(561) 555-0101")]
    [InlineData("561-555-0101")]
    [InlineData("561.555.0101")]
    [InlineData("5615550101")]
    [InlineData(" 561 555 0101 ")]
    [InlineData("1 561 555 0101")]
    [InlineData("1-561-555-0101")]
    [InlineData("+1 (561) 555-0101")]
    [InlineData("+15615550101")]
    public void A_us_number_however_written_is_stored_as_plus_one_and_ten_digits(string written) =>
        Assert.Equal("+15615550101", UsPhone.ToE164(written));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("555-0101")]            // seven digits, no area code
    [InlineData("561555010")]           // nine digits
    [InlineData("56155501011")]         // eleven digits not led by 1
    [InlineData("+44 20 7946 0958")]    // another country
    [InlineData("+5615550101")]         // a plus with no US country code
    [InlineData("161-555-0101")]        // an area code cannot start with 1
    [InlineData("061-555-0101")]        // or 0
    [InlineData("561-155-0101")]        // nor an exchange
    [InlineData("561-555-0101 x12")]    // an extension
    [InlineData("561-CALL-NOW")]        // letters
    [InlineData("1+5615550101")]        // a plus in the middle
    public void Anything_that_is_not_a_us_number_is_refused(string? written) => Assert.Null(UsPhone.ToE164(written));
}
