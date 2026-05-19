using System.Security.Claims;

namespace GeekAPI.Auth;

public sealed class CurrentUserContext(IHttpContextAccessor accessor) : ICurrentUserContext
{
    public bool IsAuthenticated
    {
        get
        {
            var sub = accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? accessor.HttpContext?.User.FindFirstValue("sub");
            return Guid.TryParse(sub, out _);
        }
    }

    public Guid UserId
    {
        get
        {
            var sub = accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? accessor.HttpContext?.User.FindFirstValue("sub");
            if (Guid.TryParse(sub, out var id))
                return id;
            throw new UnauthorizedAccessException("User is not authenticated.");
        }
    }
}
