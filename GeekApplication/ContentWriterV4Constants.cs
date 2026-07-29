namespace GeekApplication;

/// <summary>
/// Single source of truth for v4 constants that will change at the Phase 5 OAuth cutover.
/// DevUserId stands in for the OAuth `sub` claim until real auth is wired in — every v4
/// controller/repository that needs an owner_id references this constant, so the Phase 5
/// swap is a one-line change here rather than a grep-and-replace across files.
/// </summary>
public static class ContentWriterV4Constants
{
    public static readonly Guid DevUserId = Guid.Parse("00000000-0000-0000-0000-000000000001");
}
