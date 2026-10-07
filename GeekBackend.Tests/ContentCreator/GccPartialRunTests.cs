using System.Text.Json;
using GeekAPI.Services.ContentCreator;
using GeekApplication.Models.ContentCreator;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// A type that cannot be written is that type's refusal, not the run's (Jeff, 2026-10-06: "When some
/// types write, those save and the refused ones stay empty"; 2026-10-07: "One failure should not kill
/// the batch"). On 2026-10-07 five good pages (social, email, three tool pages) were thrown away beside
/// a refused pillar, blog and two tool pages, because the coordinator threw as soon as any type failed.
/// </summary>
public sealed class GccPartialRunTests
{
    private static GccGenerationCoordinator.GeneratedPiece Piece(string type, string name) =>
        new(type, """{"warnings":[]}""", name);

    private static GccGenerationCoordinator.TypeOutcome Wrote(params GccGenerationCoordinator.GeneratedPiece[] pieces) =>
        new(pieces, []);

    private static GccRunSettlement.TypeAttempt Wrote(string type, string name) =>
        GccRunSettlement.TypeAttempt.Wrote(type, Wrote(Piece(type, name)));

    private static GccRunSettlement.TypeAttempt Refused(string type, string reason) =>
        GccRunSettlement.TypeAttempt.Refused(type, reason);

    // ---- Settle ----------------------------------------------------------------------------------

    [Fact]
    public void The_types_that_wrote_are_kept_and_the_ones_that_did_not_are_named_with_their_reason()
    {
        // The 2026-10-07 shape: five requested, pillar and blog refused, the rest written.
        var settled = GccRunSettlement.Settle(
        [
            Refused("pillar", "Refused: the pillar. the draft has a gap"),
            Refused("blog", "Refused: the blog. a link leads nowhere"),
            Wrote("email-cold-outreach", "Cold outreach"),
            Wrote("social", "Social"),
        ]);

        Assert.Equal(["email-cold-outreach", "social"], settled.Pieces.Select(p => p.ContentType));
        Assert.Equal(
            ["pillar: Refused: the pillar. the draft has a gap", "blog: Refused: the blog. a link leads nowhere"],
            settled.Refusals.Select(r => r.Line));
        Assert.Equal(["pillar", "blog"], settled.Refusals.Select(r => r.Type));
        Assert.Equal(
            ["Refused: the pillar. the draft has a gap", "Refused: the blog. a link leads nowhere"],
            settled.Refusals.Select(r => r.Text));
    }

    [Fact]
    public void A_run_where_every_type_wrote_has_nothing_to_refuse()
    {
        var settled = GccRunSettlement.Settle([Wrote("pillar", "Pillar"), Wrote("blog", "Blog")]);

        Assert.Equal(2, settled.Pieces.Count);
        Assert.Empty(settled.Refusals);
    }

    [Fact]
    public void The_pieces_come_back_in_the_order_the_types_were_requested()
    {
        var settled = GccRunSettlement.Settle(
            [Wrote("social", "S"), Refused("blog", "Refused: x"), Wrote("pillar", "P"), Wrote("email-cold-outreach", "E")]);

        Assert.Equal(["social", "pillar", "email-cold-outreach"], settled.Pieces.Select(p => p.ContentType));
    }

    [Fact]
    public void A_partner_a_tool_run_refused_keeps_its_line_as_it_always_read_and_the_pages_that_wrote_are_kept()
    {
        var tool = GccRunSettlement.TypeAttempt.Wrote(
            "tool",
            new GccGenerationCoordinator.TypeOutcome(
                [Piece("tool", "Ramp: Automated Approval Workflows")],
                ["Bill: Refused: money that is not in US dollars"]));

        var settled = GccRunSettlement.Settle([Refused("pillar", "Refused: the pillar. x"), tool]);

        Assert.Equal(["Ramp: Automated Approval Workflows"], settled.Pieces.Select(p => p.ArtifactName));
        Assert.Equal(
            ["pillar: Refused: the pillar. x", "Bill: Refused: money that is not in US dollars"],
            settled.Refusals.Select(r => r.Line));
        Assert.Equal("tool", settled.Refusals[1].Type);
        Assert.Equal("Bill: Refused: money that is not in US dollars", settled.Refusals[1].Text);
    }

    [Fact]
    public void A_run_where_no_type_wrote_fails_and_says_why_for_each_type_as_it_always_did()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => GccRunSettlement.Settle(
            [Refused("pillar", "Refused: the pillar. a"), Refused("blog", "Refused: the blog. b")]));

        Assert.Equal("pillar: Refused: the pillar. a | blog: Refused: the blog. b", ex.Message);
    }

    [Fact]
    public void Nothing_to_settle_is_a_failure_not_an_empty_success()
    {
        Assert.Throws<InvalidOperationException>(() => GccRunSettlement.Settle([]));
    }

    [Fact]
    public void An_attempt_that_both_wrote_and_refused_or_did_neither_cannot_be_settled()
    {
        var both = new GccRunSettlement.TypeAttempt("pillar", Wrote(Piece("pillar", "P")), "Refused: x");
        var neither = new GccRunSettlement.TypeAttempt("blog", null, null);

        Assert.Throws<InvalidOperationException>(() => GccRunSettlement.Settle([both, Wrote("social", "S")]));
        Assert.Throws<InvalidOperationException>(() => GccRunSettlement.Settle([neither, Wrote("social", "S")]));
    }

    [Fact]
    public void A_type_that_reports_success_with_no_piece_cannot_be_settled()
    {
        var empty = GccRunSettlement.TypeAttempt.Wrote("pillar", new GccGenerationCoordinator.TypeOutcome([], []));

        Assert.Throws<InvalidOperationException>(() => GccRunSettlement.Settle([empty, Wrote("social", "S")]));
    }

    // ---- AttemptAsync and the run's record -------------------------------------------------------

    private static (List<GccGenerateJobEventWrite> Written, GccRunLog Log) BeginLog()
    {
        var written = new List<GccGenerateJobEventWrite>();
        var log = GccRunLog.Begin(
            Guid.NewGuid(), (events, _) => { written.AddRange(events); return Task.CompletedTask; }, NullLogger.Instance);
        return (written, log);
    }

    [Fact]
    public async Task A_type_that_wrote_is_recorded_under_its_own_piece_with_what_it_wrote()
    {
        var (written, _) = BeginLog();

        var attempt = await GccGenerationCoordinator.AttemptAsync(
            NullLogger.Instance, "social", Guid.NewGuid(), () => Task.FromResult(Wrote(Piece("social", "Social"))));

        Assert.NotNull(attempt.Outcome);
        Assert.Null(attempt.Error);
        var outcome = Assert.Single(written);
        Assert.Equal("outcome", outcome.Kind);
        Assert.Equal("social", outcome.Piece);
        using var doc = JsonDocument.Parse(outcome.PayloadJson);
        Assert.Equal("Social", doc.RootElement.GetProperty("written")[0].GetProperty("artifactName").GetString());
    }

    [Fact]
    public async Task A_refusal_is_that_types_attempt_and_its_record_says_refusal_without_a_stack()
    {
        var (written, _) = BeginLog();

        var attempt = await GccGenerationCoordinator.AttemptAsync(
            NullLogger.Instance, "pillar", Guid.NewGuid(),
            () => throw new InvalidOperationException("Refused: the pillar. a link leads nowhere"));

        Assert.Null(attempt.Outcome);
        Assert.Equal("Refused: the pillar. a link leads nowhere", attempt.Error);
        var outcome = Assert.Single(written);
        Assert.Equal("pillar", outcome.Piece);
        using var doc = JsonDocument.Parse(outcome.PayloadJson);
        Assert.Equal("Refused: the pillar. a link leads nowhere", doc.RootElement.GetProperty("error").GetString());
        var fault = doc.RootElement.GetProperty("fault");
        Assert.Equal("refusal", fault.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, fault.GetProperty("stackTrace").ValueKind);
    }

    [Fact]
    public async Task A_fault_in_the_code_is_that_types_attempt_too_and_its_record_carries_the_type_the_stack_and_the_cause()
    {
        var (written, _) = BeginLog();

        var attempt = await GccGenerationCoordinator.AttemptAsync(
            NullLogger.Instance, "blog", Guid.NewGuid(),
            () => throw new NullReferenceException("an unset section", new FormatException("the model's reply")));

        Assert.Null(attempt.Outcome);
        Assert.Equal("an unset section", attempt.Error);
        var outcome = Assert.Single(written);
        using var doc = JsonDocument.Parse(outcome.PayloadJson);
        var fault = doc.RootElement.GetProperty("fault");
        Assert.Equal("fault", fault.GetProperty("kind").GetString());
        Assert.Equal("System.NullReferenceException", fault.GetProperty("type").GetString());
        Assert.False(string.IsNullOrWhiteSpace(fault.GetProperty("stackTrace").GetString()));
        var inner = Assert.Single(fault.GetProperty("inner").EnumerateArray());
        Assert.Equal("System.FormatException", inner.GetProperty("type").GetString());
        Assert.Equal("the model's reply", inner.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Cancellation_is_not_a_types_refusal_and_stops_the_run()
    {
        await Assert.ThrowsAsync<OperationCanceledException>(() => GccGenerationCoordinator.AttemptAsync(
            NullLogger.Instance, "pillar", Guid.NewGuid(), () => throw new OperationCanceledException()));
    }

    [Fact]
    public async Task Three_types_one_throwing_still_come_back_as_three_attempts_for_the_settlement()
    {
        // The coordinator's fan-out in miniature: every attempt is kept, none is lost to whichever
        // task lost the race, and the settlement keeps what wrote.
        var attempts = await Task.WhenAll(
            GccGenerationCoordinator.AttemptAsync(NullLogger.Instance, "pillar", Guid.NewGuid(),
                () => throw new InvalidOperationException("Refused: the pillar. x")),
            GccGenerationCoordinator.AttemptAsync(NullLogger.Instance, "blog", Guid.NewGuid(),
                () => throw new InvalidOperationException("Refused: the blog. y")),
            GccGenerationCoordinator.AttemptAsync(NullLogger.Instance, "social", Guid.NewGuid(),
                () => Task.FromResult(Wrote(Piece("social", "Social")))));

        var settled = GccRunSettlement.Settle(attempts);

        Assert.Equal("social", Assert.Single(settled.Pieces).ContentType);
        Assert.Equal(["pillar", "blog"], settled.Refusals.Select(r => r.Type));
    }

    // ---- GccRunFault -----------------------------------------------------------------------------

    [Fact]
    public void A_message_with_Refused_is_a_refusal_and_anything_else_is_a_fault()
    {
        Assert.True(GccRunFault.IsRefusal(new InvalidOperationException("Refused: the blog. x")));
        Assert.True(GccRunFault.IsRefusal(new InvalidOperationException("pillar: Refused: a | blog: Refused: b")));
        Assert.False(GccRunFault.IsRefusal(new InvalidOperationException("Object reference not set")));
        Assert.False(GccRunFault.IsRefusal(new HttpRequestException("repository down")));
    }

    [Fact]
    public void A_long_stack_is_cut_and_a_long_cause_chain_is_capped()
    {
        Exception chain = new InvalidOperationException("root");
        for (var i = 0; i < 12; i++) chain = new InvalidOperationException($"wrapper {i}", chain);

        var described = JsonSerializer.SerializeToElement(GccRunFault.Describe(chain));

        Assert.Equal(5, described.GetProperty("inner").GetArrayLength());
    }
}
