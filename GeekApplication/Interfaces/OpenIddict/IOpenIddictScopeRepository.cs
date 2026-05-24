using GeekApplication.Entities.OpenIddict;
using GeekApplication.Results;

namespace GeekApplication.Interfaces.OpenIddict;

public interface IOpenIddictScopeRepository
{
    Task<Result<int>> CountAsync(CancellationToken cancellationToken = default);
    Task<Result<GeekOpenIddictScope>> CreateAsync(GeekOpenIddictScope scope, CancellationToken cancellationToken = default);
    Task<Result<bool>> DeleteAsync(string id, CancellationToken cancellationToken = default);
    Task<Result<GeekOpenIddictScope?>> FindByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<Result<GeekOpenIddictScope?>> FindByNameAsync(string name, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<GeekOpenIddictScope>>> ListAsync(int? count, int? offset, CancellationToken cancellationToken = default);
    Task<Result<GeekOpenIddictScope>> UpdateAsync(GeekOpenIddictScope scope, CancellationToken cancellationToken = default);
}
