using System.Security.Claims;
using GeekAPI.Auth;

namespace GeekAPI.Extensions;

public static class ClaimsExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub") ?? throw new UnauthorizedAccessException());

    public static Guid RequireUserId(this ICurrentUserContext ctx) => ctx.UserId;
}
