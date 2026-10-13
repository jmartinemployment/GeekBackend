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
    /// The slides of a deck for these partners, in order: the cover, the problem, one for each partner
    /// in the project's order, the approach, what to do next.
    /// </summary>
    /// <param name="partners">The project's partner tools, as the writer is told to spell them
    /// (<see cref="GccRequiredToolMentions.For"/>).</param>
    public static IReadOnlyList<GccDeckSlidePlan> Slides(IReadOnlyList<string> partners) =>
    [
        new(GccDeckSlideKind.Cover),
        new(GccDeckSlideKind.Problem),
        .. Named(partners).Select(partner => new GccDeckSlidePlan(GccDeckSlideKind.Partner, partner)),
        new(GccDeckSlideKind.Approach),
        new(GccDeckSlideKind.Next),
    ];

    /// <summary>
    /// The slides a request writes after the cover: the problem, each partner's, the approach. The
    /// cover is written with the post, and the last slide is built by code.
    /// </summary>
    public static IReadOnlyList<GccDeckSlidePlan> WrittenAfterTheCover(IReadOnlyList<string> partners) =>
        [.. Slides(partners).Where(slide => slide.Kind != GccDeckSlideKind.Cover && !slide.BuiltByCode)];

    /// <summary>
    /// Why a deck cannot be written for this many partners, or null when it can. Asked before a
    /// request is paid for.
    /// </summary>
    /// <remarks>
    /// A deck has a slide for each partner and four besides, and LinkedIn readers leave a document
    /// that runs long: six to twelve slides is two to eight partners. One partner is a tool page, not
    /// a deck about the partners; nine would be thirteen slides. The count is said back with the
    /// names, so the operator sees which partners the project holds.
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
