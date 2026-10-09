using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services.PromptBuilders;
using GeekAPI.Services.Workflow.Services.SchemaBuilders;
using GeekApplication.Models.ContentCreator;
using GeekBackend.Tests.ContentCreator;
using Xunit;

namespace GeekBackend.Tests.Workflow.PromptBuilders;

/// <summary>
/// An FAQ answer carries no link, and every FAQ prompt says so in place of the body's link rule.
/// </summary>
/// <remarks>
/// The first run on the "links" contract (2026-10-09): every FAQ answer on five tool pages named the
/// target "S#" -- the placeholder from the section contract, copied because the FAQ-bank prompt
/// prints no ids and the body's link instruction told the writer a link needs one. The FAQ is the
/// body's appendix, written from evidence the body already attributes; it links nothing.
/// </remarks>
public sealed class FaqAnswersCarryNoLinksTests
{
    private static readonly ContentPromptBuilder Builder = new();
    private static readonly ArticleMetadataDraft Article = new("Partner Widget", "Meta", ["ai"], []);
    private static readonly SoftwareApplicationDescriptor App = new("Partner Widget", null, "https://partner.test/widget");

    private static string SystemOf(ChatCompletionRequest request) =>
        request.Messages.First(m => m.Role == ChatRole.System).Content;

    public static TheoryData<string> EveryFaqPrompt => new() { "pillar", "blog", "toolQuestions", "toolBank" };

    private static ChatCompletionRequest Build(string which)
    {
        var context = GccOpeningAsksNothingTests.Context();
        return which switch
        {
            "pillar" => Builder.BuildArticleFaqSectionPrompt(context, Article, ["How long does it take?"], isRegeneration: false),
            "blog" => Builder.BuildBlogFaqSectionPrompt(
                context, new BlogMetadataDraft("Title", "Meta", ["ai"], ["Overview"]), ["How long does it take?"]),
            "toolQuestions" => Builder.BuildToolFaqFromQuestionsPrompt(
                context, Article, App, ["Does it sync with QuickBooks Online?"],
                "[S1] Partner Widget (https://partner.test/widget)\n  - Partner Widget syncs every payment to QuickBooks Online."),
            // The prompt reads a pair's question, answer and source; provenance is carried, not read.
            _ => Builder.BuildToolFaqSectionPrompt(
                context, Article, App,
                [new GccPartnerFaqAsset("Does it sync?", "It syncs every payment.", "https://partner.test/faq", null!)]),
        };
    }

    [Theory]
    [MemberData(nameof(EveryFaqPrompt))]
    public void Every_faq_prompt_says_an_answer_carries_no_link(string which)
    {
        var system = SystemOf(Build(which));

        Assert.Contains(ContentPromptBuilder.FaqNoLinksInstruction, system, StringComparison.Ordinal);
        Assert.Contains("Every run's \"link\" is null", system, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(EveryFaqPrompt))]
    public void No_faq_prompt_carries_the_bodys_link_rule(string which)
    {
        // The body's rule tells the writer how to link. Said to an FAQ writer with no ids in front of
        // it, it produced "S#".
        var system = SystemOf(Build(which));

        Assert.DoesNotContain(ContentPromptBuilder.LinkTextInstruction, system, StringComparison.Ordinal);
    }
}
