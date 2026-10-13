using System.Text.Json;
using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.Workflow.Providers;
using GeekApplication.Interfaces.ContentWriterV3;
using GeekApplication.Models.ContentCreator;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// What is stored for an email, a social post and an image prompt is what this code wrote from the model's fields, never
/// the model's text. The email and social writers returned the reply trimmed and
/// unparsed (a fence was removed only when the reply began with one), so a malformed reply was saved as the page's body;
/// <c>GenerateImagePromptJsonAsync</c> wrapped any reply that was not JSON as the <c>prompt</c> with a canned style and
/// called it a success. A reply that cannot be read is refused instead.
/// </summary>
public sealed class GccShortFormReplyTests
{
    private static GccCreateDto Create() => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "pillar", "Automated approval workflows", "A note.",
        null, null, null, null, "draft", DateTime.UtcNow, DateTime.UtcNow);

    private static GccGenerateService Service(string reply, out GccGenerateServiceImagePromptTests.ScriptedProvider provider)
    {
        provider = new GccGenerateServiceImagePromptTests.ScriptedProvider(reply);
        return GccGenerateServiceImagePromptTests.Build(provider);
    }

    /// <summary>The approved page a short piece is written from: a pillar that states one figure.</summary>
    private static readonly GccRepurposeSource Page = new(
        Guid.NewGuid(), "pillar", "Automated approval workflows", "Automated Approval Workflows: A Guide",
        "Why approvals stall\n\nAn invoice waits four days for a signature. Teams that route by rule cut approval time by 60%.");

    private static Task<string> Email(string reply) =>
        Service(reply, out _).RepurposeEmailAsync(Create(), Page, null, ContentGeneratorProvider.OpenAi, CancellationToken.None);

    private static Task<string> Social(string reply, string platform = "linkedin") =>
        Service(reply, out _).RepurposeSocialPostAsync(
            Create(), platform, Page, null, ContentGeneratorProvider.OpenAi, CancellationToken.None);

    private static Task<string> ImagePrompt(string reply) =>
        Service(reply, out _).GenerateImagePromptJsonAsync(
            "Automated approval workflows", null, null, ContentGeneratorProvider.OpenAi, CancellationToken.None);

    // ---- email -----------------------------------------------------------------------------------

    [Fact]
    public async Task An_email_is_stored_as_its_three_fields_written_by_this_code()
    {
        var stored = await Email("""{"subject":"Hello","body":"The body.","ctaLabel":"Read more"}""");

        Assert.Equal("""{"subject":"Hello","body":"The body.","ctaLabel":"Read more"}""", stored);
    }

    [Fact]
    public async Task A_fenced_email_reply_is_stored_without_the_fence()
    {
        var stored = await Email("```json\n{\"subject\":\"Hello\",\"body\":\"The body.\",\"ctaLabel\":\"Read more\"}\n```");

        using var doc = JsonDocument.Parse(stored);
        Assert.Equal("Hello", doc.RootElement.GetProperty("subject").GetString());
        Assert.DoesNotContain("```", stored, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_email_reply_with_words_around_the_object_is_stored_as_the_object_alone()
    {
        var stored = await Email("""Here you go: {"subject":"Hello","body":"The body.","ctaLabel":"Read more"} Enjoy!""");

        using var doc = JsonDocument.Parse(stored);
        Assert.Equal("The body.", doc.RootElement.GetProperty("body").GetString());
        Assert.DoesNotContain("Enjoy", stored, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fields_the_email_did_not_ask_for_are_not_stored()
    {
        var stored = await Email("""{"subject":"Hello","body":"The body.","ctaLabel":"Read more","aside":"not asked for"}""");

        using var doc = JsonDocument.Parse(stored);
        Assert.False(doc.RootElement.TryGetProperty("aside", out _));
    }

    [Theory]
    [InlineData("""{"subject":"Hello","body":"The body.","ctaLabel":""}""", "the reply carried no ctaLabel")]
    [InlineData("""{"subject":"Hello","ctaLabel":"Read more"}""", "the reply carried no body")]
    [InlineData("""{"body":"The body.","ctaLabel":"Read more"}""", "the reply carried no subject")]
    public async Task An_email_missing_a_field_is_refused_and_says_which(string reply, string reason)
    {
        var ex = await Assert.ThrowsAsync<ContentGenerationException>(() => Email(reply));

        Assert.Equal(ContentGenerationFailureKind.UnusableReply, ex.Kind);
        Assert.Contains($"Reason: {reason}.", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("I could not write that email.")]
    [InlineData("")]
    [InlineData("""{"subject":"Hello","body":"cut off""")]
    public async Task An_email_reply_that_cannot_be_read_is_refused_not_stored(string reply)
    {
        var ex = await Assert.ThrowsAsync<ContentGenerationException>(() => Email(reply));

        Assert.Equal(ContentGenerationFailureKind.UnusableReply, ex.Kind);
        Assert.StartsWith("Model did not return a valid cold-outreach email for the email page. ", ex.Message, StringComparison.Ordinal);
    }

    // ---- social ----------------------------------------------------------------------------------

    [Fact]
    public async Task A_social_post_is_stored_as_its_text_field_written_by_this_code()
    {
        var stored = await Social("```json\n{\"text\":\"A post.\\nSecond line.\"}\n```");

        using var doc = JsonDocument.Parse(stored);
        Assert.Equal("A post.\nSecond line.", doc.RootElement.GetProperty("text").GetString());
        Assert.Equal(1, doc.RootElement.EnumerateObject().Count());
    }

    [Theory]
    [InlineData("""{"text":""}""", "the reply carried no text")]
    [InlineData("""{"post":"wrong key"}""", "the reply carried no text")]
    public async Task A_social_post_with_no_text_is_refused_and_says_so(string reply, string reason)
    {
        var ex = await Assert.ThrowsAsync<ContentGenerationException>(() => Social(reply, "facebook"));

        Assert.Contains($"Reason: {reason}.", ex.Message, StringComparison.Ordinal);
        Assert.Contains("a valid social post for the facebook page. ", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_social_reply_that_is_not_json_is_refused_not_stored()
    {
        var ex = await Assert.ThrowsAsync<ContentGenerationException>(() => Social("Great post idea! Here it is: ..."));

        Assert.Equal(ContentGenerationFailureKind.UnusableReply, ex.Kind);
    }

    // ---- standalone image prompt ------------------------------------------------------------------

    [Fact]
    public async Task An_image_prompt_is_stored_with_every_field_the_call_asked_for()
    {
        var stored = await ImagePrompt(
            """{"prompt":"A wide hero.","style":"Illustration","negativePrompt":"text","aspectRatio":"16:9","imageModel":"m","stylePreset":"Illustration","notes":""}""");

        using var doc = JsonDocument.Parse(stored);
        Assert.Equal("A wide hero.", doc.RootElement.GetProperty("prompt").GetString());
        Assert.Equal("16:9", doc.RootElement.GetProperty("aspectRatio").GetString());
        Assert.Equal("m", doc.RootElement.GetProperty("imageModel").GetString());
    }

    [Fact]
    public async Task A_reply_that_is_not_json_is_no_longer_wrapped_as_the_prompt_with_a_canned_style()
    {
        const string apology = "I'm sorry, I can't create an image prompt for that topic.";

        var ex = await Assert.ThrowsAsync<ContentGenerationException>(() => ImagePrompt(apology));

        Assert.Equal(ContentGenerationFailureKind.UnusableReply, ex.Kind);
        Assert.Contains(apology, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_image_prompt_object_with_no_prompt_is_refused()
    {
        var ex = await Assert.ThrowsAsync<ContentGenerationException>(() =>
            ImagePrompt("""{"prompt":"  ","style":"Illustration"}"""));

        Assert.Contains("Reason: the reply carried no prompt.", ex.Message, StringComparison.Ordinal);
    }

    // ---- section image prompts --------------------------------------------------------------------

    [Fact]
    public async Task Section_image_prompts_in_a_fence_are_read()
    {
        var service = Service(
            "```json\n{\"prompts\":[{\"section\":\"Hero\",\"prompt\":\"h\"},{\"section\":\"S1\",\"prompt\":\"a\"},{\"section\":\"S2\",\"prompt\":\"b\"}]}\n```",
            out _);

        var updated = await service.GenerateSectionImagePromptsAsync(
            "pillar", "Title", TwoSectionDocument(), null, ContentGeneratorProvider.OpenAi, CancellationToken.None);

        Assert.Contains("\"imagePrompt\":\"a\"", updated, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Section_image_prompts_that_are_not_json_are_refused_as_an_unusable_reply_not_a_parse_exception()
    {
        var service = Service("Here are your prompts: one, two, three.", out _);

        var ex = await Assert.ThrowsAsync<ContentGenerationException>(() => service.GenerateSectionImagePromptsAsync(
            "pillar", "Title", TwoSectionDocument(), null, ContentGeneratorProvider.OpenAi, CancellationToken.None));

        Assert.Equal(ContentGenerationFailureKind.UnusableReply, ex.Kind);
        Assert.StartsWith("Model did not return a valid set of section image prompts for the pillar page. ", ex.Message, StringComparison.Ordinal);
    }

    private static string TwoSectionDocument() =>
        JsonSerializer.Serialize(
            new GeekAPI.Services.Workflow.Domain.Entities.ContentDocument(
                Lede: new GeekAPI.Services.Workflow.Domain.Entities.Section("h2", "Intro", [], null, []),
                Sections:
                [
                    new GeekAPI.Services.Workflow.Domain.Entities.Section("h2", "Overview", [], null, []),
                    new GeekAPI.Services.Workflow.Domain.Entities.Section("h2", "Details", [], null, []),
                ]),
            GccDocumentJson.Options);

    // ---- written from a page ---------------------------------------------------------------------

    /// <summary>
    /// A short piece is written from an approved page (Jeff, 2026-10-12: short content types "ARE DERIVED
    /// OR REPURPOSED FROM EXISTING CONTENT"). The writer is handed the page whole, and what the Brief
    /// already gave a short piece; until then it was handed the Brief alone.
    /// </summary>
    [Theory]
    [InlineData("email")]
    [InlineData("linkedin")]
    public async Task The_writer_of_a_short_piece_is_handed_the_page_it_is_written_from(string piece)
    {
        var service = Service(
            piece == "email" ? """{"subject":"Hello","body":"The body.","ctaLabel":"Read more"}""" : """{"text":"A post."}""",
            out var provider);

        if (piece == "email")
            await service.RepurposeEmailAsync(Create(), Page, null, ContentGeneratorProvider.OpenAi, CancellationToken.None);
        else
            await service.RepurposeSocialPostAsync(Create(), piece, Page, null, ContentGeneratorProvider.OpenAi, CancellationToken.None);

        var sent = Assert.Single(provider.Requests).Messages[1].Content;
        Assert.Contains("IS WRITTEN FROM ===", sent, StringComparison.Ordinal);
        Assert.Contains("An approved pillar page of this publisher's: \"Automated Approval Workflows: A Guide\".", sent, StringComparison.Ordinal);
        Assert.Contains(Page.Text, sent, StringComparison.Ordinal);
        Assert.Contains("a figure is written exactly as the page", sent, StringComparison.Ordinal);
        // And what a short piece was already handed from the Brief's side of the request.
        Assert.Contains("Topic: Automated approval workflows", sent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_figure_the_page_states_may_be_stated_and_one_it_does_not_refuses_the_piece()
    {
        // The page's own figure, in the email and in the post.
        await Email("""{"subject":"Cut approval time by 60%","body":"Rule-based routing cut approval time by 60%.","ctaLabel":"Read more"}""");
        await Social("""{"text":"Teams that route by rule cut approval time by 60%."}""");

        var email = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Email("""{"subject":"Hello","body":"Customers save $12,000 a year and cut approval time by 60%.","ctaLabel":"Read more"}"""));
        Assert.Equal(
            "Refused: the email states a figure the page it was written from does not: $12,000. Nothing was saved for it.",
            email.Message);

        var post = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Social("""{"text":"Approvals run 75% faster and cost 40% less."}"""));
        Assert.Equal(
            "Refused: the LinkedIn post states figures the page it was written from does not: 75%, 40%. Nothing was saved for it.",
            post.Message);
    }

    [Fact]
    public async Task A_piece_is_asked_for_once_and_a_refused_one_is_not_written_again()
    {
        var service = Service("""{"text":"Approvals run 75% faster."}""", out var provider);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RepurposeSocialPostAsync(
            Create(), "linkedin", Page, null, ContentGeneratorProvider.OpenAi, CancellationToken.None));

        Assert.Single(provider.Requests);
    }
}
