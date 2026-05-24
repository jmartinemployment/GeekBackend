using GeekAPI.Services;
using GeekApplication.Auth;
using GeekApplication.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace GeekAPI.Pages.Account.TwoFactor;

[AllowAnonymous]
[SecurityHeaders]
public sealed class IndexModel : PageModel
{
    private const int MaxAttempts = 5;
    private readonly IPendingVerificationRepository _pending;
    private readonly IUserRepository _users;
    private readonly IUserSecretsRepository _secrets;

    public IndexModel(
        IPendingVerificationRepository pending,
        IUserRepository users,
        IUserSecretsRepository secrets)
    {
        _pending = pending;
        _users = users;
        _secrets = secrets;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public IActionResult OnGet([FromQuery(Name = "session")] string? session, string? returnUrl)
    {
        Input.SessionCode = session ?? string.Empty;
        Input.ReturnUrl = returnUrl ?? string.Empty;
        return string.IsNullOrWhiteSpace(Input.SessionCode) ? RedirectToPage("/Account/Login/Index") : Page();
    }

    [EnableRateLimiting("twofactor")]
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Input.SessionCode))
            return RedirectToPage("/Account/Login/Index");

        var sessionResult = await _pending.GetPendingSessionAsync(Input.SessionCode);
        if (!sessionResult.IsSuccess || sessionResult.Value is null || sessionResult.Value.IsCompleted)
        {
            ErrorMessage = "Session expired. Please sign in again.";
            return Page();
        }

        var session = sessionResult.Value;
        if (session.ExpiresAt < DateTime.UtcNow || session.AttemptCount >= MaxAttempts)
        {
            await _pending.ExpirePendingSessionAsync(Input.SessionCode);
            ErrorMessage = "Session expired. Please sign in again.";
            return Page();
        }

        var secretResult = await _secrets.GetTotpSecretAsync(session.UserId, cancellationToken);
        if (!secretResult.IsSuccess || string.IsNullOrWhiteSpace(secretResult.Value))
        {
            ErrorMessage = "Two-factor is not configured for this account.";
            return Page();
        }

        var code = Input.UseRecoveryCode ? Input.RecoveryCode : Input.TotpCode;
        var valid = Input.UseRecoveryCode
            ? await VerifyRecoveryCodeAsync(session.UserId, code ?? string.Empty, cancellationToken)
            : TotpVerifier.Verify(secretResult.Value, code ?? string.Empty);

        if (!valid)
        {
            await _pending.IncrementAttemptAsync(Input.SessionCode);
            ErrorMessage = "Invalid verification code.";
            return Page();
        }

        await _pending.CompletePendingSessionAsync(Input.SessionCode);
        var userResult = await _users.FindByIdAsync(session.UserId);
        if (!userResult.IsSuccess || userResult.Value is null)
        {
            ErrorMessage = "User not found.";
            return Page();
        }

        await OAuthSignInService.SignInAsync(HttpContext, userResult.Value, rememberMe: false, cancellationToken);

        if (!string.IsNullOrEmpty(Input.ReturnUrl) && Url.IsLocalUrl(Input.ReturnUrl))
            return Redirect(Input.ReturnUrl);
        if (!string.IsNullOrEmpty(Input.ReturnUrl))
            return Redirect(Input.ReturnUrl);
        return Redirect("/");
    }

    private async Task<bool> VerifyRecoveryCodeAsync(Guid userId, string code, CancellationToken cancellationToken)
    {
        var hashesResult = await _secrets.GetRecoveryCodeHashesAsync(userId, cancellationToken);
        if (!hashesResult.IsSuccess || hashesResult.Value is null)
            return false;

        var normalized = code.Trim().Replace("-", string.Empty, StringComparison.Ordinal);
        foreach (var hash in hashesResult.Value)
        {
            if (PasswordHelper.Verify(normalized, hash))
                return true;
        }

        return false;
    }
}

public sealed class InputModel
{
    public string SessionCode { get; set; } = string.Empty;
    public string ReturnUrl { get; set; } = string.Empty;
    public string? TotpCode { get; set; }
    public string? RecoveryCode { get; set; }
    public bool UseRecoveryCode { get; set; }
}
