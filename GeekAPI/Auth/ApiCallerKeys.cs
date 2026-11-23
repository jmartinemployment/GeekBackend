using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace GeekAPI.Auth;

/// <summary>
/// The X-API-Key credentials GeekAPI accepts, one per named caller, so a leaked key identifies whose it was and is replaced without
/// touching anyone else's. <c>GEEK_API_CALLERS</c> lists the caller names (comma separated, lower case letters, digits and hyphens);
/// each name's key is the variable <c>GEEK_API_KEY_</c> plus the name upper-cased with hyphens as underscores
/// (<c>geek-receptionist</c> reads <c>GEEK_API_KEY_GEEK_RECEPTIONIST</c>). A name with no key, or a stray <c>GEEK_API_KEY_*</c> variable
/// not in the list, never authenticates anyone.
/// <para>
/// <c>GEEK_BACKEND_API_KEY</c> is the one key every server used to share. It still authenticates, as the caller <c>legacy</c>, only
/// while callers are being moved onto their own keys; every use of it is logged as a warning so its removal can wait for that count to
/// reach zero, and it is then deleted together with <see cref="LegacyVariable"/>.
/// </para>
/// </summary>
public static class ApiCallerKeys
{
    public const string CallersVariable = "GEEK_API_CALLERS";
    public const string KeyVariablePrefix = "GEEK_API_KEY_";
    public const string LegacyVariable = "GEEK_BACKEND_API_KEY";
    public const string LegacyCaller = "legacy";

    /// <summary>Set on HttpContext.Items to the name of the caller whose key was verified, and only then.</summary>
    public const string CallerItem = "geek.api-caller";

    private static readonly Regex ValidName = new("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string KeyVariableFor(string caller) => KeyVariablePrefix + caller.ToUpperInvariant().Replace('-', '_');

    /// <summary>The caller names listed in <see cref="CallersVariable"/>, lower-cased and trimmed, in the order given.</summary>
    public static IReadOnlyList<string> ListedCallers() =>
        (Environment.GetEnvironmentVariable(CallersVariable) ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(name => name.ToLowerInvariant())
            .ToList();

    /// <summary>Every caller that has a key right now: the listed ones, then <c>legacy</c> if the old shared key is set.</summary>
    public static IReadOnlyList<(string Caller, string Key)> Configured()
    {
        var configured = new List<(string, string)>();
        foreach (var caller in ListedCallers())
        {
            if (!ValidName.IsMatch(caller)) continue;
            var key = Environment.GetEnvironmentVariable(KeyVariableFor(caller));
            if (!string.IsNullOrWhiteSpace(key)) configured.Add((caller, key));
        }

        var legacy = Environment.GetEnvironmentVariable(LegacyVariable);
        if (!string.IsNullOrWhiteSpace(legacy)) configured.Add((LegacyCaller, legacy));
        return configured;
    }

    /// <summary>
    /// The caller whose key this is, or null. Every configured key is compared, in fixed time and with no early exit, so how long a wrong
    /// key takes tells the caller neither how much of it was right nor which caller's key it was closest to.
    /// </summary>
    public static string? Authenticate(string presented)
    {
        var presentedHash = SHA256.HashData(Encoding.UTF8.GetBytes(presented));
        string? matched = null;
        foreach (var (caller, key) in Configured())
        {
            var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
            if (CryptographicOperations.FixedTimeEquals(expectedHash, presentedHash) && matched is null) matched = caller;
        }

        return matched;
    }

    /// <summary>
    /// Everything wrong with the configuration, for GeekAPI to refuse to start on: no key at all, a listed name that is malformed or has
    /// no key, a name listed twice, <c>legacy</c> listed as a name, a <c>GEEK_API_KEY_*</c> variable that no listed name reads, and two
    /// callers holding the same key, which is exactly the sharing this exists to end. Messages name variables, never values.
    /// </summary>
    public static IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        var listed = ListedCallers();

        foreach (var caller in listed)
        {
            if (!ValidName.IsMatch(caller))
            {
                problems.Add($"{CallersVariable} lists '{caller}', which is not a valid caller name (lower case letters, digits and single hyphens).");
                continue;
            }

            if (caller == LegacyCaller)
                problems.Add($"{CallersVariable} lists '{LegacyCaller}', a name reserved for {LegacyVariable}.");
            else if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(KeyVariableFor(caller))))
                problems.Add($"{CallersVariable} lists '{caller}' but {KeyVariableFor(caller)} is not set.");
        }

        foreach (var duplicate in listed.GroupBy(name => name).Where(group => group.Count() > 1))
            problems.Add($"{CallersVariable} lists '{duplicate.Key}' more than once.");

        var readByAName = listed.Where(name => ValidName.IsMatch(name)).Select(KeyVariableFor).ToHashSet(StringComparer.Ordinal);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var variable = (string)entry.Key;
            if (variable.StartsWith(KeyVariablePrefix, StringComparison.Ordinal) && !readByAName.Contains(variable))
                problems.Add($"{variable} is set but no caller in {CallersVariable} reads it.");
        }

        var configured = Configured();
        if (configured.Count == 0)
            problems.Add($"No API key is configured: set {CallersVariable} and one {KeyVariablePrefix}<NAME> per caller.");

        foreach (var shared in configured.GroupBy(c => c.Key, StringComparer.Ordinal).Where(group => group.Count() > 1))
            problems.Add($"Callers {string.Join(", ", shared.Select(c => c.Caller))} hold the same key; each caller needs its own.");

        return problems;
    }
}
