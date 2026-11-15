using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;

namespace GeekAPI.Services.Voice.OwnerAuth;

/// <summary>
/// Hashes and checks owner passwords with ASP.NET Core Identity's own hasher (PBKDF2 with HMAC-SHA512, a random salt per password, the
/// format and the iteration count stored in the hash), so no password algorithm is written here. A password is accepted as typed: no
/// length, character or complexity rule is added. The hash leaves GeekAPI only to be stored; the password never does.
/// </summary>
public sealed class OwnerPasswords
{
    private static readonly PasswordHasher<OwnerPasswords> Hasher = new();

    // The hash of a password nobody knows, checked when the email has no login, so an unknown email takes as long as a wrong password and
    // the time an answer takes does not tell anyone which emails have a login.
    private readonly Lazy<string> _decoy = new(() => Hasher.HashPassword(null!, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));

    public string Hash(string password) => Hasher.HashPassword(this, password);

    /// <summary>
    /// True when the password matches. A stored hash that is not base64 is a broken record, not a wrong password, and throws
    /// <see cref="FormatException"/>, which the caller answers as the store failing rather than as a refusal of the person.
    /// </summary>
    public bool Verify(string hash, string password) =>
        Hasher.VerifyHashedPassword(this, hash, password) is not PasswordVerificationResult.Failed;

    public void VerifyDecoy(string password) => Hasher.VerifyHashedPassword(this, _decoy.Value, password);
}
