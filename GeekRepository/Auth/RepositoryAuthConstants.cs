namespace GeekRepository.Auth;

public static class RepositoryAuthConstants
{
    public const string InternalServicePolicy = "InternalService";
    public const string InternalApiScope = "internal.api";

    /// <summary>The policy scheme that forwards each request to the credential its path calls for.</summary>
    public const string KeySelectorScheme = "RepositoryKeySelector";

    /// <summary>Routes under this prefix answer to X-Internal-Api-Key; every other route answers to X-Repo-Key.</summary>
    public const string InternalRoutePrefix = "/internal/v1";
}
