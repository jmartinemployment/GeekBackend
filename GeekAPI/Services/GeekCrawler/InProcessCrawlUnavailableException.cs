namespace GeekAPI.Services.GeekCrawler;

/// <summary>
/// Thrown when something asks GeekAPI to crawl. It no longer does.
///
/// <para>
/// GeekAPI's in-process crawler produces runs nothing can index: <c>SameOriginBfsCrawler</c> runs no
/// extractor, so pages carry no <c>contentHtml</c> and no <c>blocks</c>, and the run never gets a
/// <c>ContentReadyAt</c> — which <c>mongo.find_smallest_content_ready_run</c> filters on. Since
/// GeekBackend 8713268 such a run is failed rather than completed, so nothing is silently lost; this
/// refusal is what stops the walk being taken at all.
/// </para>
///
/// <para>
/// Its own type rather than a bare <see cref="InvalidOperationException"/> so the schedule service
/// can recognise the refusal and disable the schedule, instead of matching on a message.
/// </para>
/// </summary>
public sealed class InProcessCrawlUnavailableException : InvalidOperationException
{
    /// <summary>The two intake shapes that work, named in every refusal.</summary>
    public const string WorkingShapes =
        "Crawl with the external crawler instead: `npm run crawl -- --seed <url> --type <type>` on "
        + "the operator machine, or POST to its loopback API on 127.0.0.1:8787.";

    public InProcessCrawlUnavailableException()
        : base("GeekAPI does not crawl. Its in-process crawler runs no extractor, so every page it "
            + "stores has no contentHtml and no blocks, and the run gets no ContentReadyAt — nothing "
            + "can ever index it. No run was created. " + WorkingShapes)
    {
    }
}
