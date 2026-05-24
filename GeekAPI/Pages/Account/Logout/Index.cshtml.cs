using GeekAPI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GeekAPI.Pages.Account.Logout;

[SecurityHeaders]
public sealed class IndexModel : PageModel
{
    public async Task<IActionResult> OnGetAsync(string? returnUrl, CancellationToken cancellationToken)
    {
        await OAuthSignInService.SignOutAsync(HttpContext, cancellationToken);
        if (!string.IsNullOrEmpty(returnUrl))
            return Redirect(returnUrl);
        return Redirect("/");
    }
}
