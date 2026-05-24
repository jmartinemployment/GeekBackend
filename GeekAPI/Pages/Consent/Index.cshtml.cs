using GeekAPI.Pages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GeekAPI.Pages.Consent;

[AllowAnonymous]
[SecurityHeaders]
public sealed class IndexModel : PageModel
{
    public string ApplicationName { get; private set; } = "Application";
    public IReadOnlyList<string> Scopes { get; private set; } = [];

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public void OnGet(string? returnUrl, string? application, string? scope)
    {
        Input.ReturnUrl = returnUrl ?? string.Empty;
        ApplicationName = application ?? ApplicationName;
        Scopes = string.IsNullOrWhiteSpace(scope)
            ? []
            : scope.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public IActionResult OnPost()
    {
        if (Input.Button == "deny")
            return Redirect(Input.ReturnUrl);

        return Redirect(Input.ReturnUrl);
    }

    public sealed class InputModel
    {
        public string ReturnUrl { get; set; } = string.Empty;
        public string Button { get; set; } = "accept";
    }
}
