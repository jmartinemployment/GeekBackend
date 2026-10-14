using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using GeekAPI.Services.ContentCreator.Generation;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Services;

namespace GeekAPI.Services.ContentCreator;

/// <summary>What a slide of the deck is for, in the order a deck holds them.</summary>
public enum GccDeckSlideKind
{
    /// <summary>The first slide: a title and one line.</summary>
    Cover,

    /// <summary>What goes wrong in the work today and what it costs.</summary>
    Problem,

    /// <summary>One partner tool: the failure it answers and what is done about it.</summary>
    Partner,

    /// <summary>How the pieces fit, in the order they happen.</summary>
    Approach,

    /// <summary>The booking words and the operator's questions. Built by code, never written.</summary>
    Next,
}

/// <summary>One slide of a deck before a word of it is written: what it is for and who makes it.</summary>
/// <param name="Kind">What the slide is for.</param>
/// <param name="Partner">The partner tool a <see cref="GccDeckSlideKind.Partner"/> slide is about, as
/// the writer is told to spell it. Null on every other slide.</param>
public sealed record GccDeckSlidePlan(GccDeckSlideKind Kind, string? Partner = null)
{
    /// <summary>
    /// True for the one slide no request writes: the last, which code builds from the publisher's
    /// booking words and the Brief's questions (<see cref="GccClosing"/>).
    /// </summary>
    public bool BuiltByCode => Kind == GccDeckSlideKind.Next;
}

/// <summary>
/// "PDF / LinkedIn document": a deck of slides written as a page of the project, and the LinkedIn post
/// that carries it (<c>plans/enable-pdf.md</c>). This class holds what a deck is before it is written:
/// which slides it has, how many partners it takes, and the shape of the two replies that write it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not a repurpose</b> (Jeff, 2026-10-08). A deck is written from the Brief and the research, like
/// the Pillar. No page is its source.
/// </para>
/// <para>
/// <b>It names every partner</b> (Jeff, 2026-10-08: "Yes, it should incorporate"). Each partner has a
/// slide of its own, so the number of slides follows from the number of partners and from nothing the
/// writer decides.
/// </para>
/// <para>
/// <b>The writer returns content, never markup.</b> A slide's reply is a title and its lines, as
/// strings. Code makes the document every page is from them (<c>ContentDocument</c>: the cover is the
/// opening, each slide a top-level section whose heading is the slide's title and whose one list holds
/// its lines), and the PDF is made from that document.
/// </para>
/// </remarks>
public static class GccLinkedInDocument
{
    /// <summary>The type as the form labels it on Generate's "Content type" list.</summary>
    public const string Label = "PDF / LinkedIn document";

    /// <summary>
    /// Whether a content type is the deck's, however it is spelled ("linkedin-document",
    /// "linkedinDocument"). The one place that is asked.
    /// </summary>
    public static bool IsType(string? contentType) =>
        GccGenerationCoordinator.LettersOf(contentType ?? string.Empty) == GccGenerationCoordinator.LinkedInDocumentType;

    /// <summary>
    /// The slides of a deck for these partners, in order: the cover, the problem, one for each partner
    /// in the project's order, the approach, what to do next.
    /// </summary>
    /// <param name="partners">The project's partner tools, as the writer is told to spell them
    /// (<see cref="GccRequiredToolMentions.For"/>).</param>
    /// <param name="lastSlides">How many slides what to do next takes: the slides
    /// <see cref="GccLinkedInDocumentPdfRenderer.LastSlides"/> made of <see cref="NextSlide"/>, the
    /// Brief's questions carried on to as many as they need to be read; none when the publisher has no
    /// booking words.</param>
    public static IReadOnlyList<GccDeckSlidePlan> Slides(IReadOnlyList<string> partners, int lastSlides) =>
    [
        new(GccDeckSlideKind.Cover),
        new(GccDeckSlideKind.Problem),
        .. Named(partners).Select(partner => new GccDeckSlidePlan(GccDeckSlideKind.Partner, partner)),
        new(GccDeckSlideKind.Approach),
        .. Enumerable.Repeat(new GccDeckSlidePlan(GccDeckSlideKind.Next), Math.Max(0, lastSlides)),
    ];

    /// <summary>
    /// The slides a request writes after the cover: the problem, each partner's, the approach. The
    /// cover is written with the post, and the last slides are built by code.
    /// </summary>
    public static IReadOnlyList<GccDeckSlidePlan> WrittenAfterTheCover(IReadOnlyList<string> partners) =>
        [.. Slides(partners, lastSlides: 0).Where(slide => slide.Kind != GccDeckSlideKind.Cover)];

    /// <summary>
    /// Why a deck cannot be written for this many partners, or null when it can. Asked before a
    /// request is paid for.
    /// </summary>
    /// <remarks>
    /// A deck has a slide for each partner and four besides, and LinkedIn readers leave a document
    /// that runs long: six to twelve slides is two to eight partners. One partner is a tool page, not
    /// a deck about the partners; nine would be thirteen slides. The count is said back with the
    /// names, so the operator sees which partners the project holds. The Brief's questions are not
    /// counted against it: what the operator typed is carried on to another slide when it does not
    /// fit one (<see cref="GccLinkedInDocumentPdfRenderer.LastSlides"/>), and never refused or cut.
    /// </remarks>
    public static string? PartnerRefusal(IReadOnlyList<string> partners)
    {
        var named = Named(partners);
        if (named.Count >= ContentLengthTargets.LinkedInDocumentMinPartners
            && named.Count <= ContentLengthTargets.LinkedInDocumentMaxPartners)
        {
            return null;
        }

        var has = named.Count switch
        {
            0 => "this project has none",
            1 => $"this project has 1 ({named[0]})",
            _ => $"this project has {named.Count} ({string.Join(", ", named)})",
        };
        return $"Refused: a {Label} has one slide for each partner tool and takes "
            + $"{ContentLengthTargets.LinkedInDocumentMinPartners} to {ContentLengthTargets.LinkedInDocumentMaxPartners} "
            + $"partners; {has}. Nothing was written for it.";
    }

    /// <summary>
    /// The cover as the opening of the page's document: its title is the heading, its line the one
    /// paragraph under it.
    /// </summary>
    public static Section CoverSection(CoverReply cover) =>
        new(
            Tag: "h2",
            Heading: cover.Title.Trim(),
            Paragraphs: [new TextParagraph([new Run(cover.Line.Trim())])],
            Href: null,
            Children: []);

    /// <summary>
    /// The cover read back from a document's opening, or null when there is no opening. What the
    /// slides request is shown of the cover already written.
    /// </summary>
    public static CoverReply? CoverOf(Section? opening) =>
        opening is null
            ? null
            : new CoverReply(
                opening.Heading,
                string.Join(
                    " ",
                    opening.Paragraphs
                        .OfType<TextParagraph>()
                        .Select(paragraph => string.Concat(paragraph.Runs.Select(run => run.Text)).Trim())
                        .Where(text => text.Length > 0)));

    /// <summary>
    /// One written slide as a top-level section of the page's document: its title is the heading and
    /// its lines are the items of the section's one list.
    /// </summary>
    public static Section SlideSection(SlideReply slide) =>
        new(
            Tag: "h2",
            Heading: slide.Title.Trim(),
            Paragraphs:
            [
                new ListParagraph(
                    Ordered: false,
                    Items: [.. slide.Lines
                        .Where(line => !string.IsNullOrWhiteSpace(line))
                        .Select(line => (IReadOnlyList<Run>)[new Run(line.Trim())])]),
            ],
            Href: null,
            Children: []);

    /// <summary>
    /// The cover-and-post reply, read. A reply that cannot be read, or that leaves the title, the line
    /// or the post blank, is refused with the reason; nothing is sent again.
    /// </summary>
    public static CoverAndPostReply ReadCoverAndPost(string? reply) =>
        LlmResponseJsonParser.Parse<CoverAndPostReply>(
            reply ?? string.Empty,
            "the deck's cover and post",
            "cover and post",
            static read =>
                read.Cover is null ? "the reply carried no cover"
                : string.IsNullOrWhiteSpace(read.Cover.Title) ? "the cover carried no title"
                : string.IsNullOrWhiteSpace(read.Cover.Line) ? "the cover carried no line"
                : string.IsNullOrWhiteSpace(read.Post) ? "the reply carried no post"
                : null);

    /// <summary>
    /// The slides reply, read: one slide for each assigned, in the order assigned.
    /// </summary>
    /// <param name="reply">The model's reply.</param>
    /// <param name="assigned">How many slides the request assigned.</param>
    /// <remarks>
    /// A reply with another number of slides is refused, not fitted: a slide belongs to the partner its
    /// place in the list gives it, so one slide too few or too many leaves no way to say which partner
    /// each is about. A slide with no title, or with no line, is refused for the same reason a page
    /// with an empty section is.
    /// </remarks>
    public static IReadOnlyList<SlideReply> ReadSlides(string? reply, int assigned) =>
        LlmResponseJsonParser.Parse<SlidesReply>(
            reply ?? string.Empty,
            "the deck's slides",
            "slides",
            read =>
            {
                if (read.Slides is null) return "the reply carried no slides";
                if (read.Slides.Count != assigned)
                {
                    return $"the reply carried {read.Slides.Count} slide(s) and {assigned} were assigned";
                }

                for (var i = 0; i < read.Slides.Count; i++)
                {
                    var slide = read.Slides[i];
                    if (slide is null || string.IsNullOrWhiteSpace(slide.Title)) return $"slide {i + 1} carried no title";
                    if (slide.Lines is null || !slide.Lines.Any(line => !string.IsNullOrWhiteSpace(line)))
                    {
                        return $"slide {i + 1} carried no line";
                    }
                }

                return null;
            }).Slides;

    /// <summary>
    /// The deck's last slide, what to do next, built by code from the publisher's booking words and
    /// the operator's questions; or null when the publisher has no scheduler or no wording for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same words every page ends on (<see cref="GccClosing"/>), as a slide: the booking words are
    /// its title, the questions from the Brief's "One question per line" its lines, and the site's
    /// address is printed under them as text. No request writes it and no writer is shown the
    /// questions.
    /// </para>
    /// <para>
    /// <b>No link.</b> LinkedIn does not make a link inside a document clickable, so the address is
    /// words on the slide. The links are in the first comment (<see cref="FirstComment"/>).
    /// </para>
    /// <para>
    /// <b>One slide as written here, and as many as it needs in the deck.</b> Every question goes on
    /// it, however many the Brief holds; <see cref="GccLinkedInDocumentPdfRenderer.LastSlides"/> then
    /// carries them on to further slides with the same booking words when one slide cannot hold them
    /// at a size that reads on a phone.
    /// </para>
    /// </remarks>
    public static Section? NextSlide(CompanyProfileOptions company, IReadOnlyList<string> questions)
    {
        if (string.IsNullOrWhiteSpace(company.ConsultationAnchorHref)) return null;

        var asked = questions.Select(question => question.Trim()).Where(question => question.Length > 0).ToList();
        string? title;
        if (asked.Count == 0)
        {
            title = company.ConsultationClosingLinkWithoutQuestions?.Trim();
        }
        else
        {
            var lead = company.ConsultationClosingLead?.Trim();
            var link = company.ConsultationClosingLink?.Trim();
            title = string.IsNullOrWhiteSpace(lead) || string.IsNullOrWhiteSpace(link) ? null : $"{lead} {link}";
        }

        if (string.IsNullOrWhiteSpace(title)) return null;

        var paragraphs = new List<Paragraph>();
        if (asked.Count > 0)
        {
            paragraphs.Add(new ListParagraph(false, [.. asked.Select(question => (IReadOnlyList<Run>)[new Run(question)])]));
        }

        var address = SiteAddress(company);
        if (address.Length > 0) paragraphs.Add(new TextParagraph([new Run(address)]));

        return new Section(Tag: "h2", Heading: title, Paragraphs: paragraphs, Href: null, Children: []);
    }

    /// <summary>The publisher's site as a reader would type it: "geekatyourspot.com". Empty when the site's address is not configured.</summary>
    public static string SiteAddress(CompanyProfileOptions company)
    {
        if (SiteOf(company) is not { } site) return string.Empty;
        var host = site.Host;
        return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
    }

    /// <summary>
    /// The publisher's site, from the address its articles are published under, or null when that is
    /// not a web address. A path alone is not one: on Linux and macOS "/use-cases" parses as an
    /// absolute address of a file, which would make every link a file's.
    /// </summary>
    private static Uri? SiteOf(CompanyProfileOptions company) =>
        Uri.TryCreate(company.ArticleBaseUrl?.Trim(), UriKind.Absolute, out var site)
        && (site.Scheme == Uri.UriSchemeHttps || site.Scheme == Uri.UriSchemeHttp)
            ? site
            : null;

    /// <summary>One link of the first comment: what it is called, and where it leads.</summary>
    /// <param name="Label">The booking words, or a partner tool's name.</param>
    /// <param name="Href">The link as the publisher's own pages carry it: the scheduler's anchor, or a
    /// tool page's path. What the run compares against the tool pages the project has.</param>
    /// <param name="Url">The whole address, as it is pasted into LinkedIn.</param>
    public sealed record CommentLink(string Label, string Href, string Url);

    /// <summary>
    /// The links that go with a deck, in the order they are listed: the booking link, then each
    /// partner's tool page on the publisher's site. Empty when the site's address is not configured.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>They go in the first comment, not in the post.</b> Jeff's <c>social</c> skill, on LinkedIn:
    /// "Links in the main post (kills reach)" and "Put links in comments, not post body". The plan
    /// had put them under the post; his skill decides.
    /// </para>
    /// <para>
    /// Put there by code. The writer writes no link and no address (Jeff, 2026-10-10), and is shown
    /// neither of these.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<CommentLink> Links(
        CompanyProfileOptions company, IReadOnlyList<GccPartnerToolPage> partnerPages)
    {
        if (SiteOf(company) is not { } site) return [];
        var origin = site.GetLeftPart(UriPartial.Authority);

        var links = new List<CommentLink>();
        var anchor = company.ConsultationAnchorHref?.Trim();
        var book = company.ConsultationClosingLinkWithoutQuestions?.Trim();
        if (!string.IsNullOrWhiteSpace(anchor) && !string.IsNullOrWhiteSpace(book))
        {
            links.Add(new CommentLink(book, anchor, $"{origin}/{anchor}"));
        }

        foreach (var page in partnerPages)
        {
            if (string.IsNullOrWhiteSpace(page.Path)) continue;
            var path = page.Path.StartsWith('/') ? page.Path : $"/{page.Path}";
            links.Add(new CommentLink(page.ProductName, path, $"{origin}{path}"));
        }

        return links;
    }

    /// <summary>
    /// The first comment under the post, as text to paste: the booking link on a line of its own,
    /// then a line for each partner's page. Empty when there is no link to list.
    /// </summary>
    public static string FirstComment(IReadOnlyList<CommentLink> links)
    {
        if (links.Count == 0) return string.Empty;
        var lines = links.Select(link => $"{link.Label}: {link.Url}").ToList();
        // The booking link stands apart from the partners' pages when both are there.
        return links.Count > 1 && links[0].Href.StartsWith('#')
            ? $"{lines[0]}\n\n{string.Join("\n", lines.Skip(1))}"
            : string.Join("\n", lines);
    }

    /// <summary>
    /// The heading the post travels under while it is checked with the deck. Never shown: the post is
    /// taken back out before the deck is saved (<see cref="SplitPost"/>).
    /// </summary>
    public const string PostHeading = "The LinkedIn post";

    /// <summary>
    /// The deck with its post as one more section after the last slide, so one pass cleans and checks
    /// both: the em dash, a sentence that states a number the research does not hold, money that is
    /// not in US dollars.
    /// </summary>
    /// <param name="deck">The cover and every slide.</param>
    /// <param name="post">The post as the writer returned it: paragraphs with a blank line between.</param>
    public static ContentDocument WithPost(ContentDocument deck, string post)
    {
        var paragraphs = (post ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(paragraph => (Paragraph)new TextParagraph([new Run(paragraph)]))
            .ToList();
        return deck with
        {
            Sections =
            [
                .. deck.Sections,
                new Section(Tag: "h2", Heading: PostHeading, Paragraphs: paragraphs, Href: null, Children: []),
            ],
        };
    }

    /// <summary>
    /// The deck and its post, apart again: what <see cref="WithPost"/> put together, after the checks.
    /// </summary>
    /// <param name="checkedDocument">The document the checks read.</param>
    /// <param name="slides">How many slides the deck has, the cover included.</param>
    /// <returns>The deck as it is saved, and the post as plain text with a blank line between its
    /// paragraphs. A document that holds no post gives the document as it is and an empty post.</returns>
    public static (ContentDocument Deck, string Post) SplitPost(ContentDocument checkedDocument, int slides)
    {
        var afterTheCover = Math.Max(0, slides - 1);
        if (PostOf(checkedDocument, slides) is not { } post) return (checkedDocument, string.Empty);

        var text = string.Join(
            "\n\n",
            post.Paragraphs
                .OfType<TextParagraph>()
                .Select(paragraph => string.Concat(paragraph.Runs.Select(run => run.Text)).Trim())
                .Where(paragraph => paragraph.Length > 0));
        return (checkedDocument with { Sections = [.. checkedDocument.Sections.Take(afterTheCover)] }, text);
    }

    /// <summary>The section a deck's post travels in, or null when the document holds none.</summary>
    /// <param name="slides">How many slides the deck has, the cover included.</param>
    public static Section? PostOf(ContentDocument document, int slides)
    {
        var afterTheCover = Math.Max(0, slides - 1);
        return document.Sections.Count == afterTheCover + 1
            && string.Equals(document.Sections[afterTheCover].Heading, PostHeading, StringComparison.Ordinal)
                ? document.Sections[afterTheCover]
                : null;
    }

    /// <summary>A deck as it is saved, read back: what the PDF and the export are made from.</summary>
    /// <param name="Title">The deck's title, which is its cover's.</param>
    /// <param name="Deck">The cover as the opening, and each slide after it as a section.</param>
    /// <param name="Slides">What each slide is, in order, the cover first.</param>
    /// <param name="Post">The LinkedIn post the document is attached to.</param>
    /// <param name="FirstComment">The links, as text to paste as the first comment under the post.</param>
    public sealed record SavedDeck(
        string Title,
        ContentDocument Deck,
        IReadOnlyList<GccDeckSlidePlan> Slides,
        string Post,
        string FirstComment);

    /// <summary>
    /// A saved page's body read as a deck, or null when it is not one: another type's page, a body
    /// that is not JSON, or a deck whose slide list and document do not agree.
    /// </summary>
    /// <remarks>
    /// Nothing is inferred. What each slide is comes from the saved <c>slides</c> list, which the
    /// writer made beside the document, and a body without it is not read as a deck by the position
    /// of its sections.
    /// </remarks>
    public static SavedDeck? ReadSaved(string? bodyJson, JsonSerializerOptions options)
    {
        if (string.IsNullOrWhiteSpace(bodyJson)) return null;

        try
        {
            using var saved = JsonDocument.Parse(bodyJson);
            var root = saved.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("slides", out var listed) || listed.ValueKind != JsonValueKind.Array
                || !root.TryGetProperty("body", out var body) || body.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var slides = new List<GccDeckSlidePlan>();
            foreach (var slide in listed.EnumerateArray())
            {
                if (slide.ValueKind != JsonValueKind.Object
                    || !slide.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String
                    || !Enum.TryParse<GccDeckSlideKind>(kind.GetString(), ignoreCase: true, out var what)
                    || !Enum.IsDefined(what))
                {
                    return null;
                }

                slides.Add(new GccDeckSlidePlan(what, Text(slide, "partner")));
            }

            var deck = body.Deserialize<ContentDocument>(options);
            if (deck?.Lede is null || deck.Sections is null) return null;
            // One section for each slide after the cover, and the cover first.
            if (slides.Count != deck.Sections.Count + 1 || slides[0].Kind != GccDeckSlideKind.Cover) return null;

            return new SavedDeck(
                Text(root, "title") ?? deck.Lede.Heading,
                deck,
                slides,
                Text(root, "caption") ?? string.Empty,
                Text(root, "firstComment") ?? string.Empty);
        }
        catch (JsonException)
        {
            return null;
        }

        static string? Text(JsonElement of, string name) =>
            of.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    /// <summary>Each partner once, in the order given, blanks left out.</summary>
    private static IReadOnlyList<string> Named(IReadOnlyList<string>? partners) =>
        [.. (partners ?? [])
            .Select(partner => (partner ?? string.Empty).Trim())
            .Where(partner => partner.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>The reply to the request that writes the cover and the post.</summary>
    /// <param name="Cover">The deck's first slide.</param>
    /// <param name="Post">The LinkedIn post the document is attached to, as plain text.</param>
    public sealed record CoverAndPostReply(CoverReply Cover, string Post);

    /// <summary>The cover: a title and the one line under it.</summary>
    public sealed record CoverReply(string Title, string Line);

    /// <summary>The reply to the request that writes the slides after the cover.</summary>
    public sealed record SlidesReply(IReadOnlyList<SlideReply> Slides);

    /// <summary>One slide: its title and its lines.</summary>
    public sealed record SlideReply(string Title, IReadOnlyList<string> Lines);

    /// <summary>The name the cover-and-post reply format is sent under.</summary>
    public const string CoverAndPostSchemaName = "deck_cover_and_post";

    /// <summary>The name the slides reply format is sent under.</summary>
    public const string SlidesSchemaName = "deck_slides";

    /// <summary>
    /// The options both replies are read with and both reply formats are made from, so a format never
    /// describes a shape the reader does not read.
    /// </summary>
    /// <remarks>
    /// No field of either reply may be null or left out: the format says so (a field declared without
    /// a question mark is a plain string, object or array in it, never "or null"). A reply that leaves
    /// one blank anyway is refused where it is read (<see cref="ReadCoverAndPost"/>,
    /// <see cref="ReadSlides"/>), by the reply reader every other request's reply goes through.
    /// </remarks>
    internal static readonly JsonSerializerOptions ReplyJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    /// <summary>The cover-and-post reply format, strict: every field present, no other field, none null.</summary>
    public static string CoverAndPostSchema { get; } = WithoutNull(GccAdHocJsonSchema.For<CoverAndPostReply>(ReplyJson));

    /// <summary>The slides reply format, strict.</summary>
    public static string SlidesSchema { get; } = WithoutNull(GccAdHocJsonSchema.For<SlidesReply>(ReplyJson));

    /// <summary>
    /// A reply format with "or null" taken off every type that carries it.
    /// </summary>
    /// <remarks>
    /// The format is made from the reply's own type, and a field declared without a question mark comes
    /// out as a plain type. What is inside a list does not: a slide's lines are a list of strings, and
    /// the list's items came out as "string or null", which told the model a slide may hold a null line.
    /// </remarks>
    private static string WithoutNull(string schema)
    {
        var root = JsonNode.Parse(schema)!;
        StripNull(root);
        return root.ToJsonString();
    }

    private static void StripNull(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj["type"] is JsonArray types)
                {
                    var named = types
                        .Select(type => type?.GetValue<string>())
                        .Where(type => type is not null && type != "null")
                        .ToList();
                    if (named.Count == 1) obj["type"] = named[0];
                }

                foreach (var property in obj.ToList()) StripNull(property.Value);
                break;

            case JsonArray array:
                foreach (var item in array) StripNull(item);
                break;
        }
    }
}
