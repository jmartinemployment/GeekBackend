using System.Text.Json;
using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.Workflow.Providers;
using GeekApplication.Interfaces.ContentWriterV3;
using GeekApplication.Models.ContentCreator;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// What is stored for an email, a social post and an image prompt is what this code wrote from the model's fields, never
/// the model's text. <c>GenerateEmailAsync</c> and <c>GenerateSocialPostAsync</c> returned the reply trimmed and
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

    private static Task<string> Email(string reply) =>
        Service(reply, out _).GenerateEmailAsync(Create(), null, ContentGeneratorProvider.OpenAi, null, CancellationToken.None);

    private static Task<string> Social(string reply, string platform = "linkedin") =>
        Service(reply, out _).GenerateSocialPostAsync(
            Create(), platform, null, ContentGeneratorProvider.OpenAi, null, CancellationToken.None);

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
        GccGenerateService.SerializeDocument(new GeekAPI.Services.Workflow.Domain.Entities.ContentDocument(
            Lede: new GeekAPI.Services.Workflow.Domain.Entities.Section("h2", "Intro", [], null, []),
            Sections:
            [
                new GeekAPI.Services.Workflow.Domain.Entities.Section("h2", "Overview", [], null, []),
                new GeekAPI.Services.Workflow.Domain.Entities.Section("h2", "Details", [], null, []),
            ]));

    // ---- the repurpose pack ----------------------------------------------------------------------

    [Fact]
    public async Task A_repurpose_pack_is_read_through_the_same_parser_and_needs_variants()
    {
        var packed = await Service("```json\n{\"variants\":[{\"channel\":\"LinkedIn\",\"body\":\"b\"}]}\n```", out _)
            .GenerateRepurposePackAsync("{}", ["LinkedIn"], ContentGeneratorProvider.OpenAi, CancellationToken.None);

        using var doc = JsonDocument.Parse(packed);
        Assert.Equal(1, doc.RootElement.GetProperty("variants").GetArrayLength());

        var ex = await Assert.ThrowsAsync<ContentGenerationException>(() =>
            Service("""{"variants":[]}""", out _)
                .GenerateRepurposePackAsync("{}", ["LinkedIn"], ContentGeneratorProvider.OpenAi, CancellationToken.None));
        Assert.Contains("the reply carried no non-empty variants array", ex.Message, StringComparison.Ordinal);
    }
}
