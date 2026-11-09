using GeekApplication.Models.Social;
using GeekApplication.Results;
using Xunit;

namespace GeekBackend.Tests.Social;

public class ReplyDraftStatusTests
{
    [Theory]
    [InlineData(ReplyDraftStatuses.Pending, ReplyDraftStatuses.Approved)]
    [InlineData(ReplyDraftStatuses.Pending, ReplyDraftStatuses.Rejected)]
    [InlineData(ReplyDraftStatuses.Approved, ReplyDraftStatuses.Sending)]
    [InlineData(ReplyDraftStatuses.Approved, ReplyDraftStatuses.Rejected)]
    [InlineData(ReplyDraftStatuses.Sending, ReplyDraftStatuses.Sent)]
    [InlineData(ReplyDraftStatuses.Sending, ReplyDraftStatuses.Failed)]
    public void The_allowed_moves(string from, string to)
    {
        Assert.True(ReplyDraftStatuses.CanTransition(from, to));
    }

    [Theory]
    [InlineData(ReplyDraftStatuses.Pending, ReplyDraftStatuses.Sending)]
    [InlineData(ReplyDraftStatuses.Pending, ReplyDraftStatuses.Sent)]
    [InlineData(ReplyDraftStatuses.Approved, ReplyDraftStatuses.Sent)]
    [InlineData(ReplyDraftStatuses.Sending, ReplyDraftStatuses.Pending)]
    [InlineData(ReplyDraftStatuses.Sending, ReplyDraftStatuses.Rejected)]
    public void The_moves_that_would_skip_or_undo_a_step(string from, string to)
    {
        Assert.False(ReplyDraftStatuses.CanTransition(from, to));
    }

    [Fact]
    public void A_failed_draft_is_never_sent_again()
    {
        foreach (var next in ReplyDraftStatuses.All)
            Assert.False(ReplyDraftStatuses.CanTransition(ReplyDraftStatuses.Failed, next), next);
    }

    [Theory]
    [InlineData(ReplyDraftStatuses.Rejected)]
    [InlineData(ReplyDraftStatuses.Sent)]
    public void Rejected_and_sent_are_final(string final)
    {
        foreach (var next in ReplyDraftStatuses.All)
            Assert.False(ReplyDraftStatuses.CanTransition(final, next), next);
    }

    [Fact]
    public void An_unknown_status_moves_nowhere()
    {
        Assert.False(ReplyDraftStatuses.CanTransition("archived", ReplyDraftStatuses.Pending));
        Assert.False(ReplyDraftStatuses.CanTransition(ReplyDraftStatuses.Pending, "archived"));
        Assert.False(ReplyDraftStatuses.IsKnown("archived"));
    }

    [Fact]
    public void Complaint_and_sensitive_are_never_auto_sent()
    {
        Assert.Equal(new[] { ReplyCategories.Complaint, ReplyCategories.Sensitive }, ReplyCategories.NeverAutoSend);

        foreach (var category in ReplyCategories.NeverAutoSend)
            Assert.Contains(category, ReplyCategories.All);
    }
}

public class ResultErrorCodeTests
{
    [Fact]
    public void A_failure_raised_with_a_code_carries_it()
    {
        var result = Result<string>.Failure("The platform throttled the call.", SocialFailureCodes.RateLimited);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Failure, result.Status);
        Assert.Equal("The platform throttled the call.", result.Error);
        Assert.Equal(SocialFailureCodes.RateLimited, result.ErrorCode);
    }

    [Fact]
    public void A_failure_raised_without_a_code_has_none()
    {
        var failure = Result<string>.Failure("Broken.");
        var notFound = Result<string>.NotFound("Gone.");

        Assert.Null(failure.ErrorCode);
        Assert.Null(notFound.ErrorCode);
        Assert.Equal(ResultStatus.NotFound, notFound.Status);
    }

    [Fact]
    public void A_success_has_no_error_and_no_code()
    {
        var result = Result<string>.Success("ok");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
        Assert.Null(result.ErrorCode);
    }
}
