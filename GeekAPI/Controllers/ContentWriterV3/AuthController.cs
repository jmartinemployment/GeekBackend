using GeekAPI.Auth;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace GeekAPI.Controllers.ContentWriterV3;

[ApiController]
[Route("api/content-writer/v3/auth")]
public class AuthController : ControllerBase
{
    private readonly ICurrentUserContext _currentUser;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        ICurrentUserContext currentUser,
        IHttpContextAccessor httpContextAccessor,
        ILogger<AuthController> logger)
    {
        _currentUser = currentUser;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    /// <summary>
    /// Get current authenticated user info
    /// </summary>
    [HttpGet("me")]
    public ActionResult<UserInfoResponse> GetCurrentUser()
    {
        if (!_currentUser.IsAuthenticated)
            return Unauthorized();

        var userId = _currentUser.UserId;
        var email = _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Email)
                    ?? $"user-{userId:N}@geekatyourspot.com";

        // For now, map userId to clientId deterministically
        // In production, this should query a Users table to get the actual clientId
        var clientId = userId; // For testing: use userId as clientId
        var workspaceId = Guid.Parse("550e8400-e29b-41d4-a716-446655440001"); // Default workspace

        _logger.LogInformation("User {UserId} authenticated", userId);

        var response = new UserInfoResponse(
            Id: userId,
            Email: email,
            ClientId: clientId,
            WorkspaceId: workspaceId
        );

        return Ok(response);
    }

    public record UserInfoResponse(
        Guid Id,
        string Email,
        Guid ClientId,
        Guid WorkspaceId
    );
}
