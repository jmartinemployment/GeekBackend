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
    /** Page title from the crawler's deterministic extractor (`readTitle`), read off the blocks. */
    public string? Title { get; set; }
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
    /**
     * Short plain excerpt from the crawler's deterministic extractor (`readExcerpt`), read off the
     * blocks. Not Readability: that was removed because it is an *article* extractor and most
     * crawled pages are not articles -- measured, it returned 9% of one live page's prose, 44% of
     * another and 175% of a third (`Geek-Crawler-v2/src/crawl/extract-content.ts` header).
     */
    public string? Excerpt { get; set; }
    public string? FailureReason { get; set; }
    public DateTimeOffset CrawledAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
