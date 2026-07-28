using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekApplication.Models.ContentWriterV3;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace GeekAPI.Controllers.ContentWriterV3;

[ApiController]
[Route("api/content-writer/v3/auth")]
public class AuthController : ControllerBase
{
    private static readonly Guid DefaultWorkspaceId = Guid.Parse("550e8400-e29b-41d4-a716-446655440001");

    private readonly ICurrentUserContext _currentUser;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly HttpContentWriterV3Repository _repo;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        ICurrentUserContext currentUser,
        IHttpContextAccessor httpContextAccessor,
        HttpContentWriterV3Repository repo,
        ILogger<AuthController> logger)
    {
        _currentUser = currentUser;
        _httpContextAccessor = httpContextAccessor;
        _repo = repo;
        _logger = logger;
    }

    /// <summary>
    /// Get current authenticated user info. Ensures the default demo workspace + client exist
    /// so first-time sign-in is not an empty shell.
    /// </summary>
    [HttpGet("me")]
    public async Task<ActionResult<UserInfoResponse>> GetCurrentUser(CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Unauthorized();

        var userId = _currentUser.UserId;
        var email = _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Email)
                    ?? $"user-{userId:N}@geekatyourspot.com";

        var workspaceId = await EnsureDefaultWorkspaceAsync(ct);
        var client = await EnsureDemoClientAsync(workspaceId, email, ct);

        // #region agent log
        _logger.LogInformation(
            "CW_AUTH_ME session=2d6b04 hypothesis=H23 user={UserId} workspace={WorkspaceId} client={ClientId} ensured=true",
            userId, workspaceId, client.Id);
        // #endregion

        return Ok(new UserInfoResponse(
            Id: userId,
            Email: email,
            ClientId: client.Id,
            WorkspaceId: workspaceId
        ));
    }

    private async Task<Guid> EnsureDefaultWorkspaceAsync(CancellationToken ct)
    {
        var existing = await _repo.GetWorkspaceByIdAsync(DefaultWorkspaceId, ct);
        if (existing is not null)
            return existing.Id;

        _logger.LogInformation("Creating default Demo Workspace {WorkspaceId}", DefaultWorkspaceId);
        var created = await _repo.CreateWorkspaceAsync(
            new CreateWorkspaceCommand("Demo Workspace", DefaultWorkspaceId), ct);
        return created.Id;
    }

    private async Task<ClientDto> EnsureDemoClientAsync(Guid workspaceId, string email, CancellationToken ct)
    {
        var clients = await _repo.GetClientsByWorkspaceIdAsync(workspaceId, ct);
        if (clients.Count > 0)
            return clients[0];

        var name = string.IsNullOrWhiteSpace(email) ? "Demo Client" : $"{email.Split('@')[0]} (Demo)";
        _logger.LogInformation("Creating demo client in workspace {WorkspaceId}", workspaceId);
        return await _repo.CreateClientAsync(new CreateClientCommand(workspaceId, name), ct);
    }

    public record UserInfoResponse(
        Guid Id,
        string Email,
        Guid ClientId,
        Guid WorkspaceId
    );
}
