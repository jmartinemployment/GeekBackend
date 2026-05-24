using GeekApplication.Entities.OpenIddict;
using GeekApplication.Results;

namespace GeekApplication.Interfaces.OpenIddict;

public interface IOpenIddictApplicationRepository
{
    Task<Result<int>> CountAsync(CancellationToken cancellationToken = default);
    Task<Result<GeekOpenIddictApplication>> CreateAsync(GeekOpenIddictApplication application, CancellationToken cancellationToken = default);
    Task<Result<bool>> DeleteAsync(string id, CancellationToken cancellationToken = default);
    Task<Result<GeekOpenIddictApplication?>> FindByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<Result<GeekOpenIddictApplication?>> FindByClientIdAsync(string clientId, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<GeekOpenIddictApplication>>> FindByRedirectUriAsync(string uri, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<GeekOpenIddictApplication>>> FindByPostLogoutRedirectUriAsync(string uri, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<GeekOpenIddictApplication>>> ListAsync(int? count, int? offset, CancellationToken cancellationToken = default);
    Task<Result<GeekOpenIddictApplication>> UpdateAsync(GeekOpenIddictApplication application, CancellationToken cancellationToken = default);
}
