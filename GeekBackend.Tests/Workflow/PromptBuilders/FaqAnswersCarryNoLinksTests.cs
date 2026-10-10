using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services.PromptBuilders;
using GeekAPI.Services.Workflow.Services.SchemaBuilders;
using GeekApplication.Models.ContentCreator;
using GeekBackend.Tests.ContentCreator;
using Xunit;

namespace GeekBackend.Tests.Workflow.PromptBuilders;

/// <summary>
/// An FAQ answer carries no link, and no FAQ prompt has to say so: the reply has no field to put one in.
/// </summary>
/// <remarks>
/// The first run on the "links" contract (2026-10-09): every FAQ answer on five tool pages named the
/// target "S#" -- the placeholder from the section contract, copied because the FAQ-bank prompt
/// printed no ids and the body's link instruction told the writer a link needs one. An instruction
/// was then added telling the FAQ writer every run's "link" is null. The writer links nothing now
/// (Jeff, 2026-10-10): the field, the placeholder and the instruction are all gone, and the FAQ is
/// added after <c>GccToolLinker</c> has run, so code cannot link it either.
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
                "Partner Widget (https://partner.test/widget)\n  - Partner Widget syncs every payment to QuickBooks Online."),
            // The prompt reads a pair's question, answer and source; provenance is carried, not read.
            _ => Builder.BuildToolFaqSectionPrompt(
                context, Article, App,
                [new GccPartnerFaqAsset("Does it sync?", "It syncs every payment.", "https://partner.test/faq", null!)]),
        };
    }

    [Theory]
    [MemberData(nameof(EveryFaqPrompt))]
    public void No_faq_prompt_offers_a_run_a_link_or_names_a_target_id(string which)
    {
        // "S#" in the contract is what the writer copied into every answer on 2026-10-09.
        var prompt = string.Join("\n", Build(which).Messages.Select(m => m.Content));

        Assert.DoesNotContain("\"link\"", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("S#", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("T#", prompt, StringComparison.Ordinal);
        Assert.Contains("{\"text\": string (plain text only", SystemOf(Build(which)), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(EveryFaqPrompt))]
    public void No_faq_prompt_carries_a_rule_about_links(string which)
    {
        // Neither the body's rule nor the "carries no link" rule that replaced it for a day. A rule about
        // something the reply cannot contain is one more thing to misread.
        var system = SystemOf(Build(which));

        Assert.DoesNotContain("A LINK IS A RUN", system, StringComparison.Ordinal);
        Assert.DoesNotContain("LINKS:", system, StringComparison.Ordinal);
        Assert.DoesNotContain("carries no link", system, StringComparison.Ordinal);
    }
}
