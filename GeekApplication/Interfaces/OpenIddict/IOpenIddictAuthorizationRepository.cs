using GeekApplication.Entities.OpenIddict;
using GeekApplication.Results;

namespace GeekApplication.Interfaces.OpenIddict;

public interface IOpenIddictAuthorizationRepository
{
    Task<Result<int>> CountAsync(CancellationToken cancellationToken = default);
    Task<Result<GeekOpenIddictAuthorization>> CreateAsync(GeekOpenIddictAuthorization authorization, CancellationToken cancellationToken = default);
    Task<Result<bool>> DeleteAsync(string id, CancellationToken cancellationToken = default);
    Task<Result<GeekOpenIddictAuthorization?>> FindByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<GeekOpenIddictAuthorization>>> ListAsync(int? count, int? offset, CancellationToken cancellationToken = default);
    Task<Result<GeekOpenIddictAuthorization>> UpdateAsync(GeekOpenIddictAuthorization authorization, CancellationToken cancellationToken = default);
}
