using GeekApplication.Entities.OpenIddict;
using GeekApplication.Results;

namespace GeekApplication.Interfaces.OpenIddict;

public interface IOpenIddictTokenRepository
{
    Task<Result<int>> CountAsync(CancellationToken cancellationToken = default);
    Task<Result<GeekOpenIddictToken>> CreateAsync(GeekOpenIddictToken token, CancellationToken cancellationToken = default);
    Task<Result<bool>> DeleteAsync(string id, CancellationToken cancellationToken = default);
    Task<Result<GeekOpenIddictToken?>> FindByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<Result<GeekOpenIddictToken?>> FindByReferenceIdAsync(string referenceId, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<GeekOpenIddictToken>>> ListAsync(int? count, int? offset, CancellationToken cancellationToken = default);
    Task<Result<GeekOpenIddictToken>> UpdateAsync(GeekOpenIddictToken token, CancellationToken cancellationToken = default);
    Task<Result<int>> RevokeBySubjectAsync(string subject, CancellationToken cancellationToken = default);
}
