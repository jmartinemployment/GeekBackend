using GeekAPI.Services.Social;
using GeekAPI.Services.Social.Review;
using GeekApplication.Interfaces.Social;
using GeekApplication.Models.Social;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeekBackend.Tests.Social;

/// <summary>
/// The Reviewer refuses what Jeff's copy rules forbid and says which rule it broke. Each rule has a
/// text that breaks it and a near miss that does not, because a pattern that refuses everything is as
/// wrong as one that refuses nothing.
/// </summary>
public class OutboundTextReviewerTests
{
    private static readonly OutboundTextReviewer Reviewer = new();

    private static OutboundTextReview Review(
        string text,
        string kind = OutboundTextKinds.Reply,
        string platform = SocialPlatforms.Facebook,
        params string[] approvedLinks)
    {
        var result = Reviewer.Review(new OutboundText
        {
            Kind = kind,
            Platform = platform,
            Text = text,
            ApprovedLinks = approvedLinks.ToList(),
        });

        Assert.True(result.IsSuccess, result.Error);
        return result.Value!;
    }

    [Fact]
    public void A_plain_friendly_reply_passes_with_no_rule_broken()
    {
        var review = Review("Thanks for reading. Most small businesses we work with start with one workflow, then add more.");

        Assert.True(review.Passed);
        Assert.Empty(review.Violations);
    }

    [Fact]
    public void An_em_dash_is_refused_and_a_hyphen_is_not()
    {
        Assert.Equal(new[] { ReviewRules.EmDash }, Review("Good question — start with payroll.").Violations);
        Assert.True(Review("Good question, start with payroll and follow-up work.").Passed);
    }

    [Theory]
    [InlineData("Built for SMBs of every size.")]
    [InlineData("Great for an SMB owner.")]
    [InlineData("smb tools are cheap.")]
    [InlineData("Our SMB's payroll runs weekly.")]
    public void The_word_SMB_is_refused_in_any_form(string text) =>
        Assert.Equal(new[] { ReviewRules.Smb }, Review(text).Violations);

    [Fact]
    public void A_word_that_only_contains_the_letters_is_not_the_word_SMB() =>
        Assert.True(Review("The smbclient command lists shares.").Passed);

    [Theory]
    [InlineData("We are at 123 Main Street, Naples.")]
    [InlineData("Come by 4500 North Tamiami Trail.")]
    [InlineData("Send it to 12 Oak Ave.")]
    [InlineData("Mail to PO Box 4417.")]
    [InlineData("Mail to P.O. Box 4417.")]
    public void A_street_address_is_refused(string text) =>
        Assert.Equal(new[] { ReviewRules.HomeAddress }, Review(text).Violations);

    [Theory]
    [InlineData("We cut month-end close from 10 days to 4.")]
    [InlineData("Five ways to cut costs this quarter.")]
    [InlineData("It took 3 weeks.")]
    public void A_number_beside_words_is_not_an_address(string text) =>
        Assert.True(Review(text).Passed);

    [Theory]
    [InlineData("Call 239-555-0142 today.")]
    [InlineData("Call (239) 555-0142 today.")]
    [InlineData("Call 239.555.0142 today.")]
    [InlineData("Call 2395550142 today.")]
    [InlineData("Call +1 239 555 0142 today.")]
    [InlineData("Call +44 20 7946 0958 today.")]
    public void A_phone_number_is_refused(string text) =>
        Assert.Equal(new[] { ReviewRules.PhoneNumber }, Review(text).Violations);

    [Theory]
    [InlineData("It was published on 2026-11-10.")]
    [InlineData("We handled 1,200 invoices in 2025.")]
    [InlineData("Version 5.2.1 shipped.")]
    public void A_date_or_a_count_is_not_a_phone_number(string text) =>
        Assert.True(Review(text).Passed);

    [Theory]
    [InlineData("Write to jeff@example.com for details.")]
    [InlineData("Write to first.last+tag@sub.example.co for details.")]
    public void An_email_address_is_refused_and_its_domain_is_not_also_a_link(string text) =>
        Assert.Equal(new[] { ReviewRules.EmailAddress }, Review(text).Violations);

    [Fact]
    public void An_at_mention_is_not_an_email_address() =>
        Assert.True(Review("Thanks @geekatyourspot for the share.").Passed);

    [Theory]
    [InlineData("See https://example.com/page for more.")]
    [InlineData("See http://example.com/page for more.")]
    [InlineData("See www.example.com for more.")]
    [InlineData("See example.com/blog for more.")]
    [InlineData("See example.io for more.")]
    public void A_link_that_was_not_approved_is_refused(string text) =>
        Assert.Equal(new[] { ReviewRules.UnapprovedLink }, Review(text).Violations);

    [Theory]
    [InlineData("See https://geekatyourspot.com/blog/ai-payroll for more.")]
    [InlineData("See https://geekatyourspot.com/blog/ai-payroll/ for more.")]
    [InlineData("See http://geekatyourspot.com/blog/ai-payroll for more.")]
    [InlineData("See www.geekatyourspot.com/blog/ai-payroll for more.")]
    [InlineData("See https://GEEKATYOURSPOT.com/blog/ai-payroll. It helps.")]
    [InlineData("See (https://geekatyourspot.com/blog/ai-payroll) for more.")]
    public void The_approved_link_passes_however_it_is_spelled_and_punctuated(string text)
    {
        var review = Review(text, approvedLinks: "https://geekatyourspot.com/blog/ai-payroll");

        Assert.True(review.Passed, string.Join(",", review.Violations));
    }

    [Fact]
    public void A_different_page_on_an_approved_site_is_a_link_that_was_not_approved()
    {
        var review = Review(
            "See https://geekatyourspot.com/blog/something-else for more.",
            approvedLinks: "https://geekatyourspot.com/blog/ai-payroll");

        Assert.Equal(new[] { ReviewRules.UnapprovedLink }, review.Violations);
    }

    [Fact]
    public void One_unapproved_link_among_approved_ones_refuses_the_text()
    {
        var review = Review(
            "Read https://geekatyourspot.com/a and https://other.example.com/b.",
            approvedLinks: "https://geekatyourspot.com/a");

        Assert.Equal(new[] { ReviewRules.UnapprovedLink }, review.Violations);
    }

    [Theory]
    [InlineData("Node.js and Vue.js are fine.")]
    [InlineData("That is, e.g. a spreadsheet.")]
    public void Dotted_words_that_are_not_hosts_are_not_links(string text) =>
        Assert.True(Review(text).Passed);

    [Fact]
    public void A_reply_over_the_house_limit_is_too_long_and_one_at_the_limit_is_not()
    {
        Assert.True(Review(new string('a', ReviewLimits.ReplyCharacters)).Passed);
        Assert.Equal(new[] { ReviewRules.TooLong }, Review(new string('a', ReviewLimits.ReplyCharacters + 1)).Violations);
    }

    [Fact]
    public void A_post_may_be_longer_than_a_reply()
    {
        var text = new string('a', ReviewLimits.ReplyCharacters + 1);

        Assert.True(Review(text, OutboundTextKinds.Post).Passed);
        Assert.Equal(
            new[] { ReviewRules.TooLong },
            Review(new string('a', ReviewLimits.PostCharacters + 1), OutboundTextKinds.Post).Violations);
    }

    [Fact]
    public void Every_rule_a_text_breaks_is_named_in_the_order_the_rules_are_listed()
    {
        var text = "Hey SMB owner — call 239-555-0142 or write jeff@example.com, visit 12 Oak Ave, or see https://other.example.com/x. "
            + new string('a', ReviewLimits.ReplyCharacters);

        Assert.Equal(ReviewRules.All, Review(text).Violations);
    }

    [Fact]
    public void Every_violation_is_a_rule_the_store_can_hold()
    {
        var text = "Hey SMB owner — call 239-555-0142 or write jeff@example.com, visit 12 Oak Ave, or see https://other.example.com/x.";

        Assert.All(Review(text).Violations, rule => Assert.True(ReviewRules.IsKnown(rule)));
    }

    [Fact]
    public void The_reviewer_never_rewrites_the_text_it_was_given()
    {
        var original = new OutboundText { Kind = OutboundTextKinds.Reply, Platform = SocialPlatforms.Facebook, Text = "Good — SMB." };

        var result = Reviewer.Review(original);

        Assert.True(result.IsSuccess);
        Assert.Equal("Good — SMB.", original.Text);
    }

    [Theory]
    [InlineData("", OutboundTextKinds.Reply, SocialPlatforms.Facebook)]
    [InlineData("  ", OutboundTextKinds.Reply, SocialPlatforms.Facebook)]
    [InlineData("Hello.", "tweet", SocialPlatforms.Facebook)]
    [InlineData("Hello.", OutboundTextKinds.Reply, "myspace")]
    [InlineData("Hello.", OutboundTextKinds.Reply, "")]
    public void Text_it_cannot_review_is_a_failed_result_and_never_a_pass(string text, string kind, string platform)
    {
        var result = Reviewer.Review(new OutboundText { Kind = kind, Platform = platform, Text = text });

        Assert.False(result.IsSuccess);
        Assert.Equal(SocialFailureCodes.InvalidRequest, result.ErrorCode);
        Assert.Null(result.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a link")]
    [InlineData("ftp://example.com/file")]
    public void An_approved_link_it_cannot_compare_is_a_failed_result(string approved)
    {
        var result = Reviewer.Review(new OutboundText
        {
            Kind = OutboundTextKinds.Reply,
            Platform = SocialPlatforms.Facebook,
            Text = "Hello.",
            ApprovedLinks = new List<string> { approved },
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(SocialFailureCodes.InvalidRequest, result.ErrorCode);
    }

    [Fact]
    public void An_error_message_never_carries_the_text_it_was_given()
    {
        var result = Reviewer.Review(new OutboundText
        {
            Kind = "tweet",
            Platform = SocialPlatforms.Facebook,
            Text = "A secret remark from a commenter.",
        });

        Assert.DoesNotContain("secret remark", result.Error);
    }

    [Fact]
    public void The_reviewer_is_registered_once_for_every_caller()
    {
        var services = new ServiceCollection();
        services.AddSocialAgents();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        Assert.IsType<OutboundTextReviewer>(provider.GetRequiredService<IOutboundTextReviewer>());
        Assert.Same(
            provider.GetRequiredService<IOutboundTextReviewer>(),
            provider.GetRequiredService<IOutboundTextReviewer>());
    }
}
