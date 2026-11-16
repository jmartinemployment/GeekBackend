namespace GeekAPI.Services.Voice.Onboarding;

/// <summary>
/// A US business phone as people write it, (561) 555-0101, 561-555-0101, 561.555.0101 or 5615550101, with or without a leading
/// 1 or +1, turned into the stored form, +1 and ten digits. The area code and the exchange start with 2 through 9, as the North
/// American plan numbers them. Anything else is not a US number and answers null: another country's code, letters, an extension,
/// too few or too many digits.
/// </summary>
public static class UsPhone
{
    public static string? ToE164(string? written)
    {
        if (string.IsNullOrWhiteSpace(written)) return null;
        var text = written.Trim();
        if (!text.All(c => char.IsAsciiDigit(c) || c is ' ' or '-' or '.' or '(' or ')' or '+')) return null;
        if (text.LastIndexOf('+') > 0) return null;

        var digits = new string(text.Where(char.IsAsciiDigit).ToArray());
        if (text.StartsWith('+') && !(digits.Length == 11 && digits[0] == '1')) return null;
        if (digits.Length == 11 && digits[0] == '1') digits = digits[1..];
        if (digits.Length != 10 || digits[0] < '2' || digits[3] < '2') return null;

        return "+1" + digits;
    }
}
