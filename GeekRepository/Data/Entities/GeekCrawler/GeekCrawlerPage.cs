using MongoDB.Bson;

namespace GeekRepository.Data.Entities.GeekCrawler;

public class GeekCrawlerPage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RunId { get; set; }
    public string Origin { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string FinalUrl { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public bool RobotsAllowed { get; set; }
    public string? Html { get; set; }
    /** Clean article title (Readability). */
    public string? Title { get; set; }
    /** Clean article markdown (Readability → markdown). Legacy: the crawler no longer produces it. */
    public string? Markdown { get; set; }
    /** Clean semantic HTML fragment from the crawler's extractor. The corpus body. */
    public string? ContentHtml { get; set; }
    /**
     * The same content typed, in document order, for the chunker. Stored as a native BSON array
     * rather than a serialized string: the RAG Library reads each element as a document
     * (`render_block_text` calls `.get("kind")` per block), so a string blob fails at runtime.
     * Carried through untyped — the schema is defined by the crawler and consumed by the Library,
     * and a C# mirror here would be a third definition to keep in sync.
     */
    public BsonArray? Blocks { get; set; }
    /** Short plain excerpt from Readability when available. */
    public string? Excerpt { get; set; }
    /** Set by one-time Rag backfill when Markdown was derived from stored Html. */
    public DateTimeOffset? MarkdownBackfilledAt { get; set; }
    public string? FailureReason { get; set; }
    public DateTimeOffset CrawledAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
