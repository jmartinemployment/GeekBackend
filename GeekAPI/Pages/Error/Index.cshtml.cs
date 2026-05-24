using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GeekAPI.Pages.Error;

[AllowAnonymous]
[SecurityHeaders]
public sealed class IndexModel : PageModel
{
    public string Message { get; private set; } = "An unexpected error occurred.";

    public void OnGet(string? message) =>
        Message = string.IsNullOrWhiteSpace(message) ? Message : message;
}
