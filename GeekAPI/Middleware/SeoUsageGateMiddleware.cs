using GeekAPI.Auth;
using GeekApplication.Constants.Seo;
using GeekApplication.Interfaces.Seo;

namespace GeekAPI.Middleware;

public sealed class SeoUsageGateMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        IUsageMeteringService metering,
        ISubscriptionService subscriptions,
        ICurrentUserContext userContext)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var feature = MeteredRoutes.GetFeatureKey(context.Request.Method, path);
        if (feature is null)
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

        var ensure = await metering.EnsureWithinLimitAsync(userId, tierResult.Value!, feature);
        if (!ensure.IsSuccess)
        {
            var usage = await metering.GetUsageAsync(userId, feature);
            var limit = await metering.GetLimitAsync(tierResult.Value!, feature);
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await context.Response.WriteAsJsonAsync(new
            {
                error = ensure.Error,
                feature,
                usage = usage.Value,
                limit = limit.Value,
                upgradeUrl = "https://seo.geekatyourspot.com/pricing",
            });
            return;
        }

        await next(context);

        if (context.Response.StatusCode is >= 200 and < 300)
        {
            await metering.IncrementAsync(userId, feature);
        }
    }
}
