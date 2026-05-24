using Microsoft.AspNetCore.Mvc.Filters;

namespace GeekAPI.Pages;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class SecurityHeadersAttribute : ActionFilterAttribute
{
    public override void OnResultExecuting(ResultExecutingContext context)
    {
        var headers = context.HttpContext.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Content-Security-Policy"] = "default-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:";
        base.OnResultExecuting(context);
    }
}
