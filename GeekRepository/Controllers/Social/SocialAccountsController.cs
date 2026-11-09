using GeekApplication.Interfaces.Social;
using GeekApplication.Models.Social;
using GeekRepository.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Social;

[ApiController]
[Route("repo/social/accounts")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public sealed class SocialAccountsController : ControllerBase
{
    private readonly ISocialStore _social;

    public SocialAccountsController(ISocialStore social) => _social = social;

    [HttpPost]
    public async Task<IActionResult> Connect([FromBody] ConnectSocialAccountCommand command, CancellationToken ct)
    {
        if (command is null) return BadRequest("An account is required.");
        return SocialResults.ToAction(this, await _social.ConnectAccountAsync(command, ct));
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) =>
        SocialResults.ToAction(this, await _social.ListAccountsAsync(ct));

    [HttpGet("{accountId:guid}")]
    public async Task<IActionResult> Get(Guid accountId, CancellationToken ct) =>
        SocialResults.ToAction(this, await _social.GetAccountAsync(accountId, ct));

    /// <summary>The stored ciphertext, for GeekAPI to decrypt. Reachable only with the internal service key.</summary>
    [HttpGet("{accountId:guid}/credential")]
    public async Task<IActionResult> GetCredential(Guid accountId, CancellationToken ct) =>
        SocialResults.ToAction(this, await _social.GetCredentialAsync(accountId, ct));

    [HttpPut("{accountId:guid}/token")]
    public async Task<IActionResult> StoreToken(Guid accountId, [FromBody] StoreSocialTokenCommand command, CancellationToken ct)
    {
        if (command is null) return BadRequest("A token is required.");
        return SocialResults.ToAction(this, await _social.StoreTokenAsync(accountId, command, ct));
    }

    [HttpPost("{accountId:guid}/reconnect-required")]
    public async Task<IActionResult> MarkReconnectRequired(Guid accountId, CancellationToken ct) =>
        SocialResults.ToAction(this, await _social.MarkReconnectRequiredAsync(accountId, ct));

    [HttpPut("{accountId:guid}/settings")]
    public async Task<IActionResult> UpdateSettings(Guid accountId, [FromBody] UpdateSocialAccountSettingsCommand command, CancellationToken ct)
    {
        if (command is null) return BadRequest("Settings are required.");
        return SocialResults.ToAction(this, await _social.UpdateSettingsAsync(accountId, command, ct));
    }
}
