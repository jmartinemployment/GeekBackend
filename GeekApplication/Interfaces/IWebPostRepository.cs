using GeekApplication.Models.WebPost;

namespace GeekApplication.Interfaces;

public interface IWebPostRepository
{
    Task<WebPostFlatDto?> GetBySlugAsync(string slug, CancellationToken ct = default);

    Task<WebPostFlatDto> UpsertAsync(UpsertWebPostCommand command, CancellationToken ct = default);

    Task<bool> DeleteAsync(string slug, CancellationToken ct = default);
}
