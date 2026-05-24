using System.Security.Claims;
using GeekApplication.Auth;
using GeekApplication.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Validation.AspNetCore;

namespace GeekAPI.Controllers.Auth;

[ApiController]
[Route("api/auth/2fa")]
[Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
public sealed class TwoFactorController : ControllerBase
{
    private readonly IUserSecretsRepository _secrets;
    private readonly IUserRepository _users;

    public TwoFactorController(IUserSecretsRepository secrets, IUserRepository users)
    {
        _secrets = secrets;
        _users = users;
    }

    [HttpPost("setup")]
    public async Task<IActionResult> Setup(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var secret = TotpVerifier.GenerateSecret();
        var user = await _users.FindByIdAsync(userId);
        var email = user.Value?.Email ?? user.Value?.Username ?? userId.ToString();
        var issuer = Environment.GetEnvironmentVariable("AUTH_SERVER_URL") ?? "GeekAPI";
        return Ok(new
        {
            secret,
            qrUri = TotpVerifier.BuildQrUri(new Uri(issuer).Host, email, secret)
        });
    }

    [HttpPost("activate")]
    public async Task<IActionResult> Activate([FromBody] TotpCodeRequest req, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (!TotpVerifier.Verify(req.Secret, req.Code))
            return BadRequest(new { success = false, error = "Invalid TOTP code" });

        await _secrets.SetTotpSecretAsync(userId, req.Secret, cancellationToken);
        var recoveryCodes = GenerateRecoveryCodes();
        var hashed = recoveryCodes.Select(PasswordHelper.Hash).ToList();
        await _secrets.SetRecoveryCodeHashesAsync(userId, hashed, cancellationToken);

        var user = await _users.FindByIdAsync(userId);
        if (user.IsSuccess && user.Value is not null)
        {
            user.Value.TwoFactorEnabled = true;
            await _users.UpdateAsync(user.Value);
        }

        return Ok(new { success = true, recoveryCodes });
    }

    [HttpPost("verify")]
    public IActionResult Verify([FromBody] TotpCodeOnlyRequest req)
    {
        return Ok(new { valid = TotpVerifier.Verify(req.Secret, req.Code) });
    }

    [HttpPost("disable")]
    public async Task<IActionResult> Disable([FromBody] DisableTwoFactorRequest req, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var verify = await _users.VerifyPasswordAsync(userId, req.Password);
        if (!verify.IsSuccess || verify.Value is not true)
            return Unauthorized(new { success = false, error = "Invalid password" });

        await _secrets.ClearTotpSecretAsync(userId, cancellationToken);
        var user = await _users.FindByIdAsync(userId);
        if (user.IsSuccess && user.Value is not null)
        {
            user.Value.TwoFactorEnabled = false;
            await _users.UpdateAsync(user.Value);
        }

        return Ok(new { success = true });
    }

    private Guid GetUserId()
    {
        var claim = User.FindFirst("geek_user_id")?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(claim, out var userId)
            ? userId
            : throw new UnauthorizedAccessException();
    }

    private static IReadOnlyList<string> GenerateRecoveryCodes()
    {
        var codes = new List<string>(10);
        for (var i = 0; i < 10; i++)
            codes.Add($"{Random.Shared.Next(1000, 9999)}-{Random.Shared.Next(1000, 9999)}");
        return codes;
    }
}

public sealed record TotpCodeRequest(string Secret, string Code);
public sealed record TotpCodeOnlyRequest(string Secret, string Code);
public sealed record DisableTwoFactorRequest(string Password);
