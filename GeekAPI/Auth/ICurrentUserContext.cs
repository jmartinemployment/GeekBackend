namespace GeekAPI.Auth;

public interface ICurrentUserContext
{
    Guid UserId { get; }
    bool IsAuthenticated { get; }
}
