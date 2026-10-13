using System.Text;
using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Providers;

// Filed under GeekAPI/Services/ContentCreator/Writers beside the Pillar's part, and for the same
// reason in ContentPromptBuilder's own namespace: every part of one partial class shares it.
namespace GeekAPI.Services.Workflow.Services.PromptBuilders;

// "PDF / LinkedIn document" (plans/enable-pdf.md): the two requests that write a deck. The cover is
// written with the LinkedIn post that carries the document; every slide after it in one request. The
// last slide is built by code (GccClosing) and no request is told to write it.
//
// Both requests send the one system message every long-form request sends (SystemPrompt), so a deck is
// written in the voice, under the grounding rule and under the dollar rule a pillar is, and is told
// not to use the em dash by the same line. What differs is the reply: a slide is a title and its
// lines, as strings. There is no section, no paragraph and no run for the writer to shape, and so no
// nesting, no quotation and no link it could return.
//
// What a slide and the post are asked to be is taken from Jeff's own skills, not written here from
// scratch: the "social" skill's carousel reference, which covers LinkedIn document posts (slide 1 is
// the thumbnail and stands alone; one idea a slide; a count promised on the cover is exactly
// delivered; claims and numbers are real; the post above the document is a second hook, written as
// its own post and never "see attached"), and "post-writer" (a post is 150 to 300 words). The deck's
// own order, a cover, the problem, one slide a partner, the approach and one call to action, is the
// plan's, and is that reference's numbered-list shape with its last slide's two jobs on two slides.
public partial class ContentPromptBuilder
{
    /// <summary>The cover-and-post reply, described to the writer. A property for the reason
    /// <see cref="IntroductionJsonContract"/> is one: it reads the deck's numbers, and a field's
    /// initialisation order across the parts of a partial class is not specified.</summary>
    private static string DeckCoverAndPostJsonContract =>
        "{\"cover\": {\"title\": string (the deck's title: at most "
        + $"{ContentLengthTargets.LinkedInDocumentTitleMaxWords} words), "
        + "\"line\": string (the one sentence under the title: at most "
        + $"{ContentLengthTargets.LinkedInDocumentCoverLineMaxWords} words)}}, "
        + "\"post\": string (the LinkedIn post, plain text; \\n\\n between its paragraphs)}";

    /// <summary>The slides reply, described to the writer.</summary>
    private static string DeckSlidesJsonContract =>
        "{\"slides\": [{\"title\": string (at most "
        + $"{ContentLengthTargets.LinkedInDocumentTitleMaxWords} words), "
        + "\"lines\": [string (one line: at most "
        + $"{ContentLengthTargets.LinkedInDocumentLineMaxWords} words), ...] "
        + $"({ContentLengthTargets.LinkedInDocumentMinLines} to {ContentLengthTargets.LinkedInDocumentMaxLines} lines)}}, ...] "
        + "(one entry for each slide assigned, in the order given)}";

    /// <summary>
    /// What the cover-and-post request reserves for its reply: a post of 300 words and a cover of 30
    /// are about 450 tokens, and this is a little over twice that.
    /// </summary>
    private const int DeckCoverAndPostMaxOutputTokens = 1024;

    /// <summary>
    /// What the slides request reserves for its reply. The longest deck has ten written slides, each at
    /// most a ten-word title and four fourteen-word lines: 660 words, about 1,200 tokens with the
    /// reply's own punctuation. This is that and two thirds again.
    /// </summary>
    /// <remarks>
    /// Sized to what a reply can hold and not above it on purpose. On 2026-10-13 two replies of another
    /// request filled the rest of their 3,072 tokens with blanks; a reservation is what such a reply
    /// can cost, and a deck's slides need a third of that.
    /// </remarks>
    private const int DeckSlidesMaxOutputTokens = 2048;

    /// <summary>What a deck is, said the same way at the top of both of its requests.</summary>
    private static string DeckDefinition(int slideCount) =>
        $"A LinkedIn document: a deck of {slideCount} slides attached to a LinkedIn post. A reader meets the "
        + "post and the first slide in the feed, then swipes the slides one at a time, most of them on a "
        + "phone. A deck is not an article cut into squares: each slide is a title and a few short lines, "
        + "carries one idea, makes sense alone, and gives the reader a reason to swipe to the next.";

    /// <summary>
    /// The rules every line of a deck is held to, whichever request writes it. The page's checks
    /// refuse a link and a quotation in a deck, so the writer is told of both here, before the reply.
    /// </summary>
    private const string DeckLineRules =
        "A title and a line are plain words: no address, no link, no hashtag, no emoji, no quotation "
        + "marks around a source's words, and no label in front such as \"Slide 3:\" or \"Tip:\". "
        + "A figure, a price, a percentage or a timeframe appears only where the evidence in this message gives it; "
        + "where the evidence gives none, the line is written without one. No customer and no case study "
        + "is reported, named or unnamed. Do not ask the reader questions and do not tell them to ask "
        + "themselves anything: the deck's last slide carries the publisher's own questions, and it is "
        + "not written here.";

    /// <summary>How the brief's choices are headed in the request that writes the cover and the post.</summary>
    private const string DeckBriefForCoverAndPost = "=== BRIEF CONTROLS (honor in the cover and in the post) ===";

    /// <summary>How the brief's choices are headed in the request that writes the slides.</summary>
    private const string DeckBriefForSlides = "=== BRIEF CONTROLS (honor in every slide) ===";

    /// <summary>
    /// What both requests are told of the reader and of the operator's position, before any evidence:
    /// the brief's choices, once; what the publisher's own site says of itself; the operator's framing
    /// of the problem. A part with nothing to print leaves no gap.
    /// </summary>
    private static void AppendDeckSources(
        StringBuilder user, ProjectGenerationContext context, string briefHeading, string? operatorsFraming)
    {
        foreach (var block in new[]
                 {
                     BuildBriefBodyGuidance(context, withAngle: true, heading: briefHeading),
                     BuildPublisherSiteBlock(context),
                     operatorsFraming,
                 })
        {
            if (!string.IsNullOrWhiteSpace(block)) user.AppendLine(block.TrimEnd()).AppendLine();
        }
    }

    /// <summary>
    /// The search results and sources a page's opening is shown, where the context holds any. The
    /// pages' block of tools to name and link is left out: a deck carries no link, and its partners
    /// are named by its own slide list.
    /// </summary>
    private static void AppendDeckSiteResearch(StringBuilder user, ProjectGenerationContext context)
    {
        var research = ResearchBriefBuilder.Build(context, ResearchBriefPhase.Opening);
        if (!string.IsNullOrWhiteSpace(research)) user.AppendLine(research.TrimEnd()).AppendLine();
    }

    /// <summary>
    /// The subject as it is searched for. A deck is not scored for search, so it is told the phrase and
    /// told not to write to it.
    /// </summary>
    private static string DeckKeywordLine(ProjectGenerationContext context) =>
        $"The subject, as it is searched for: {context.TargetKeyword}. A deck is not written to a keyword: "
        + "use the phrase where it is the natural wording and nowhere else.";

    public ChatCompletionRequest BuildLinkedInDocumentCoverPrompt(
        ProjectGenerationContext context,
        IReadOnlyList<string> slides,
        int partnerSlides,
        string? operatorsFraming = null,
        string? evidenceBlock = null)
    {
        var system = SystemPrompt(DeckCoverAndPostJsonContract);

        var user = new StringBuilder()
            .AppendLine("=== THIS PIECE ===")
            .AppendLine(DeckDefinition(slides.Count))
            .AppendLine("This request writes the deck's cover and the post. The other slides are written separately.")
            .AppendLine($"Publisher positioning: {context.ImplementerPositioning}")
            .AppendLine();

        // The brief, once, as every other request of a page prints it; then the publisher and the operator.
        AppendDeckSources(user, context, DeckBriefForCoverAndPost, operatorsFraming);

        if (!string.IsNullOrWhiteSpace(evidenceBlock))
        {
            user.AppendLine(
                    "HOW TO USE THE EVIDENCE BELOW: it is here so the cover and the post are true, not so it "
                    + "gets covered. The problem they open on must be one the passages describe, and any figure "
                    + "in them must appear in a passage.")
                .AppendLine(evidenceBlock.TrimEnd())
                .AppendLine();
        }

        AppendDeckSiteResearch(user, context);

        user.AppendLine("=== ASSIGNMENT ===")
            .AppendLine(DeckKeywordLine(context))
            .AppendLine("THE COVER:")
            .AppendLine(
                "The cover is the one slide a reader sees in the feed, before they know a deck follows. It has "
                + "to stop the scroll with nothing behind it.")
            .AppendLine(
                "\"title\": the failure this deck is about, as this reader lives it. A statement, never a "
                + "question. It names no product and no company, the publisher's included. At most "
                + $"{ContentLengthTargets.LinkedInDocumentTitleMaxWords} words.")
            .AppendLine(
                "\"line\": one sentence that says what the deck settles for the reader. At most "
                + $"{ContentLengthTargets.LinkedInDocumentCoverLineMaxWords} words.")
            .AppendLine(
                $"A count on the cover is exactly what the deck delivers: it has {partnerSlides} slides on "
                + $"partner tools, one tool a slide. Say {partnerSlides} or say no count; never round and never "
                + "promise more.")
            .AppendLine(DeckLineRules)
            .AppendLine("THE POST:")
            .AppendLine(
                "The LinkedIn post the document is attached to, "
                + $"{ContentLengthTargets.LinkedInDocumentPostMinWords} to {ContentLengthTargets.LinkedInDocumentPostMaxWords} "
                + "words. It is a second hook, written as a post in its own right: it does not repeat the "
                + "cover's words, and it is never a note that a document is attached. Its first two lines are "
                + "what a reader sees before \"see more\", so the hook is there. Then what the failure costs "
                + "this reader, then what the slides walk them through. It ends on its own material, with no "
                + "sign-off.")
            .AppendLine($"{BrandTones.LinkedInChannelLine}")
            .AppendLine(
                "Short paragraphs, a blank line between them. It may name a partner tool where it says what "
                + "that tool answers; it need not name them all, the slides do. It writes no address, no link "
                + "and no hashtag, and at most one emoji: the booking link and each partner's page are put "
                + "under the post by code. The grounding rule holds for the post as for the cover: no figure "
                + "that is not in the evidence, no customer, no case study.")
            .AppendLine()
            .AppendLine("The deck's slides, for context only (write the cover and none of the others):")
            .AppendLine(string.Join(Environment.NewLine, slides.Select((slide, i) => $"{i + 1}. {slide}")))
            .AppendLine()
            .AppendLine("Answer in the JSON the output contract in the system message describes.");

        return new ChatCompletionRequest(
            Messages: [new(ChatRole.System, system), new(ChatRole.User, user.ToString())],
            Temperature: 0.65,
            MaxOutputTokens: DeckCoverAndPostMaxOutputTokens,
            JsonSchemaName: GccLinkedInDocument.CoverAndPostSchemaName,
            JsonSchema: GccLinkedInDocument.CoverAndPostSchema);
    }

    public ChatCompletionRequest BuildLinkedInDocumentSlidesPrompt(
        ProjectGenerationContext context,
        IReadOnlyList<SectionSlot> slides,
        int slideCount,
        string? operatorsFraming = null,
        string? evidenceBlock = null,
        GccLinkedInDocument.CoverReply? cover = null)
    {
        var system = SystemPrompt(DeckSlidesJsonContract);

        var user = new StringBuilder()
            .AppendLine("=== THIS PIECE ===")
            .AppendLine(DeckDefinition(slideCount))
            .AppendLine(
                "This request writes every slide between the cover and the last. The cover is already "
                + "written, and the last slide, what to do next, is built from the publisher's own booking "
                + "words and is not written here.")
            .AppendLine($"Publisher positioning: {context.ImplementerPositioning}")
            .AppendLine();

        AppendDeckSources(user, context, DeckBriefForSlides, operatorsFraming);

        if (!string.IsNullOrWhiteSpace(evidenceBlock))
        {
            user.AppendLine(evidenceBlock.TrimEnd()).AppendLine();
        }

        AppendDeckSiteResearch(user, context);

        if (cover is not null)
        {
            user.AppendLine("THE COVER, ALREADY WRITTEN (the slides continue it; none repeats it):")
                .AppendLine($"Title: {cover.Title}")
                .AppendLine($"Line: {cover.Line}")
                .AppendLine();
        }

        user.AppendLine("=== ASSIGNMENT ===")
            .AppendLine(DeckKeywordLine(context))
            .AppendLine($"Write these {slides.Count} slides, in this order. Each numbered entry says what the slide must cover; you write its title:")
            .AppendLine(string.Join(Environment.NewLine, slides.Select((slide, i) =>
                $"{i + 1}. Cover: {slide.Label}"
                + (slide.Guidance is { Length: > 0 } guidance ? Environment.NewLine + $"   {guidance}" : string.Empty))))
            .AppendLine()
            .AppendLine(
                $"Each slide: a title of at most {ContentLengthTargets.LinkedInDocumentTitleMaxWords} words and "
                + $"{ContentLengthTargets.LinkedInDocumentMinLines} to {ContentLengthTargets.LinkedInDocumentMaxLines} lines, "
                + $"each line at most {ContentLengthTargets.LinkedInDocumentLineMaxWords} words. A line is one "
                + "complete thought a reader takes in at a glance: what changes, for whom, and what it saves "
                + "or stops. Not a fragment, not a slogan, and not a sentence cut in two across lines. One idea "
                + "a slide: where a slide needs a second idea to make its point, it is making two points.")
            .AppendLine(
                "A title is a statement, never a question. It names no product and no company: a partner "
                + "tool is named in its slide's lines, spelled exactly as the slide's entry above spells it, "
                + "and never in a title.")
            .AppendLine(
                "No two slides make the same point. A partner's slide is about that partner alone and names "
                + "no other partner.")
            .AppendLine(DeckLineRules)
            .AppendLine()
            .AppendLine("Answer in the JSON the output contract in the system message describes.");

        return new ChatCompletionRequest(
            Messages: [new(ChatRole.System, system), new(ChatRole.User, user.ToString())],
            Temperature: 0.65,
            MaxOutputTokens: DeckSlidesMaxOutputTokens,
            JsonSchemaName: GccLinkedInDocument.SlidesSchemaName,
            JsonSchema: GccLinkedInDocument.SlidesSchema);
    }
}
