using System.Text.Json;
using System.Text.RegularExpressions;
using GeekAPI.Services.GeekCrawler;
using GeekAPI.Services.Receptionist.Knowledge;
using GeekAPI.Services.Voice.Mcp;
using GeekApplication.Interfaces.Receptionist;
using GeekApplication.Models.ContentCreator;
using GeekApplication.Models.Voice;
using Microsoft.Extensions.Logging;

namespace GeekBackend.Tests.Receptionist;

/// <summary>
/// The one tool that lets a receptionist answer a question about the business itself: a tenant with no indexed site or no configured
/// Library gets no tool at all rather than one that always answers nothing; the caller's own words go to the Library as the need for
/// that tenant's run; a search that failed and a search that found nothing are different refusals, and neither is an answer; and no log
/// line carries the caller's question or anything the Library said back.
/// </summary>
public sealed class KnowledgeToolboxTests
{
    private const string Question = "How much do you charge to replace a water heater on a Sunday";
    private const string Answer = "A Sunday call-out is two hundred and forty dollars before parts.";
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
    /// the toolbox takes; a fake that quietly answered it would hide the day the toolbox stopped sending a query object.
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

    private sealed class Rig
    {
        public FakeRag Rag { get; } = new();
        public CapturingLogger<KnowledgeToolbox> Log { get; } = new();
        public KnowledgePassageJudge Judge { get; } = new(new KeepEverything(), new CapturingLogger<KnowledgePassageJudge>());

        public KnowledgeToolbox? Create() => KnowledgeToolbox.Create(Tenant, Run, Rag, Judge, Log);

        public KnowledgeToolbox? CreateWith(Guid? knowledgeRunId) => KnowledgeToolbox.Create(Tenant, knowledgeRunId, Rag, Judge, Log);

        /// <summary>The toolbox a configured tenant with an indexed run gets, for the tests that are about running a tool.</summary>
        public KnowledgeToolbox Box => Create() ?? throw new InvalidOperationException("The rig's own toolbox was refused.");
    }

    private static GccQuoteablePage Page(string title, params string[] paragraphs) =>
        new(Url: "https://example.test/" + Uri.EscapeDataString(title), Title: title, Headings: [], Paragraphs: paragraphs);

    private static GeekCrawlerRagQueryResult Found(params GccQuoteablePage[] pages) =>
        new() { RunId = Run, Pages = pages };

    private static GeekCrawlerRagQueryResult SearchFailed() =>
        new() { RunId = Run, Pages = [], Failed = true, Error = "RAG query unavailable.", Warning = "RAG query unavailable." };

    private static ReceptionistToolCall Ask(string question) =>
        new("c1", KnowledgeToolbox.LookupTool, JsonSerializer.Serialize(new { question }));

    private static ReceptionistToolCall AskRaw(string? argumentsJson) =>
        new("c1", KnowledgeToolbox.LookupTool, argumentsJson!);

    // ----- Whether a business gets the tool at all -----

    [Fact]
    public void A_library_that_is_not_configured_leaves_the_call_with_no_knowledge_tool()
    {
        var rig = new Rig();
        rig.Rag.IsEnabled = false;

        Assert.Null(rig.Create());
        Assert.Contains(rig.Log.Lines, l => l.Contains("evidence library is not configured", StringComparison.Ordinal));
        Assert.Empty(rig.Rag.Queries);
    }

    [Fact]
    public void A_business_with_no_indexed_site_gets_no_knowledge_tool()
    {
        var rig = new Rig();

        Assert.Null(rig.CreateWith(null));
        Assert.Contains(rig.Log.Lines, l => l.Contains("no indexed site", StringComparison.Ordinal));
        Assert.Empty(rig.Rag.Queries);
    }

    [Fact]
    public void An_empty_run_id_is_no_indexed_site_and_not_a_run_to_search()
    {
        var rig = new Rig();

        Assert.Null(rig.CreateWith(Guid.Empty));
        Assert.Contains(rig.Log.Lines, l => l.Contains("no indexed site", StringComparison.Ordinal));
        Assert.Empty(rig.Rag.Queries);
    }

    [Fact]
    public void The_toolbox_offers_one_tool_that_takes_the_callers_question_as_a_string()
    {
        var tool = Assert.Single(new Rig().Box.Tools);

        Assert.Equal("look_up_business_info", tool.Name);
        Assert.Equal(KnowledgeToolbox.LookupTool, tool.Name);
        Assert.False(string.IsNullOrWhiteSpace(tool.Description));

        Assert.Equal("object", tool.InputSchema.GetProperty("type").GetString());
        var question = tool.InputSchema.GetProperty("properties").GetProperty("question");
        Assert.Equal("string", question.GetProperty("type").GetString());
        Assert.Contains(
            "question",
            tool.InputSchema.GetProperty("required").EnumerateArray().Select(r => r.GetString()!));
    }

    // ----- What the model may ask for, and how -----

    [Fact]
    public async Task A_tool_this_toolbox_never_offered_is_refused_and_nothing_is_searched()
    {
        var rig = new Rig();

        var result = await rig.Box.RunAsync(new ReceptionistToolCall("c1", "create_event", """{"question":"anything"}"""));

        Assert.False(result.IsSuccess);
        Assert.Equal(McpFailureCodes.ToolNotAllowed, result.ErrorCode);
        Assert.Empty(rig.Rag.Queries);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"question\":")]
    [InlineData("[1,2]")]
    [InlineData("\"just a string\"")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Arguments_that_are_not_a_json_object_are_invalid_request_and_nothing_is_searched(string? argumentsJson)
    {
        var rig = new Rig();

        var result = await rig.Box.RunAsync(AskRaw(argumentsJson));

        Assert.False(result.IsSuccess);
        Assert.Equal(VoiceFailureCodes.InvalidRequest, result.ErrorCode);
        Assert.Empty(rig.Rag.Queries);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"query":"when do you open"}""")]
    [InlineData("""{"question":""}""")]
    [InlineData("""{"question":"   "}""")]
    [InlineData("""{"question":null}""")]
    [InlineData("""{"question":7}""")]
    public async Task Arguments_carrying_no_question_or_an_empty_one_are_invalid_request(string argumentsJson)
    {
        var rig = new Rig();

        var result = await rig.Box.RunAsync(AskRaw(argumentsJson));

        Assert.False(result.IsSuccess);
        Assert.Equal(VoiceFailureCodes.InvalidRequest, result.ErrorCode);
        Assert.Empty(rig.Rag.Queries);
    }

    // ----- What came back, and what the model is told about it -----

    [Fact]
    public async Task A_search_that_failed_is_lookup_unavailable_because_nothing_was_searched()
    {
        var rig = new Rig();
        rig.Rag.Result = SearchFailed();

        var result = await rig.Box.RunAsync(Ask(Question));

        Assert.False(result.IsSuccess);
        // Not nothing_found: a failed search and a business that has not published an answer are different facts.
        Assert.Equal(KnowledgeFailureCodes.LookupUnavailable, result.ErrorCode);
        Assert.NotEqual(KnowledgeFailureCodes.NothingFound, result.ErrorCode);
        Assert.Single(rig.Rag.Queries);
    }

    [Fact]
    public async Task A_library_that_went_away_mid_call_is_lookup_unavailable_too()
    {
        var rig = new Rig();
        // Disabled between building the toolbox and the caller asking: the client answers null, which is not an empty result.
        rig.Rag.Result = null;

        var result = await rig.Box.RunAsync(Ask(Question));

        Assert.False(result.IsSuccess);
        Assert.Equal(KnowledgeFailureCodes.LookupUnavailable, result.ErrorCode);
    }

    [Fact]
    public async Task A_search_that_found_nothing_is_nothing_found_and_the_model_is_told_to_take_a_message()
    {
        var rig = new Rig();
        rig.Rag.Result = Found();

        var result = await rig.Box.RunAsync(Ask(Question));

        Assert.False(result.IsSuccess);
        Assert.Equal(KnowledgeFailureCodes.NothingFound, result.ErrorCode);
        Assert.Contains("take a message", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Pages_come_back_as_passages_carrying_each_pages_title_and_its_paragraphs()
    {
        var rig = new Rig();
        rig.Rag.Result = Found(
            Page("Pricing", Answer, "Parts are billed separately."),
            Page("   ", "We work across Palm Beach County."));

        var result = await rig.Box.RunAsync(Ask(Question));

        Assert.True(result.IsSuccess);
        using var json = JsonDocument.Parse(result.Value!);
        var passages = json.RootElement.GetProperty("passages").EnumerateArray().ToList();
        Assert.Equal(2, passages.Count);

        Assert.Equal("Pricing", passages[0].GetProperty("Title").GetString());
        Assert.Contains(Answer, passages[0].GetProperty("Text").GetString()!, StringComparison.Ordinal);
        Assert.Contains("Parts are billed separately.", passages[0].GetProperty("Text").GetString()!, StringComparison.Ordinal);

        // A page with no title of its own carries none rather than a blank one.
        Assert.Equal(JsonValueKind.Null, passages[1].GetProperty("Title").ValueKind);
        Assert.Equal("We work across Palm Beach County.", passages[1].GetProperty("Text").GetString());
    }

    [Fact]
    public async Task A_page_with_nothing_readable_on_it_is_dropped_and_a_search_of_only_those_is_nothing_found()
    {
        var mixed = new Rig();
        mixed.Rag.Result = Found(Page("Blank", "", "   ", "\t"), Page("Hours", "We open at eight."));

        var kept = await mixed.Box.RunAsync(Ask(Question));

        Assert.True(kept.IsSuccess);
        using var json = JsonDocument.Parse(kept.Value!);
        var passage = Assert.Single(json.RootElement.GetProperty("passages").EnumerateArray().ToList());
        Assert.Equal("Hours", passage.GetProperty("Title").GetString());

        var allBlank = new Rig();
        allBlank.Rag.Result = Found(Page("Blank", "", "   "), Page("Also blank", "\n"));

        var nothing = await allBlank.Box.RunAsync(Ask(Question));

        Assert.False(nothing.IsSuccess);
        Assert.Equal(KnowledgeFailureCodes.NothingFound, nothing.ErrorCode);
    }

    [Fact]
    public async Task No_more_than_the_first_few_paragraphs_of_a_page_are_read_to_the_model()
    {
        var rig = new Rig();
        var paragraphs = Enumerable.Range(1, KnowledgeToolbox.ParagraphsPerPage + 2)
            .Select(n => $"Paragraph number {n}.")
            .ToArray();
        rig.Rag.Result = Found(Page("Pricing", paragraphs));

        var result = await rig.Box.RunAsync(Ask(Question));

        Assert.True(result.IsSuccess);
        using var json = JsonDocument.Parse(result.Value!);
        var text = json.RootElement.GetProperty("passages")[0].GetProperty("Text").GetString()!;

        foreach (var taken in paragraphs.Take(KnowledgeToolbox.ParagraphsPerPage))
        {
            Assert.Contains(taken, text, StringComparison.Ordinal);
        }

        foreach (var dropped in paragraphs.Skip(KnowledgeToolbox.ParagraphsPerPage))
        {
            Assert.DoesNotContain(dropped, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task The_callers_own_question_is_searched_verbatim_against_this_businesss_own_run()
    {
        var rig = new Rig();
        rig.Rag.Result = Found(Page("Pricing", Answer));

        await rig.Box.RunAsync(Ask(Question));

        var query = Assert.Single(rig.Rag.Queries);
        Assert.Equal(Question, query.Need);
        Assert.Equal(Run, query.RunId);
        Assert.Equal(KnowledgeToolbox.TopK, query.TopK);
    }

    // ----- Zero Data Retention -----

    [Fact]
    public async Task No_log_line_carries_the_callers_question_or_anything_the_library_answered()
    {
        var disabled = new Rig();
        disabled.Rag.IsEnabled = false;
        Assert.Null(disabled.Create());

        var noRun = new Rig();
        Assert.Null(noRun.CreateWith(null));

        var notAllowed = new Rig();
        await notAllowed.Box.RunAsync(new ReceptionistToolCall("c1", "create_event", JsonSerializer.Serialize(new { question = Question })));

        // The question itself, sent as arguments that will not parse: what cannot be read must still not be logged.
        var unparseable = new Rig();
        await unparseable.Box.RunAsync(AskRaw(Question));

        var noQuestion = new Rig();
        await noQuestion.Box.RunAsync(AskRaw($$"""{"asked":"{{Question}}"}"""));

        var failed = new Rig();
        failed.Rag.Result = SearchFailed();
        await failed.Box.RunAsync(Ask(Question));

        var gone = new Rig();
        gone.Rag.Result = null;
        await gone.Box.RunAsync(Ask(Question));

        var empty = new Rig();
        empty.Rag.Result = Found();
        await empty.Box.RunAsync(Ask(Question));

        var answered = new Rig();
        answered.Rag.Result = Found(Page("Pricing", Answer));
        var success = await answered.Box.RunAsync(Ask(Question));
        Assert.True(success.IsSuccess);

        var rigs = new[] { disabled, noRun, notAllowed, unparseable, noQuestion, failed, gone, empty, answered };
        foreach (var rig in rigs)
        {
            foreach (var line in rig.Log.Lines)
            {
                Assert.DoesNotContain(Question, line, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(Answer, line, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("water heater", line, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("two hundred and forty", line, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
