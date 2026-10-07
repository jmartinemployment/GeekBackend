using System.Text.Json;
using GeekAPI.Services.ContentCreator;
using GeekApplication.Models.ContentCreator;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// A reply nobody could use is refused by one failure that says the same things whichever call it was, is classified as
/// the model's output rather than a fault in the code, and shows in the run's record by name. Before this the eight
/// parse methods wrote eight messages (some with a reason and a truncation hint, some with neither), and every one was
/// recorded as a fault with a stack pointing at the parser.
/// </summary>
public sealed class UnusableReplyTests
{
    private const string NotJson = "I'm sorry, I can't help with that.";

    private const string StrayQuoteLede =
        """{"ledeType":"anecdotal","heading":"H","paragraphs":[{"type":"text","runs":[{"text":"supported by "},"{"text":"Stampli"}]}]}""";

    private sealed record Sample(string? Name);

    public static TheoryData<string, string, Func<string, string, object?>> EveryParseMethod => new()
    {
        { "structured section", "label", (raw, label) => LlmResponseJsonParser.ParseSection(raw, "h2", label) },
        { "sections array", "label", (raw, label) => LlmResponseJsonParser.ParseSections(raw, label) },
        { "lede", "label", (raw, label) => LlmResponseJsonParser.ParseLede(raw, label) },
        { "lede+introduction", "label", (raw, label) => LlmResponseJsonParser.ParseLedeAndIntroduction(raw, label) },
        { "JSON object", "label", (raw, label) => LlmResponseJsonParser.Parse<Sample>(raw, label) },
        { "social post", "label", (raw, label) => LlmResponseJsonParser.ParseSocialText(raw, "https://x.test/", label) },
        { "cold-outreach email", "label", (raw, label) => LlmResponseJsonParser.ParseColdOutreach(raw, label) },
        {
            "section image prompts", "label",
            (raw, label) => LlmResponseJsonParser.ParseSectionImagePrompts(raw, [], label)
        },
    };

    // ---- one failure, whichever call ------------------------------------------------------------

    [Theory]
    [MemberData(nameof(EveryParseMethod))]
    public void Every_parse_method_refuses_a_reply_it_cannot_read_with_the_same_fields(
        string what, string label, Func<string, string, object?> parse)
    {
        var ex = Assert.Throws<ContentGenerationException>(() => parse(NotJson, label));

        Assert.Equal(ContentGenerationFailureKind.UnusableReply, ex.Kind);
        Assert.StartsWith($"Model did not return a valid {what} for {label}. ", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Reason: ", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Repairs tried: none (no named repair changed the reply). ", ex.Message, StringComparison.Ordinal);
        Assert.Contains($"Reply was {NotJson.Length} chars; first 200: {NotJson}.", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_reason_is_the_first_fault_found_in_the_reply_as_the_model_wrote_it()
    {
        // The stray quote makes the reply invalid as written, so the reason is where the parser first choked,
        // not what a later candidate found.
        const string beyondRepair = """{"ledeType":"anecdotal","paragraphs":[{"type":"text","runs":[{"text":"a"},"{"text":"b"]}]}""";

        var ex = Assert.Throws<ContentGenerationException>(() => LlmResponseJsonParser.ParseLede(beyondRepair, "lede"));

        Assert.Contains("Reason: JSON error at byte", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Repairs tried: stray-quote-before-object", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_reply_that_is_valid_json_with_no_paragraphs_says_so()
    {
        var ex = Assert.Throws<ContentGenerationException>(() =>
            LlmResponseJsonParser.ParseLede("""{"ledeType":"anecdotal","paragraphs":[]}""", "lede"));

        Assert.Contains("Reason: the lede carried no paragraphs.", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_reply_says_it_was_empty()
    {
        var ex = Assert.Throws<ContentGenerationException>(() => LlmResponseJsonParser.ParseLede("  ", "lede"));

        Assert.Contains("Reason: the reply was empty.", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Reply was 2 chars", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_reply_cut_off_before_it_closed_says_it_looks_truncated()
    {
        var ex = Assert.Throws<ContentGenerationException>(() =>
            LlmResponseJsonParser.ParseLede("""{"ledeType":"anecdotal","paragraphs":[{"type":"text","runs":[{"text":"stops here""", "lede"));

        Assert.Contains("so it was cut off", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_complete_reply_wrapped_in_a_fence_is_not_called_truncated_for_the_fence()
    {
        var ex = Assert.Throws<ContentGenerationException>(() =>
            LlmResponseJsonParser.ParseLede("```json\n{\"ledeType\":\"anecdotal\",\"paragraphs\":[]}\n```", "lede"));

        Assert.DoesNotContain("cut off", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Content_that_breaks_a_rule_the_call_stated_is_the_same_kind_of_failure()
    {
        const string withMarkup =
            """{"ledeType":"anecdotal","paragraphs":[{"type":"text","runs":[{"text":"This is **bold** text."}]}]}""";

        var ex = Assert.Throws<ContentGenerationException>(() => LlmResponseJsonParser.ParseLede(withMarkup, "lede"));

        Assert.Equal(ContentGenerationFailureKind.UnusableReply, ex.Kind);
        Assert.Contains("stray formatting symbols", ex.Message, StringComparison.Ordinal);
    }

    // ---- classification --------------------------------------------------------------------------

    [Fact]
    public void An_unusable_reply_is_a_refusal_in_the_record_and_has_no_stack()
    {
        var ex = Assert.Throws<ContentGenerationException>(() => LlmResponseJsonParser.ParseLede(NotJson, "lede"));

        Assert.True(GccRunFault.IsRefusal(ex));
        Assert.True(GccRunFault.IsUnusableReply(ex));
        var described = JsonSerializer.SerializeToElement(GccRunFault.Describe(ex));
        Assert.Equal("unusable-reply", described.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, described.GetProperty("stackTrace").ValueKind);
        Assert.Equal(ex.Message, described.GetProperty("message").GetString());
    }

    [Fact]
    public void Any_other_content_generation_failure_is_still_a_fault_in_the_code()
    {
        var ex = new ContentGenerationException("the provider answered 500");

        Assert.False(GccRunFault.IsRefusal(ex));
        Assert.False(GccRunFault.IsUnusableReply(ex));
        Assert.Equal("fault", JsonSerializer.SerializeToElement(GccRunFault.Describe(ex)).GetProperty("kind").GetString());
    }

    // ---- a repair is visible ---------------------------------------------------------------------

    [Fact]
    public void A_reply_that_parsed_only_after_a_repair_is_noted_with_the_call_and_the_repair()
    {
        using var scope = JsonRepairTrace.Begin();

        LlmResponseJsonParser.ParseLede(StrayQuoteLede, "tool page 'Stampli' lede");

        var note = Assert.Single(scope.Drain());
        Assert.Equal("tool page 'Stampli' lede", note.Label);
        Assert.Equal(["stray-quote-before-object"], note.Repairs);
    }

    [Fact]
    public void A_well_formed_reply_leaves_nothing_to_note()
    {
        using var scope = JsonRepairTrace.Begin();

        LlmResponseJsonParser.ParseLede(
            """{"ledeType":"anecdotal","paragraphs":[{"type":"text","runs":[{"text":"fine"}]}]}""", "lede");

        Assert.Empty(scope.Drain());
    }

    [Fact]
    public void A_reply_is_still_repaired_when_no_run_is_listening()
    {
        var (lede, _) = LlmResponseJsonParser.ParseLede(StrayQuoteLede, "lede");

        Assert.Single(lede.Paragraphs);
    }

    [Fact]
    public async Task Two_pieces_running_side_by_side_never_see_each_others_notes()
    {
        async Task<IReadOnlyList<JsonRepairNote>> Piece(string label)
        {
            using var scope = JsonRepairTrace.Begin();
            await Task.Yield();
            LlmResponseJsonParser.ParseLede(StrayQuoteLede, label);
            await Task.Yield();
            return scope.Drain();
        }

        var both = await Task.WhenAll(Piece("pillar lede"), Piece("blog lede"));

        Assert.Equal("pillar lede", Assert.Single(both[0]).Label);
        Assert.Equal("blog lede", Assert.Single(both[1]).Label);
    }

    // ---- the run's record -----------------------------------------------------------------------

    private static (List<GccGenerateJobEventWrite> Written, GccRunLog Log) BeginLog()
    {
        var written = new List<GccGenerateJobEventWrite>();
        var log = GccRunLog.Begin(
            Guid.NewGuid(), (events, _) => { written.AddRange(events); return Task.CompletedTask; }, NullLogger.Instance);
        return (written, log);
    }

    private static GccGenerationCoordinator.TypeOutcome Wrote() =>
        new([new GccGenerationCoordinator.GeneratedPiece("tool", """{"warnings":[]}""", "Stampli")], []);

    [Fact]
    public async Task A_repair_made_while_a_piece_wrote_is_recorded_once_under_that_piece_before_its_outcome()
    {
        var (written, _) = BeginLog();

        await GccGenerationCoordinator.AttemptAsync(NullLogger.Instance, "tool", Guid.NewGuid(), () =>
        {
            LlmResponseJsonParser.ParseLede(StrayQuoteLede, "tool page 'Stampli' lede");
            return Task.FromResult(Wrote());
        });

        Assert.Equal(["repaired", "outcome"], written.Select(e => e.Kind));
        var repaired = written[0];
        Assert.Equal("tool", repaired.Piece);
        using var doc = JsonDocument.Parse(repaired.PayloadJson);
        var entry = Assert.Single(doc.RootElement.GetProperty("repairs").EnumerateArray());
        Assert.Equal("tool page 'Stampli' lede", entry.GetProperty("label").GetString());
        Assert.Equal("stray-quote-before-object", Assert.Single(entry.GetProperty("repairs").EnumerateArray()).GetString());
    }

    [Fact]
    public async Task A_piece_that_needed_no_repair_records_no_repaired_event()
    {
        var (written, _) = BeginLog();

        await GccGenerationCoordinator.AttemptAsync(NullLogger.Instance, "tool", Guid.NewGuid(), () =>
        {
            LlmResponseJsonParser.ParseLede(
                """{"ledeType":"anecdotal","paragraphs":[{"type":"text","runs":[{"text":"fine"}]}]}""", "lede");
            return Task.FromResult(Wrote());
        });

        Assert.Equal(["outcome"], written.Select(e => e.Kind));
    }

    [Fact]
    public async Task A_piece_refused_for_an_unusable_reply_records_the_repairs_tried_and_the_reason_without_a_stack()
    {
        var (written, _) = BeginLog();

        var attempt = await GccGenerationCoordinator.AttemptAsync(NullLogger.Instance, "tool", Guid.NewGuid(), () =>
        {
            LlmResponseJsonParser.ParseLede(StrayQuoteLede, "tool page 'Stampli' lede");
            LlmResponseJsonParser.ParseLede(NotJson, "tool page 'Stampli' lede");
            return Task.FromResult(Wrote());
        });

        Assert.Null(attempt.Outcome);
        Assert.Equal(["repaired", "outcome"], written.Select(e => e.Kind));
        using var doc = JsonDocument.Parse(written[1].PayloadJson);
        var fault = doc.RootElement.GetProperty("fault");
        Assert.Equal("unusable-reply", fault.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, fault.GetProperty("stackTrace").ValueKind);
        Assert.Contains("Reason: ", doc.RootElement.GetProperty("error").GetString(), StringComparison.Ordinal);
    }
}
