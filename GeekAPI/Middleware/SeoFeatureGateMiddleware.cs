using GeekAPI.Auth;
using GeekApplication.Constants.Seo;
using GeekApplication.Interfaces.Seo;

namespace GeekAPI.Middleware;

public sealed class SeoFeatureGateMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ISubscriptionService subscriptions, ICurrentUserContext userContext)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (!path.StartsWith("/api/seo", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var required = FeatureGates.GetRequiredTier(path);
        if (required is null)
        {
            await next(context);
            return;
        }

        Guid userId;
        try
        {
            userId = userContext.UserId;
        }
        catch
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Authentication required" });
            return;
        }

        var tierResult = await subscriptions.GetActiveTierAsync(userId);
        if (!tierResult.IsSuccess)
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new { error = tierResult.Error });
            return;
        }

        if ((int)tierResult.Value! < (int)required)
        {
            context.Response.StatusCode = StatusCodes.Status402PaymentRequired;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Upgrade required for this feature",
                requiredTier = required.ToString().ToLowerInvariant(),
                currentTier = tierResult.Value.ToString().ToLowerInvariant(),
                upgradeUrl = "https://seo.geekatyourspot.com/pricing",
            });
            return;
        }

        await next(context);
    }
}
