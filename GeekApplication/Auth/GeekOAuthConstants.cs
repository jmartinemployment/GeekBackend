namespace GeekApplication.Auth;

/// <summary>
/// OAuth scopes, audiences, and first-party client IDs for service-to-service access.
/// See GeekRepository/plan/COMPREHENSIVE_IMPLEMENTATION_PLAN.md — Zero Trust / Client Credentials.
/// </summary>
public static class GeekOAuthConstants
{
    public const string GeekApiClientId = "geekapi";
    public const string InternalApiScope = "internal.api";
    public const string GeekRepositoryAudience = "geek-repository";
    public const string InternalServicePolicy = "InternalService";
}
