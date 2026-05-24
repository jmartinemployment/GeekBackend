using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GeekAPI.Pages;

public static class PageExtensions
{
    public static bool IsNativeRedirectUri(string? redirectUri) =>
        !string.IsNullOrEmpty(redirectUri)
        && !redirectUri.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        && !redirectUri.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

    public static IActionResult LoadingPage(this PageModel page, string? redirectUri) =>
        page.RedirectToPage("/Redirect/Index", new { RedirectUri = redirectUri });
}
