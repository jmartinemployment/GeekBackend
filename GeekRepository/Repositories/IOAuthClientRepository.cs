using GeekBackend.Data.Models;
using GeekBackend.Data.Results;

namespace GeekRepository.Repositories;

public interface IOAuthClientRepository
{
    Task<Result<OauthClient>> FindByClientIdAsync(string clientId);
    Task<Result<OauthClient>> CreateAsync(CreateOAuthClientRequest request);
}

public class CreateOAuthClientRequest
{
    public string ClientId { get; set; } = null!;
    public string? ClientSecret { get; set; }
    public List<string> RedirectUris { get; set; } = [];
    public List<string> GrantTypes { get; set; } = [];
    public List<string> ResponseTypes { get; set; } = [];
    public string Scope { get; set; } = null!;
    public string TokenEndpointAuthMethod { get; set; } = null!;
}
