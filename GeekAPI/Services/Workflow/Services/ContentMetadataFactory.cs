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
    /// <summary>
    /// What stands in for the hero image until one exists.
    ///
    /// The pipeline generates an image <i>prompt</i>, not a picture, so at this stage the URL is
    /// genuinely unknown. A visible placeholder is the operator's instruction to fill it in; the
    /// publisher's logo -- which is what this was -- looked like a real answer and was wrong on
    /// every page (Jeff, 2026-09-23: "you will not know what that is as at this stage it is only an
    /// image prompt, so use [article-image] as placeholder").
    /// </summary>
    public const string ArticleImagePlaceholder = "[article-image]";

    /// <remarks>
    /// AreaServed and PublisherType are not conditional. They sit on the <i>publisher</i> node, and
    /// the publisher of a page about a partner's product is still the operator -- "Geek At Your Spot
    /// serves these areas" is true whether the page's subject is Tipalti or AP automation. A flag
    /// once suppressed them for tool pages on the grounds that a partner page must not assert the
    /// operator's geography; that confused the page's subject with the page's publisher, and the
    /// effect was that the revenue-critical type shipped a generic Organization with no service
    /// areas while pillar and blog carried the real declared type and geography -- one publisher,
    /// two descriptions, in one site's markup (Jeff, 2026-09-23).
    /// </remarks>
    public static ContentMetadata For(
        ProjectGenerationContext context,
        string title,
        string metaDescription,
        string canonicalUrl,
        List<string> keywords,
        ContentDocument document,
        DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        return new ContentMetadata(
            title,
            metaDescription,
            context.AuthorName,
            context.PublisherName,
            context.PublisherLogoUrl,
            canonicalUrl,
            ArticleImagePlaceholder,
            now,
            now,
            keywords,
            ContentDocumentText.CountWords(document),
            AreaServed: context.SiteAreaServed,
            PublisherType: context.SitePublisherType,
            Faq: ContentDocumentText.ExtractFaqPairs(document));
    }
}
