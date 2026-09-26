using GeekAPI.Services.ContentCreator.ContentTypes;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services.PromptBuilders;
using GeekAPI.Services.Workflow.Services.SchemaBuilders;

namespace GeekBackend.Tests.Workflow.PromptBuilders;

/// <summary>
/// A tool page may block-quote its partner only when the partner's own wording was supplied.
///
/// <para>
/// BuildPublisherSiteBlock names "a partner's claim from the partner's own page" as the case a
/// blockquote exists for, and the section contract offers the type and the cite field to hold one.
/// Neither checks that any partner wording arrived. Where none did, a quote box would assert
/// someone's exact published words and a cite would say where to verify them, both invented from a
/// product name and a link -- and nothing downstream looks: the guardrail passes quotes through
/// deliberately and the renderer writes the cite straight onto the tag.
/// </para>
/// </summary>
public class ToolPageQuotationTests
{
    private static ProjectGenerationContext Context() => new(
        ProjectName: "Acme",
        ProjectUrl: "https://acme.test",
        TargetKeyword: "accounts payable automation",
        Department: "marketing",
        SiteName: "Acme",
        DetectedTone: string.Empty,
        DetectedFocus: string.Empty,
        // The publisher's own site is the only prose on the ToolPageGenerator path, and the same
        // block that permits a partner quote forbids quoting this.
        CrawledHeadings: ["How we work"],
        CrawledParagraphs: ["We deploy finance automation for mid-market teams."],
        JsonLdStructuredSummary: null,
        KeywordSources: [],
        PeopleAlsoAskQuestions: [],
        PublisherName: "Geek",
        PublisherLogoUrl: "https://geek.test/logo.png",
        AuthorName: "Author",
        ArticleBaseUrl: "https://geek.test/articles",
        BlogBaseUrl: "https://geek.test/blog",
        ToolBaseUrl: "https://geek.test/tools",
        ImplementerPositioning: "an AI implementation partner",
        Provider: LlmProviderType.OpenAi);

    private static string ToolPrompt(bool quotableSourceAvailable, string? researchJson)
    {
        var context = Context();
        var app = new SoftwareApplicationDescriptor("Tipalti", "Payables automation.");
        var request = new ContentPromptBuilder().BuildToolBodyPrompt(
            context,
            new ArticleMetadataDraft("Tipalti", "Meta", ["ai"], []),
            app,
            "tipalti",
            ToolPrompts.Outline(context, app.Name),
            revisionNotes: null,
            extractedToolResearchJson: researchJson,
            lede: null,
            quotableSourceAvailable: quotableSourceAvailable);
        return string.Join("\n", request.Messages.Select(m => m.Content));
    }

    [Fact]
    public void With_no_partner_wording_the_page_may_not_quote_at_all()
    {
        // ToolPageGenerator's shape: a name and a link, which is not wording anyone published.
        var system = ToolPrompt(false, """{"name":"Tipalti","href":"https://tipalti.com"}""");

        Assert.Contains("this page carries no block quotation", system, StringComparison.Ordinal);
        Assert.Contains("Do not emit a paragraph of type \"quote\"", system, StringComparison.Ordinal);
        Assert.Contains("do not set \"cite\" on anything", system, StringComparison.Ordinal);
    }

    [Fact]
    public void With_no_partner_wording_the_prompt_does_not_still_invite_one()
    {
        // The permission and the prohibition must not both be in the prompt; the model would be
        // choosing between them.
        var system = ToolPrompt(false, null);

        Assert.DoesNotContain("copied exactly", system, StringComparison.Ordinal);
    }

    [Fact]
    public void With_grounded_partner_evidence_a_quote_is_allowed_but_pinned_to_it()
    {
        // The Create path's shape: a grounded extraction, every item carrying its verbatim span.
        var system = ToolPrompt(true, """{"citables":[{"claim":"Cuts approval time","quote":"Cuts approval time"}]}""");

        Assert.Contains("copied exactly", system, StringComparison.Ordinal);
        Assert.Contains("the URL that evidence names as their source", system, StringComparison.Ordinal);
        Assert.DoesNotContain("this page carries no block quotation", system, StringComparison.Ordinal);
    }

    [Fact]
    public void An_allowed_quote_still_refuses_a_tidied_paraphrase()
    {
        // The failure this guards is not an absent quote, it is a near one presented as exact.
        var system = ToolPrompt(true, """{"citables":[{"claim":"x","quote":"x"}]}""");

        Assert.Contains("not a paraphrase tidied into quotation marks", system, StringComparison.Ordinal);
        Assert.Contains("not a claim you are confident they make", system, StringComparison.Ordinal);
    }

    [Fact]
    public void The_product_is_named_in_the_rule_either_way()
    {
        Assert.Contains("QUOTING Tipalti", ToolPrompt(true, "{}"), StringComparison.Ordinal);
        Assert.Contains("QUOTING Tipalti", ToolPrompt(false, null), StringComparison.Ordinal);
    }
}
