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
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = request.ClientId,
            DisplayName = request.DisplayName,
            ClientType = request.IsPublic
                ? OpenIddictConstants.ClientTypes.Public
                : OpenIddictConstants.ClientTypes.Confidential,
            ClientSecret = request.ClientSecret
        };

        foreach (var uri in request.RedirectUris ?? [])
            descriptor.RedirectUris.Add(new Uri(uri, UriKind.Absolute));

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

public sealed record CreateClientRequest(
    string ClientId,
    string DisplayName,
    bool IsPublic,
    string? ClientSecret,
    IReadOnlyList<string>? RedirectUris);
