using System.Collections.Concurrent;
using GeekApplication.Interfaces.Crm;
using GeekApplication.Models.Crm;
using GeekApplication.Results;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace GeekAPI.Auth;

/// <summary>
/// How a request reaches /api/crm/*: either a server holding the X-API-Key (the receptionist, imports) or a signed-in
/// GeekOAuth user with the geekcrm.manage scope who belongs to a tenant. A bare valid token is not enough: GeekOAuth is
/// shared across Geek apps and GeekAPI does not validate audience, so the scope says who may call and tenant_users says
/// which tenant they work in and as what.
/// </summary>
public static class CrmAuthConstants
{
    public const string ManageScope = "geekcrm.manage";

    /// <summary>Set on HttpContext.Items by ApiKeyMiddleware once the X-API-Key has been checked and only then.</summary>
    public const string VerifiedApiKeyItem = "geek.api-key-verified";

    public const string CallerItem = "crm.caller";
}

/// <summary>Who is calling. A server is trusted with every tenant and every role; a signed-in user has exactly one tenant and a role.</summary>
public sealed record CrmCaller(bool IsServer, Guid? TenantId, string Role, Guid? UserId)
{
    public static CrmCaller Server { get; } = new(true, null, TenantUserRoles.Owner, null);

    public bool IsOwner => IsServer || Role == TenantUserRoles.Owner;
}

/// <summary>
/// The tenant and role of a signed-in user, read from GeekRepository and kept for at most a minute, so a member removed
/// from tenant_users loses access within a minute. Only a found membership is kept: a user added a moment ago is not kept
/// waiting, and a failed lookup is never remembered.
/// </summary>
public sealed class CrmMembershipCache(Func<Guid, CancellationToken, Task<Result<CrmMembership>>> lookup, TimeProvider clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<Guid, (CrmMembership Membership, DateTimeOffset Expires)> _kept = new();

    public async Task<Result<CrmMembership>> GetAsync(Guid userId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (_kept.TryGetValue(userId, out var hit) && hit.Expires > now)
            return Result<CrmMembership>.Success(hit.Membership);

        _kept.TryRemove(userId, out _);
        var found = await lookup(userId, ct).ConfigureAwait(false);
        if (found.IsSuccess) _kept[userId] = (found.Value!, now + Lifetime);
        return found;
    }
}

/// <summary>Marks a route only an owner (or a server) may call: the catalog, pricing, custom property definitions and imports.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class CrmOwnerOnlyAttribute : Attribute;

[AttributeUsage(AttributeTargets.Class)]
public sealed class CrmAccessAttribute : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var http = context.HttpContext;
        if (http.Items.TryGetValue(CrmAuthConstants.VerifiedApiKeyItem, out var verified) && verified is true)
        {
            http.Items[CrmAuthConstants.CallerItem] = CrmCaller.Server;
            return;
        }

        if (http.User.Identity?.IsAuthenticated != true)
        {
            context.Result = Refuse(StatusCodes.Status401Unauthorized, "Authentication is required.");
            return;
        }

        var scoped = http.User.Claims.Any(static c => c.Type is "scope" or "scp"
            && c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(CrmAuthConstants.ManageScope, StringComparer.Ordinal));
        if (!scoped)
        {
            context.Result = Refuse(StatusCodes.Status403Forbidden, $"The {CrmAuthConstants.ManageScope} scope is required.");
            return;
        }

        var sub = http.User.FindFirst("sub")?.Value;
        if (!Guid.TryParse(sub, out var userId))
        {
            context.Result = Refuse(StatusCodes.Status403Forbidden, "The token carries no user id.");
            return;
        }

        var membership = await http.RequestServices.GetRequiredService<CrmMembershipCache>().GetAsync(userId, http.RequestAborted).ConfigureAwait(false);
        if (!membership.IsSuccess)
        {
            context.Result = membership.ErrorCode == CrmFailureCodes.NotFound
                ? Refuse(StatusCodes.Status403Forbidden, "The user belongs to no tenant.")
                : Refuse(StatusCodes.Status503ServiceUnavailable, "Membership could not be checked.");
            return;
        }

        var caller = new CrmCaller(false, membership.Value!.TenantId, membership.Value.Role, userId);
        http.Items[CrmAuthConstants.CallerItem] = caller;
        if (!caller.IsOwner && context.ActionDescriptor.EndpointMetadata.OfType<CrmOwnerOnlyAttribute>().Any())
            context.Result = Refuse(StatusCodes.Status403Forbidden, "Only an owner may do that.");
    }

    private static ObjectResult Refuse(int status, string error) => new(new { success = false, error }) { StatusCode = status };
}
