using GeekAPI.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;

namespace GeekAPI.Controllers.Admin;

[ApiController]
[Route("api/admin/clients")]
[Authorize(
    AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme,
    Roles = "admin")]
public sealed class ClientsController : ControllerBase
{
    private readonly IOpenIddictApplicationManager _applications;

    public ClientsController(IOpenIddictApplicationManager applications) =>
        _applications = applications;

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateClientRequest request, CancellationToken cancellationToken)
    {
        if (await _applications.FindByClientIdAsync(request.ClientId, cancellationToken) is not null)
            return Conflict(new { success = false, error = $"Client '{request.ClientId}' already exists." });

        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = request.ClientId,
            DisplayName = request.DisplayName,
            ClientSecret = request.ClientSecret
        };

        if (request.IsPublic)
        {
            OpenIddictClientPermissionBuilder.ApplyPublicAuthorizationCodeClient(
                descriptor,
                request.RedirectUris,
                request.Scopes);
        }
        else if (request.AllowIntrospection)
        {
            OpenIddictClientPermissionBuilder.ApplyConfidentialIntrospection(descriptor);
        }
        else
        {
            OpenIddictClientPermissionBuilder.ApplyConfidentialClientCredentials(
                descriptor,
                request.Scopes ?? ["mcp.tools"]);
        }

        await _applications.CreateAsync(descriptor, cancellationToken);
        return Ok(new { success = true, clientId = request.ClientId });
    }

    [HttpDelete("{clientId}")]
    public async Task<IActionResult> Delete(string clientId, CancellationToken cancellationToken)
    {
        var application = await _applications.FindByClientIdAsync(clientId, cancellationToken);
        if (application is null)
            return NotFound();

        await _applications.DeleteAsync(application, cancellationToken);
        return NoContent();
    }
}

/// <summary>
/// Register a new OAuth client. Public clients get PKCE authorization-code + refresh permissions.
/// Confidential clients default to client_credentials unless AllowIntrospection is true.
/// </summary>
public sealed record CreateClientRequest(
    string ClientId,
    string DisplayName,
    bool IsPublic,
    string? ClientSecret,
    IReadOnlyList<string>? RedirectUris,
    IReadOnlyList<string>? Scopes,
    bool AllowIntrospection = false);
