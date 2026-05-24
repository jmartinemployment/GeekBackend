using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GeekAPI.Pages.Redirect;

[AllowAnonymous]
[SecurityHeaders]
public sealed class IndexModel : PageModel
{
    public string? RedirectUri { get; private set; }

    public void OnGet(string? redirectUri) => RedirectUri = redirectUri;
}
