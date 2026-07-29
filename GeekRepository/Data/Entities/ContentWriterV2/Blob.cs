namespace GeekRepository.Data.Entities.ContentWriterV2;

/// <summary>
/// Generic JSON-blob storage for the content-writer-v2 product's own persistence layer
/// (IPersistenceStore). One row per (Collection, ItemId) pair — Collection is an arbitrary
/// caller-chosen name ("projects", "clients", ...), so new collections need no schema change.
/// </summary>
public class Blob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Collection { get; set; } = string.Empty;
    public Guid ItemId { get; set; }
    public string DataJson { get; set; } = "{}";
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
