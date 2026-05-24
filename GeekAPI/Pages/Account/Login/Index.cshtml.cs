using GeekAPI.Pages;
using GeekAPI.Services;
using GeekApplication.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace GeekAPI.Pages.Account.Login;

[AllowAnonymous]
[SecurityHeaders]
public sealed class IndexModel : PageModel
{
    private readonly IUserRepository _users;
    private readonly IPendingVerificationRepository _pending;

    public IndexModel(IUserRepository users, IPendingVerificationRepository pending)
    {
        _users = users;
        _pending = pending;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public void OnGet(string? returnUrl) => Input.ReturnUrl = returnUrl ?? string.Empty;

    [EnableRateLimiting("login")]
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (Input.Button == "cancel")
            return Redirect(string.IsNullOrEmpty(Input.ReturnUrl) ? "/" : Input.ReturnUrl);

        if (string.IsNullOrWhiteSpace(Input.Email) || string.IsNullOrWhiteSpace(Input.Password))
        {
            ErrorMessage = "Email and password are required.";
            return Page();
        }

        var userResult = await _users.FindByEmailAsync(Input.Email.Trim());
        if (!userResult.IsSuccess || userResult.Value is null)
        {
            ErrorMessage = "Invalid email or password.";
            return Page();
        }

        var user = userResult.Value;
        var verify = await _users.VerifyPasswordAsync(user.Id, Input.Password);
        if (!verify.IsSuccess || verify.Value is not true)
        {
            ErrorMessage = "Invalid email or password.";
            return Page();
        }

        if (user.TwoFactorEnabled)
        {
            var sessionCode = Guid.NewGuid().ToString("N");
            var expiresAt = DateTime.UtcNow.AddMinutes(5);
            var fingerprint = Input.DeviceFingerprint ?? string.Empty;
            await _pending.StorePendingSessionAsync(user.Id, sessionCode, fingerprint, expiresAt);
            return RedirectToPage(
                "/Account/TwoFactor/Index",
                new { session = sessionCode, returnUrl = Input.ReturnUrl });
        }

        await OAuthSignInService.SignInAsync(HttpContext, user, Input.RememberLogin, cancellationToken);
        return RedirectAfterLogin(Input.ReturnUrl);
    }

    private IActionResult RedirectAfterLogin(string? returnUrl)
    {
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        if (!string.IsNullOrEmpty(returnUrl) && PageExtensions.IsNativeRedirectUri(returnUrl))
            return this.LoadingPage(returnUrl);

        if (!string.IsNullOrEmpty(returnUrl))
            return Redirect(returnUrl);

        return Redirect("/");
    }
}

public sealed class InputModel
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? DeviceFingerprint { get; set; }
    public string ReturnUrl { get; set; } = string.Empty;
    public bool RememberLogin { get; set; }
    public string Button { get; set; } = "login";
}
