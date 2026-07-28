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

        await EnsureDefaultWorkspaceAsync(ct);
        var client = await EnsureDemoClientAsync(email, ct);

        // #region agent log
        _logger.LogInformation(
            "CW_AUTH_ME session=2d6b04 hypothesis=H23 user={UserId} workspace={WorkspaceId} client={ClientId} ensured=true",
            userId, DefaultWorkspaceId, client.Id);
        // #endregion

        return Ok(new UserInfoResponse(
            Id: userId,
            Email: email,
            ClientId: client.Id,
            WorkspaceId: DefaultWorkspaceId
        ));
    }

    private async Task EnsureDefaultWorkspaceAsync(CancellationToken ct)
    {
        var existing = await _repo.GetWorkspaceByIdAsync(DefaultWorkspaceId, ct);
        if (existing is not null && existing.Id != Guid.Empty)
            return;

        _logger.LogInformation("Creating default Demo Workspace {WorkspaceId}", DefaultWorkspaceId);
        await _repo.CreateWorkspaceAsync(
            new CreateWorkspaceCommand("Demo Workspace", DefaultWorkspaceId), ct);

        existing = await _repo.GetWorkspaceByIdAsync(DefaultWorkspaceId, ct);
        if (existing is null || existing.Id == Guid.Empty)
            throw new InvalidOperationException($"Failed to ensure default workspace {DefaultWorkspaceId}");
    }

    private async Task<ClientDto> EnsureDemoClientAsync(string email, CancellationToken ct)
    {
        var clients = await _repo.GetClientsByWorkspaceIdAsync(DefaultWorkspaceId, ct);
        var usable = clients.FirstOrDefault(c => c.Id != Guid.Empty);
        if (usable is not null)
            return usable;

        var name = string.IsNullOrWhiteSpace(email) ? "Demo Client" : $"{email.Split('@')[0]} (Demo)";
        _logger.LogInformation("Creating demo client in workspace {WorkspaceId}", DefaultWorkspaceId);
        var created = await _repo.CreateClientAsync(new CreateClientCommand(DefaultWorkspaceId, name), ct);
        if (created.Id == Guid.Empty)
        {
            clients = await _repo.GetClientsByWorkspaceIdAsync(DefaultWorkspaceId, ct);
            usable = clients.FirstOrDefault(c => c.Id != Guid.Empty)
                ?? throw new InvalidOperationException("Failed to ensure demo client");
            return usable;
        }

        return created;
    }

    public record UserInfoResponse(
        Guid Id,
        string Email,
        Guid ClientId,
        Guid WorkspaceId
    );
}
