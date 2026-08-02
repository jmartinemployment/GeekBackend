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
    /// Current authenticated user. Does not invent Demo clients — callers must create real clients.
    /// </summary>
    [HttpGet("me")]
    public async Task<ActionResult<UserInfoResponse>> GetCurrentUser(CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Unauthorized();

        var userId = _currentUser.UserId;
        var email = _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Email)
                    ?? $"user-{userId:N}@geekatyourspot.com";

        await EnsureDefaultWorkspaceAsync(ct);

        var clients = await _repo.GetClientsByWorkspaceIdAsync(DefaultWorkspaceId, ct);
        var client = clients.FirstOrDefault(c =>
            c.Id != Guid.Empty
            && !c.Name.Contains("(Demo)", StringComparison.OrdinalIgnoreCase)
            && !c.Name.Contains("Smoke", StringComparison.OrdinalIgnoreCase));

        return Ok(new UserInfoResponse(
            Id: userId,
            Email: email,
            ClientId: client?.Id ?? Guid.Empty,
            WorkspaceId: DefaultWorkspaceId
        ));
    }

    private async Task EnsureDefaultWorkspaceAsync(CancellationToken ct)
    {
        var existing = await _repo.GetWorkspaceByIdAsync(DefaultWorkspaceId, ct);
        if (existing is not null && existing.Id != Guid.Empty)
            return;

        _logger.LogInformation("Creating default workspace {WorkspaceId}", DefaultWorkspaceId);
        await _repo.CreateWorkspaceAsync(
            new CreateWorkspaceCommand("Default Workspace", Guid.Empty, DefaultWorkspaceId), ct);

        existing = await _repo.GetWorkspaceByIdAsync(DefaultWorkspaceId, ct);
        if (existing is null || existing.Id == Guid.Empty)
            throw new InvalidOperationException($"Failed to ensure default workspace {DefaultWorkspaceId}");
    }

    public record UserInfoResponse(
        Guid Id,
        string Email,
        Guid ClientId,
        Guid WorkspaceId
    );
}
