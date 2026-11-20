using System.Text.Json;
using System.Text.RegularExpressions;
using GeekAPI.Services.GeekCrawler;
using GeekAPI.Services.Receptionist.Knowledge;
using GeekAPI.Services.Voice.Mcp;
using GeekApplication.Interfaces.Receptionist;
using GeekApplication.Models.ContentCreator;
using Microsoft.Extensions.Logging;

namespace GeekBackend.Tests.Receptionist;

/// <summary>
/// The door between the call's own model and the one thing that answers a caller from the business's own information. The toolbox no
/// longer answers anything itself: it decides whether this tenant gets a tool at all, refuses a tool it never offered, hands the call's
/// turns to <see cref="KnowledgeOrchestrator"/>, and turns what comes back into either a checked answer or a coded refusal that tells
/// the receptionist to take a message. What happens between those two points — formulating, searching, judging, writing, grounding — is
/// the orchestrator's, and is tested where it lives.
///
/// <para>These tests drive the real orchestrator over scripted models rather than a stand-in, because the outcome the toolbox translates
/// has to be an outcome the orchestrator can actually produce.</para>
/// </summary>
public sealed class KnowledgeToolboxTests
{
    /// <summary>What the caller said on the line.</summary>
    private const string Question = "How much do you charge to replace a water heater on a Sunday";

    /// <summary>What the orchestrator's formulator makes of it. The model never writes this any more, and it is never logged.</summary>
    private const string Asked = "What does this business charge to replace a water heater on a Sunday?";

    /// <summary>A sentence off the business's own page — a passage, which has not been through the grounding check.</summary>
    private const string Published = "A Sunday call-out is two hundred and forty dollars before parts.";

    /// <summary>What the orchestrator wrote and checked. Deliberately not the published sentence, so the two cannot be confused.</summary>
    private const string Answer = "It is two hundred and forty dollars for a Sunday call-out, before parts.";

    private static readonly Guid Tenant = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid Run = Guid.Parse("99999999-8888-7777-6666-555555555555");

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Lines { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception) + (exception?.ToString() ?? string.Empty));
    }

    /// <summary>
    /// A Library that answers whatever the test set, and keeps every search it was asked for. The positional overload is not the path
    /// the orchestrator takes; a fake that quietly answered it would hide the day the query object stopped being sent.
    /// </summary>
    private sealed class FakeRag : IGeekCrawlerRagClient
    {
        public bool IsEnabled { get; set; } = true;
        public GeekCrawlerRagQueryResult? Result { get; set; }
        public List<GeekCrawlerRagQuery> Queries { get; } = new();

        public Task<GeekCrawlerRagQueryResult?> QueryAsync(GeekCrawlerRagQuery query, CancellationToken ct = default)
        {
            Queries.Add(query);
            return Task.FromResult(Result);
        }

        public Task<GeekCrawlerRagQueryResult?> QueryAsync(
            string need,
            Guid runId,
            string? crawlType = null,
            string? host = null,
            int topK = 8,
            bool? preferParent = null,
            bool? preferChild = null,
            IReadOnlyList<string>? entityNames = null,
            string? retrievalMode = null,
            IReadOnlyDictionary<string, string>? anchorToolLookup = null,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task<GeekCrawlerRagIndexStatus?> EnqueueIndexAsync(Guid runId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<GeekCrawlerRagIndexStatus?> GetIndexStatusAsync(Guid runId, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<GeekCrawlerRagTemplateIndexResult?> IndexTemplatesAsync(
            IReadOnlyList<GeekCrawlerRagTemplateDto> templates,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task<GeekCrawlerRagTemplateQueryResult?> QueryTemplatesAsync(
            string need,
            int topK = 5,
            string? channel = null,
            IReadOnlyList<string>? entityTags = null,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task<GeekCrawlerRagPageText?> GetPageTextAsync(
            string pageId,
            CancellationToken ct = default,
            string? runId = null) => throw new NotSupportedException();
    }

    /// <summary>
    /// A model that answers whatever the test scripted and keeps what it was shown. One of these stands behind each of the
    /// orchestrator's workers, which is how a test here chooses which outcome the toolbox has to translate.
    /// </summary>
    private sealed class Scripted : IReceptionistModel
    {
        public Scripted(string answer) => Answer = answer;

        public string Answer { get; set; }
        public List<string> Shown { get; } = new();
        public bool IsConfigured => true;

        public Task<ReceptionistReply> ReplyAsync(
            string systemPrompt, IReadOnlyList<ReceptionistTurn> turns, IReadOnlyList<ReceptionistToolDefinition>? tools = null,
            CancellationToken ct = default)
        {
            Shown.Add(turns[^1].Text);
            return Task.FromResult(new ReceptionistReply(Answer, false));
        }

        public Task<ReceptionistCallSummary> SummarizeAsync(IReadOnlyList<ReceptionistTurn> turns, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    /// <summary>
    /// A judge that keeps every passage it is shown, so these tests stay about the toolbox. What the judge itself decides, and what it
    /// does when it fails, is <see cref="KnowledgePassageJudgeTests"/>.
    /// </summary>
    private sealed class KeepEverything : IReceptionistModel
    {
        public bool IsConfigured => true;

        public Task<ReceptionistReply> ReplyAsync(
            string systemPrompt, IReadOnlyList<ReceptionistTurn> turns, IReadOnlyList<ReceptionistToolDefinition>? tools = null,
            CancellationToken ct = default)
        {
            // The judge numbers the passages it shows; keeping all of them means naming every number it used.
            var shown = Regex.Matches(turns[^1].Text, @"^\[(\d+)\] ", RegexOptions.Multiline).Select(m => m.Groups[1].Value);
            return Task.FromResult(new ReceptionistReply($$"""{"keep": [{{string.Join(", ", shown)}}]}""", false));
        }

        public Task<ReceptionistCallSummary> SummarizeAsync(IReadOnlyList<ReceptionistTurn> turns, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    /// <summary>
    /// A tenant whose Library is configured, whose site is indexed, and whose caller has just asked the question: by default every step
    /// succeeds and the toolbox has an answer to hand over. A test changes one thing to choose a different outcome.
    /// </summary>
    private sealed class Rig
    {
        private readonly CapturingLogger<KnowledgeOrchestrator> _planning = new();
        private readonly CapturingLogger<QuestionFormulator> _formulating = new();
        private readonly CapturingLogger<KnowledgePassageJudge> _judging = new();
        private readonly CapturingLogger<AnswerComposer> _composing = new();
        private readonly CapturingLogger<AnswerGrounding> _checking = new();

        public FakeRag Rag { get; } = new();
        public CapturingLogger<KnowledgeToolbox> Log { get; } = new();

        /// <summary>The call's turns so far. The toolbox is built over a closure on this list, exactly as the live wiring is.</summary>
        public List<ReceptionistTurn> Turns { get; } = [new ReceptionistTurn(ReceptionistSpeaker.Caller, Question)];

        public Scripted Formulator { get; } = new(JsonSerializer.Serialize(new { question = Asked }));
        public Scripted Composer { get; } = new(JsonSerializer.Serialize(new { answer = Answer }));
        public Scripted Grounding { get; } = new("""{"supported": true}""");

        public KnowledgeOrchestrator Orchestrator { get; }

        public Rig()
        {
            Rag.Result = Found(Page("Pricing", Published));
            Orchestrator = new KnowledgeOrchestrator(
                new QuestionFormulator(Formulator, _formulating),
                new KnowledgePassageJudge(new KeepEverything(), _judging),
                new AnswerComposer(Composer, _composing),
                new AnswerGrounding(Grounding, _checking),
                _planning);
        }

        /// <summary>Everything anything in this rig logged, so the zero-retention assertion reads the whole path and not just the door.</summary>
        public IEnumerable<string> Lines =>
            Log.Lines
                .Concat(_planning.Lines)
                .Concat(_formulating.Lines)
                .Concat(_judging.Lines)
                .Concat(_composing.Lines)
                .Concat(_checking.Lines);

        public KnowledgeToolbox? Create() => KnowledgeToolbox.Create(Tenant, Run, Rag, Orchestrator, () => Turns, Log);

        public KnowledgeToolbox? CreateWith(Guid? knowledgeRunId) =>
            KnowledgeToolbox.Create(Tenant, knowledgeRunId, Rag, Orchestrator, () => Turns, Log);

        /// <summary>The toolbox a configured tenant with an indexed run gets, for the tests that are about running a tool.</summary>
        public KnowledgeToolbox Box => Create() ?? throw new InvalidOperationException("The rig's own toolbox was refused.");
    }

    private static GccQuoteablePage Page(string title, params string[] paragraphs) =>
        new(Url: "https://example.test/" + Uri.EscapeDataString(title), Title: title, Headings: [], Paragraphs: paragraphs);

    private static GeekCrawlerRagQueryResult Found(params GccQuoteablePage[] pages) =>
        new() { RunId = Run, Pages = pages };

    private static GeekCrawlerRagQueryResult SearchFailed() =>
        new() { RunId = Run, Pages = [], Failed = true, Error = "RAG query unavailable.", Warning = "RAG query unavailable." };

    /// <summary>The lookup as the receptionist asks for it: a name and nothing else, because the tool takes no arguments.</summary>
    private static ReceptionistToolCall Lookup() => new("c1", KnowledgeToolbox.LookupTool, "{}");

    // ----- Whether a business gets the tool at all -----

    [Fact]
    public void A_library_that_is_not_configured_leaves_the_call_with_no_knowledge_tool()
    {
        var rig = new Rig();
        rig.Rag.IsEnabled = false;

        Assert.Null(rig.Create());
        Assert.Contains(rig.Log.Lines, l => l.Contains("evidence library is not configured", StringComparison.Ordinal));
        Assert.Empty(rig.Rag.Queries);
        Assert.Empty(rig.Formulator.Shown);
    }

    [Fact]
    public void A_business_with_no_indexed_site_gets_no_knowledge_tool()
    {
        var rig = new Rig();

        Assert.Null(rig.CreateWith(null));
        Assert.Contains(rig.Log.Lines, l => l.Contains("no indexed site", StringComparison.Ordinal));
        Assert.Empty(rig.Rag.Queries);
        Assert.Empty(rig.Formulator.Shown);
    }

    [Fact]
    public void An_empty_run_id_is_no_indexed_site_and_not_a_run_to_search()
    {
        var rig = new Rig();

        Assert.Null(rig.CreateWith(Guid.Empty));
        Assert.Contains(rig.Log.Lines, l => l.Contains("no indexed site", StringComparison.Ordinal));
        Assert.Empty(rig.Rag.Queries);
        Assert.Empty(rig.Formulator.Shown);
    }

    /// <summary>
    /// The one tool takes no arguments at all. It used to take a <c>question</c> string, and that string was the defect: it was the
    /// call's own model writing down what it thought the caller meant, and the paraphrase — not the caller — was what got embedded and
    /// searched. The orchestrator reads the call's turns instead, so there is nothing left for the model to fill in, and a schema that
    /// still demanded a question would invite it to paraphrase again.
    /// </summary>
    [Fact]
    public void The_one_tool_offered_takes_no_arguments_because_the_model_no_longer_paraphrases_the_caller()
    {
        var tool = Assert.Single(new Rig().Box.Tools);

        Assert.Equal("look_up_business_info", tool.Name);
        Assert.Equal(KnowledgeToolbox.LookupTool, tool.Name);
        Assert.False(string.IsNullOrWhiteSpace(tool.Description));

        Assert.Equal("object", tool.InputSchema.GetProperty("type").GetString());
        Assert.Empty(tool.InputSchema.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToList());
        Assert.False(tool.InputSchema.GetProperty("properties").TryGetProperty("question", out _));
        Assert.False(tool.InputSchema.TryGetProperty("required", out _));
    }

    // ----- What the model may ask for -----

    [Fact]
    public async Task A_tool_this_toolbox_never_offered_is_refused_and_nothing_is_looked_up()
    {
        var rig = new Rig();

        var result = await rig.Box.RunAsync(new ReceptionistToolCall("c1", "create_event", "{}"));

        Assert.False(result.IsSuccess);
        Assert.Equal(McpFailureCodes.ToolNotAllowed, result.ErrorCode);
        Assert.Empty(rig.Formulator.Shown);
        Assert.Empty(rig.Rag.Queries);
    }

    // ----- Turning one outcome into what the receptionist is told -----

    [Fact]
    public async Task An_orchestrator_that_answered_gives_the_receptionist_that_answer()
    {
        var rig = new Rig();

        var result = await rig.Box.RunAsync(Lookup());

        Assert.True(result.IsSuccess);
        Assert.Null(result.ErrorCode);
        using var json = JsonDocument.Parse(result.Value!);
        Assert.Equal(Answer, json.RootElement.GetProperty("answer").GetString());
    }

    /// <summary>
    /// What the receptionist gets is the answer, under <c>answer</c>, and never the passages it was built from. The difference is the
    /// whole point of the orchestrator: the answer has been written from the passages and then checked back against them by a separate
    /// call, and the passages have not been through anything. Handing passages over would put the call's own model back in the position
    /// of writing an answer from raw text with nothing checking it.
    /// </summary>
    [Fact]
    public async Task A_checked_answer_is_handed_over_as_an_answer_and_never_as_passages()
    {
        var rig = new Rig();

        var result = await rig.Box.RunAsync(Lookup());

        Assert.True(result.IsSuccess);
        using var json = JsonDocument.Parse(result.Value!);

        var fields = json.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal("answer", Assert.Single(fields));
        Assert.False(json.RootElement.TryGetProperty("passages", out _));

        // The published sentence the passage carried is not what came back; the checked answer is.
        Assert.Equal(Answer, json.RootElement.GetProperty("answer").GetString());
        Assert.DoesNotContain(Published, result.Value!, StringComparison.Ordinal);
        Assert.DoesNotContain("Pricing", result.Value!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Nothing was asked — the caller said hello, or gave a name. This is the one refusal that does not tell the receptionist to take a
    /// message, because there is no question to take a message about; it simply says there was nothing to look up and the call carries on.
    /// </summary>
    [Fact]
    public async Task Nothing_asked_is_refused_with_that_code_and_does_not_tell_the_receptionist_to_take_a_message()
    {
        var rig = new Rig();
        rig.Formulator.Answer = """{"question": null}""";

        var result = await rig.Box.RunAsync(Lookup());

        Assert.False(result.IsSuccess);
        Assert.Equal(AnswerRefusals.NothingAsked, result.ErrorCode);
        Assert.Equal("There was no question about the business to look up.", result.Error);
        Assert.DoesNotContain("take a message", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The business has published nothing that answers it. The receptionist is told to take a message rather than answer.</summary>
    [Fact]
    public async Task Not_published_is_refused_with_that_code_and_the_receptionist_is_told_to_take_a_message()
    {
        var rig = new Rig();
        // Found nothing, and the second, broader attempt is the same question again — which the orchestrator reads as nothing published.
        rig.Rag.Result = Found();

        var result = await rig.Box.RunAsync(Lookup());

        Assert.False(result.IsSuccess);
        Assert.Equal(AnswerRefusals.NotPublished, result.ErrorCode);
        Assert.Contains("does not cover that", result.Error!, StringComparison.Ordinal);
        Assert.Contains("Take a message instead of answering.", result.Error!, StringComparison.Ordinal);
    }

    /// <summary>An answer was written and the business's own words did not support it. It is discarded, and a message is taken.</summary>
    [Fact]
    public async Task Unsupported_is_refused_with_that_code_and_the_receptionist_is_told_to_take_a_message()
    {
        var rig = new Rig();
        rig.Grounding.Answer = """{"supported": false}""";

        var result = await rig.Box.RunAsync(Lookup());

        Assert.False(result.IsSuccess);
        Assert.Equal(AnswerRefusals.Unsupported, result.ErrorCode);
        Assert.Contains("does not support an answer to that", result.Error!, StringComparison.Ordinal);
        Assert.Contains("Take a message instead of answering.", result.Error!, StringComparison.Ordinal);

        // The rejected draft is never handed over as something to say.
        Assert.Null(result.Value);
    }

    /// <summary>A step could not be run at all. Nothing was established either way, so nothing is said and a message is taken.</summary>
    [Fact]
    public async Task Could_not_check_is_refused_with_that_code_and_the_receptionist_is_told_to_take_a_message()
    {
        var rig = new Rig();
        rig.Rag.Result = SearchFailed();

        var result = await rig.Box.RunAsync(Lookup());

        Assert.False(result.IsSuccess);
        Assert.Equal(AnswerRefusals.CouldNotCheck, result.ErrorCode);
        Assert.Contains("could not be checked", result.Error!, StringComparison.Ordinal);
        Assert.Contains("Take a message instead of answering.", result.Error!, StringComparison.Ordinal);
    }

    // ----- The turns handed over are the call's own, at the moment the tool runs -----

    /// <summary>
    /// The toolbox holds a function, not a list. It has to: the toolbox is built before the call session exists and the session needs
    /// the toolbox, so the live wiring closes over a session variable that is still null at construction. If the turns were read when
    /// the toolbox was built, every lookup on every call would hand the orchestrator an empty call and come back "nothing asked".
    ///
    /// <para>Proved the only way that distinguishes the two: the list is empty when the toolbox is built and the caller's words are added
    /// afterwards, and the orchestrator is shown them.</para>
    /// </summary>
    [Fact]
    public async Task The_turns_handed_over_are_the_ones_the_call_has_when_the_tool_runs_not_when_it_was_built()
    {
        var rig = new Rig();
        rig.Turns.Clear();

        var box = rig.Box;
        rig.Turns.Add(new ReceptionistTurn(ReceptionistSpeaker.Caller, Question));

        var result = await box.RunAsync(Lookup());

        // The formulator is the first thing the orchestrator runs, and it is shown the call as it stands.
        Assert.Contains(Question, Assert.Single(rig.Formulator.Shown), StringComparison.Ordinal);
        Assert.True(result.IsSuccess);
    }

    // ----- Zero Data Retention -----

    [Fact]
    public async Task No_log_line_carries_the_callers_words_or_the_answer()
    {
        var disabled = new Rig();
        disabled.Rag.IsEnabled = false;
        Assert.Null(disabled.Create());

        var noRun = new Rig();
        Assert.Null(noRun.CreateWith(null));

        var notAllowed = new Rig();
        await notAllowed.Box.RunAsync(new ReceptionistToolCall("c1", "create_event", "{}"));

        var nothingAsked = new Rig();
        nothingAsked.Formulator.Answer = """{"question": null}""";
        await nothingAsked.Box.RunAsync(Lookup());

        var notPublished = new Rig();
        notPublished.Rag.Result = Found();
        await notPublished.Box.RunAsync(Lookup());

        var unsupported = new Rig();
        unsupported.Grounding.Answer = """{"supported": false}""";
        await unsupported.Box.RunAsync(Lookup());

        var couldNotCheck = new Rig();
        couldNotCheck.Rag.Result = SearchFailed();
        await couldNotCheck.Box.RunAsync(Lookup());

        var answered = new Rig();
        var success = await answered.Box.RunAsync(Lookup());
        Assert.True(success.IsSuccess);

        var rigs = new[] { disabled, noRun, notAllowed, nothingAsked, notPublished, unsupported, couldNotCheck, answered };
        foreach (var rig in rigs)
        {
            foreach (var line in rig.Lines)
            {
                Assert.DoesNotContain(Question, line, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(Asked, line, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(Published, line, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(Answer, line, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("water heater", line, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("two hundred and forty", line, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
