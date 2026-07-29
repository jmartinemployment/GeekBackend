using GeekApplication.Models.ContentWriterV4;

namespace GeekApplication.Interfaces.ContentWriterV4;

public interface IDocumentRepository
{
    Task<DocumentDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<DocumentDto>> GetByOwnerIdAsync(Guid ownerId, CancellationToken ct = default);
    Task<DocumentDto> CreateAsync(CreateDocumentCommand command, CancellationToken ct = default);
    Task<DocumentDto> UpdateAsync(UpdateDocumentCommand command, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
