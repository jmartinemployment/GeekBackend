using GeekApplication.Models.Social;

namespace GeekBackend.Tests.Social;

/// <summary>
/// The vocabularies the agents and the store share. The store keeps a triage outcome and the rules a
/// refused draft broke, and the database checks both against these lists, so a list that drifts from
/// its neighbours is a value the store cannot hold.
/// </summary>
public class SocialAgentContractTests
{
    [Fact]
    public void An_untriaged_filter_is_a_way_to_ask_and_never_a_value_the_store_holds()
    {
        Assert.DoesNotContain(TriageOutcomes.Untriaged, TriageOutcomes.All);
        Assert.Equal(TriageOutcomes.All.Append(TriageOutcomes.Untriaged).OrderBy(v => v), TriageOutcomes.Filters.OrderBy(v => v));
        Assert.False(TriageOutcomes.IsKnown(TriageOutcomes.Untriaged));
        Assert.True(TriageOutcomes.IsKnownFilter(TriageOutcomes.Untriaged));
    }

    [Fact]
    public void Only_an_outcome_that_means_triage_did_not_finish_may_be_run_again()
    {
        Assert.Equal(
            new[] { TriageOutcomes.BlockedNoClearance, TriageOutcomes.Failed }.OrderBy(v => v),
            TriageOutcomes.Rerunnable.OrderBy(v => v));
        Assert.All(TriageOutcomes.Rerunnable, outcome => Assert.True(TriageOutcomes.IsKnown(outcome)));
        Assert.DoesNotContain(TriageOutcomes.ReplyNeeded, TriageOutcomes.Rerunnable);
        Assert.DoesNotContain(TriageOutcomes.NoReplyNeeded, TriageOutcomes.Rerunnable);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("em_dash", true)]
    [InlineData("too_long", true)]
    [InlineData("EM_DASH", false)]
    [InlineData("swearing", false)]
    public void A_review_rule_is_known_only_by_its_exact_name(string? rule, bool expected) =>
        Assert.Equal(expected, ReviewRules.IsKnown(rule));

    [Fact]
    public void Every_review_rule_is_listed_once()
    {
        Assert.Equal(ReviewRules.All.Count, ReviewRules.All.Distinct().Count());
        Assert.Equal(7, ReviewRules.All.Count);
    }

    [Fact]
    public void The_drafter_is_handed_the_category_and_answers_with_text_only()
    {
        var request = new ReplyDraftRequest { Category = ReplyCategories.Praise };
        var answer = new ReplyDraftSuggestion { DraftText = "Thank you." };

        Assert.Equal(ReplyCategories.Praise, request.Category);
        Assert.Equal("Thank you.", answer.DraftText);
        Assert.DoesNotContain(typeof(ReplyDraftSuggestion).GetProperties(), p => p.Name is "Category" or "NeedsHumanReason");
    }
}
