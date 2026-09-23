using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Domain.Entities;

namespace GeekAPI.Services.Workflow.Services;

/// <summary>
/// Assembles the <see cref="ContentMetadata"/> every schema builder takes.
///
/// This was written inline seven times -- five in ContentGenerationOrchestrator, once for the tool
/// page, once for the blog -- with the same eight context fields copied each time and only the
/// title, description, canonical URL, keywords, word count and document varying. The cost showed up
/// immediately: the blog copy omitted AreaServed and PublisherType, which all five orchestrator
/// copies set, because there was nothing to omit them *from*.
/// </summary>
public static class ContentMetadataFactory
{
    /// <param name="includePublisherGeography">
    /// False for a partner page. AreaServed and PublisherType describe the operator's own site, and
    /// asserting them on a page about a third party's product claims something untrue -- the tool
    /// page has always left them unset for that reason.
    /// </param>
    public static ContentMetadata For(
        ProjectGenerationContext context,
        string title,
        string metaDescription,
        string canonicalUrl,
        List<string> keywords,
        ContentDocument document,
        DateTime? nowUtc = null,
        bool includePublisherGeography = true)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        return new ContentMetadata(
            title,
            metaDescription,
            context.AuthorName,
            context.PublisherName,
            context.PublisherLogoUrl,
            canonicalUrl,
            context.PublisherLogoUrl,
            now,
            now,
            keywords,
            ContentDocumentText.CountWords(document),
            AreaServed: includePublisherGeography ? context.SiteAreaServed : null,
            PublisherType: includePublisherGeography ? context.SitePublisherType : null,
            Faq: ContentDocumentText.ExtractFaqPairs(document));
    }
}
