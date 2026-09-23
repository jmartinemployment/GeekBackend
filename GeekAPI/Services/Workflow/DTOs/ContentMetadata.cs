namespace GeekAPI.Services.Workflow.DTOs;

/// <summary>Shared metadata used to stamp out both the TechnicalArticle and BlogPosting JSON+LD schemas.</summary>
public record ContentMetadata(
    string Headline,
    string Description,
    string AuthorName,
    string PublisherName,
    string PublisherLogoUrl,
    string CanonicalUrl,
    /// <summary>
    /// The article's own hero image.
    ///
    /// <para>
    /// At generation time there is no image -- this pipeline produces image <i>prompts</i>, and the
    /// picture is made afterwards. So this carries
    /// <see cref="ContentMetadataFactory.ArticleImagePlaceholder"/>, a visible token the operator
    /// replaces when the real asset exists.
    /// </para>
    ///
    /// <para>
    /// It used to be handed the publisher's logo, so every article ever generated declared the same
    /// company logo as its image -- an SVG with no intrinsic dimensions, and not representative of
    /// any article's content, which is the one thing schema.org's Article image is for.
    /// </para>
    /// </summary>
    string MainImageUrl,
    DateTime DatePublishedUtc,
    DateTime DateModifiedUtc,
    List<string> Keywords,
    int WordCount,
    /// <summary>
    /// Geographies the publisher serves, harvested from the client's own published JSON-LD by
    /// <c>JsonLdParserService</c>. Emitted as <c>areaServed</c> on the publisher.
    /// </summary>
    /// <remarks>
    /// Empty means the site declares none. Emit nothing in that case — an empty <c>areaServed</c>
    /// asserts "serves nowhere", which is worse than silence and is the mistake
    /// <c>GccV2BrandKitBuilder</c> already hardcodes.
    /// </remarks>
    IReadOnlyList<string>? AreaServed = null,
    /// <summary>
    /// The business type the client's own markup declares — <c>LocalBusiness</c>,
    /// <c>ProfessionalService</c>, <c>Organization</c> and so on.
    /// </summary>
    /// <remarks>
    /// Mirror it; never infer it. Promoting a business to <c>LocalBusiness</c> because it happens
    /// to have an address is fabrication in schema form.
    /// </remarks>
    string? PublisherType = null,
    /// <summary>Question/answer pairs this page actually contains, for <c>FAQPage</c>.</summary>
    /// <remarks>Emitted only when the page really has them — never invented to win a rich result.</remarks>
    IReadOnlyList<ContentFaqEntry>? Faq = null);

/// <summary>One question and its answer, as they appear on the page.</summary>
public record ContentFaqEntry(string Question, string Answer);
