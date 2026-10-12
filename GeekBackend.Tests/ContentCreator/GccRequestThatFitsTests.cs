using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services;
using GeekApplication.Models.ContentCreator;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// The one limit on what a request carries: what its model accepts in one request. A request is
/// counted before it is sent; within the limit it carries everything, over it the partner research is
/// chosen from, and a model whose limit nobody wrote down is sent nothing.
/// </summary>
/// <remarks>
/// Jeff, 2026-10-12: "STOP NOT USING DATA, EVEN IF THAT MEANS IT COSTS MORE". Until that day research
/// was cut by numbers of the code's own (350,000 characters, 40,000 for each partner) and no code knew
/// what a model accepts, which is how a pillar and a blog were sent at 141,637 tokens to a model that
/// takes 128,000 on 2026-10-10. The same behaviour through the real writers is in
/// <c>BriefFieldReachTests</c>.
/// </remarks>
public class GccRequestThatFitsTests(ITestOutputHelper output)
{
    // ---- what each model accepts --------------------------------------------------------------------

    [Theory]
    [InlineData("gpt-4.1", 1_047_576)]
    [InlineData("gpt-4.1-mini", 1_047_576)]
    [InlineData("gpt-4.1-2025-04-14", 1_047_576)]
    [InlineData("  GPT-4.1 ", 1_047_576)]
    [InlineData("gpt-4o", 128_000)]
    [InlineData("gpt-4o-2024-08-06", 128_000)]
    [InlineData("gpt-4o-mini-2024-07-18", 128_000)]
    public void A_listed_model_and_the_models_named_after_it_accept_what_the_family_does(string model, int accepts)
    {
        Assert.Equal(accepts, OpenAiModelLimits.RequestTokensFor(model));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("gpt-5")]
    [InlineData("o3")]
    [InlineData("claude-opus-5")]
    // A name that only begins with a listed model's letters is another model.
    [InlineData("gpt-4.10")]
    [InlineData("gpt-4omni")]
    public void A_model_that_is_not_listed_has_no_limit_and_its_refusal_names_it(string? model)
    {
        Assert.Null(OpenAiModelLimits.RequestTokensFor(model));

        var refusal = OpenAiModelLimits.Refusal(model);

        Assert.StartsWith("Refused:", refusal, StringComparison.Ordinal);
        Assert.Contains($"'{(string.IsNullOrWhiteSpace(model) ? "(none)" : model)}'", refusal, StringComparison.Ordinal);
        // What to do about it, and the operator's first question on a refusal: whether it was billed.
        Assert.Contains("OpenAiModelLimits", refusal, StringComparison.Ordinal);
        Assert.Contains("gpt-4.1, gpt-4o", refusal, StringComparison.Ordinal);
        Assert.Contains("no request was billed", refusal, StringComparison.Ordinal);
    }

    // ---- the count ----------------------------------------------------------------------------------

    /// <summary>
    /// A request of the 08:17 UTC run of 2026-10-07 (the image prompts for a tool page), as its run log
    /// holds it. OpenAI reported it as 250 tokens.
    /// </summary>
    private const string RealSystem =
        "You write AI image-generation prompts for B2B article figures.\n"
        + "CRITICAL: Return EXACTLY ONE prompt for EACH listed section, in the exact order listed.\n"
        + "\n"
        + "VISUAL STYLE:\n"
        + "- Flat vector / infographic, professional B2B tech aesthetic.\n"
        + "- Default size: 1200x630. Style: professional illustration.\n"
        + "- NO readable text, logos, or watermarks in the image.\n"
        + "- Hero image for 'Ramp': establishing-shot composition, evokes the title's theme.\n"
        + "- Section images: teaching diagrams appropriate to the section topic.\n"
        + "\n"
        + "Respond with ONLY a single valid JSON object:\n"
        + "{\"prompts\": [{\"section\": string, \"prompt\": string}]}\n";

    private const string RealUser =
        "Article title: Ramp\n"
        + "Content type: tool\n"
        + "\n"
        + "Sections requiring image prompts:\n"
        + "- Hero: Ramp\n"
        + "- Section 1: The Costs of Manual Automated Approval Workflows and How Ramp Solves Them\n"
        + "- Section 2: How Ramp Transforms Automated Approval Workflows\n"
        + "- Section 3: Ramp's Core Architecture and Functionality\n"
        + "- Section 4: Implementing Ramp in Your Business Environment\n"
        + "- Section 5: Evaluating Ramp for Automated Approval Workflows\n"
        + "- Section 6: Is Ramp the Right Fit for Your Business?\n";

    /// <summary>
    /// The count is OpenAI's own for a request it was sent. All 40 requests of that run were counted
    /// this way when the counter was written: the 16 with no reply format, from 245 to 46,078 tokens,
    /// came to OpenAI's figure exactly. This is one of them, kept as the standing check.
    /// </summary>
    [Fact]
    public void A_request_counts_to_what_openai_reported_for_it()
    {
        var request = new ChatCompletionRequest(
        [
            new ChatMessage(ChatRole.System, RealSystem),
            new ChatMessage(ChatRole.User, RealUser),
        ]);

        Assert.Equal(250, GccRequestSize.TokensOf(request));
    }

    [Fact]
    public void The_count_is_the_messages_their_roles_and_the_start_of_the_reply()
    {
        Assert.Equal(0, GccRequestSize.TokensOf((string?)null));
        Assert.Equal(0, GccRequestSize.TokensOf(string.Empty));
        Assert.Equal(2, GccRequestSize.TokensOf("hello world"));

        // Three for the message, one for its role, two for its text, three for the start of the reply.
        Assert.Equal(9, GccRequestSize.TokensOf(new ChatCompletionRequest([new ChatMessage(ChatRole.User, "hello world")])));
        // A second message adds its own four and its text.
        Assert.Equal(
            15,
            GccRequestSize.TokensOf(new ChatCompletionRequest(
            [
                new ChatMessage(ChatRole.System, "hello world"),
                new ChatMessage(ChatRole.User, "hello world"),
            ])));
        // The room reserved for the reply is not part of what is sent.
        Assert.Equal(
            9,
            GccRequestSize.TokensOf(new ChatCompletionRequest([new ChatMessage(ChatRole.User, "hello world")], MaxOutputTokens: 16_384)));
    }

    /// <summary>
    /// A reply format is counted as its own text and its name. OpenAI counts it its own way and does not
    /// say how: on that run it added 383 for "sections" and 368 for "section" as they were that day. So
    /// this part of a count is near and not exact, and the test holds it to being counted and to being of
    /// that order, not to a figure.
    /// </summary>
    [Fact]
    public void A_reply_format_is_counted_with_the_request_it_is_sent_with()
    {
        var plain = new ChatCompletionRequest([new ChatMessage(ChatRole.User, "hello world")]);
        var withFormat = plain with { JsonSchemaName = "sections", JsonSchema = ContentSectionJsonSchema.SectionsArraySchema };

        var added = GccRequestSize.TokensOf(withFormat) - GccRequestSize.TokensOf(plain);

        Assert.Equal(
            GccRequestSize.TokensOf(ContentSectionJsonSchema.SectionsArraySchema) + GccRequestSize.TokensOf("sections"), added);
        output.WriteLine($"The \"sections\" reply format is counted as {added} tokens.");
        Assert.InRange(added, 100, 1_500);
    }

    /// <summary>
    /// Counting is not what a run waits for. A pillar's or a blog's body request after this plan is
    /// about 370,000 characters of mostly prose; it is counted here ten times and the time written out.
    /// </summary>
    [Fact]
    public void Counting_a_request_of_370_000_characters_takes_a_small_part_of_a_second()
    {
        var sentence = "The approval chain is set once and each invoice is routed by amount, by vendor and by department. ";
        var text = string.Concat(Enumerable.Repeat(sentence, 370_000 / sentence.Length + 1));
        var request = new ChatCompletionRequest(
            [new ChatMessage(ChatRole.System, RealSystem), new ChatMessage(ChatRole.User, text)],
            JsonSchemaName: "sections",
            JsonSchema: ContentSectionJsonSchema.SectionsArraySchema);
        Assert.InRange(text.Length, 370_000, 371_000);
        // Once before timing, so the encoding's own load is not what is timed.
        var tokens = GccRequestSize.TokensOf(request);

        var clock = Stopwatch.StartNew();
        for (var i = 0; i < 10; i++) Assert.Equal(tokens, GccRequestSize.TokensOf(request));
        clock.Stop();

        output.WriteLine($"{text.Length:N0} characters, {tokens:N0} tokens: {clock.ElapsedMilliseconds / 10.0:N1} ms a count.");
        // A tenth of a second is where the plan said a page's research would be counted once and reused.
        Assert.InRange(clock.ElapsedMilliseconds / 10.0, 0, 100);
    }

    // ---- a request that fits, and one that does not -------------------------------------------------

    private const string Marker = "<research>";

    /// <summary>About 1,500 tokens and 4,000 characters, and the same for every passage.</summary>
    private static readonly string Filler = string.Join(' ', Enumerable.Range(0, 480).Select(n => $"item{(n * 7919) % 10007}"));

    /// <summary><paramref name="partners"/> partners with <paramref name="each"/> pages of one passage each.</summary>
    private static IReadOnlyList<GccQuoteablePage> Pages(int partners, int each) =>
    [
        .. Enumerable.Range(0, partners).SelectMany(partner => Enumerable.Range(0, each).Select(i => new GccQuoteablePage(
            $"https://partner{partner}.test/{i}", "Title", [], [$"Passage {i} of partner {partner}. {Filler}"],
            RetrievalMode: GccQuoteablePage.RetrievalModeRagChunk))),
    ];

    private static GccCreateDto CreateWith(IReadOnlyList<GccQuoteablePage> pages) => new(
        Id: Guid.NewGuid(), ClientId: Guid.NewGuid(), OwnerUserId: Guid.NewGuid(),
        StartingContentType: "pillar", Topic: "Topic", Notes: null, ProjectSiteRunId: null, SiteSectionJson: null,
        BriefJson: null, ResearchJson: GccResearchFetchService.Serialize(new GccResearchDocument(null, pages)),
        Status: "draft", CreatedAtUtc: DateTime.UtcNow, UpdatedAtUtc: DateTime.UtcNow);

    /// <summary>A request as a writer builds it: fixed instructions, then the research block, whole or chosen.</summary>
    private static Func<IReadOnlySet<(string Url, int Position)>?, ChatCompletionRequest> Builder(
        GccCreateDto create, int reply = 8_192, string fixedText = "Write two sections.") =>
        carried => new ChatCompletionRequest(
            [
                new ChatMessage(ChatRole.System, "You write one page."),
                new ChatMessage(ChatRole.User, $"{fixedText}\n{Marker}\n{GccGenerateService.BuildResearchBlock(create, carried)}"),
            ],
            MaxOutputTokens: reply);

    private sealed class Model(string name) : IContentGenerationProvider
    {
        public int Sent { get; private set; }

        public LlmProviderType ProviderType => LlmProviderType.OpenAi;

        public string ModelFor(ChatCompletionRequest request) => name;

        public Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
        {
            Sent++;
            return Task.FromResult(new ChatCompletionResult("{}", name, 10, 5));
        }
    }

    private static List<GccGenerateJobEventWrite> Recording()
    {
        var recorded = new List<GccGenerateJobEventWrite>();
        GccRunLog.Begin(
            Guid.NewGuid(),
            (events, _) =>
            {
                lock (recorded) recorded.AddRange(events);
                return Task.CompletedTask;
            },
            NullLogger.Instance);
        return recorded;
    }

    private static JsonElement Evidence(List<GccGenerateJobEventWrite> recorded) =>
        JsonDocument.Parse(Assert.Single(recorded, e => e.Kind == "evidence").PayloadJson).RootElement;

    [Fact]
    public async Task A_request_within_what_its_model_accepts_carries_everything_and_its_size_is_on_the_record_before_it_is_sent()
    {
        // 150 passages, about 600,000 characters: far over the 350,000 at which research was cut until
        // 2026-10-12, and about a fifth of what this model accepts.
        var pages = Pages(partners: 5, each: 30);
        var create = CreateWith(pages);
        var build = Builder(create);
        var recorded = Recording();
        var model = new Model("gpt-4.1");

        var request = await GccGenerateService.RequestThatFitsAsync(
            model, new GccResearchPerRequest(pages, null), [GccResearchLabel.Problem], "Pillar body sections 1-2", 1, 5, build);

        var sent = request.Messages[1].Content;
        Assert.Equal(build(null).Messages[1].Content, sent);
        Assert.All(pages, page => Assert.Contains(page.Paragraphs[0], sent, StringComparison.Ordinal));
        Assert.DoesNotContain(GccGenerateService.ResearchIsAPartNote, sent, StringComparison.Ordinal);

        var record = Evidence(recorded);
        Assert.True(record.GetProperty("whole").GetBoolean());
        Assert.Equal(JsonValueKind.Null, record.GetProperty("leftOut").ValueKind);
        var size = record.GetProperty("request");
        Assert.Equal("gpt-4.1", size.GetProperty("model").GetString());
        Assert.Equal(1_047_576, size.GetProperty("accepts").GetInt32());
        Assert.Equal(8_192, size.GetProperty("reply").GetInt32());
        Assert.Equal(GccRequestSize.TokensOf(request), size.GetProperty("tokens").GetInt32());
        Assert.Equal(size.GetProperty("tokens").GetInt32(), size.GetProperty("withEverything").GetInt32());
        Assert.InRange(size.GetProperty("withReply").GetInt32(), 128_001, 1_047_576);
        // Counted and recorded; sending is the caller's, and has not happened.
        Assert.Equal(0, model.Sent);
    }

    [Fact]
    public async Task A_request_over_what_its_model_accepts_is_chosen_for_never_passes_the_limit_and_lists_what_was_left_out()
    {
        var pages = Pages(partners: 5, each: 30);
        var create = CreateWith(pages);
        var build = Builder(create);
        var research = new GccResearchPerRequest(pages, null);
        var recorded = Recording();

        var request = await GccGenerateService.RequestThatFitsAsync(
            new Model("gpt-4o"), research, [GccResearchLabel.Problem], "Pillar body sections 1-2", 1, 5, build);

        // With everything it is over; as sent, with the room reserved for its reply, it is not.
        Assert.InRange(GccRequestSize.TokensOf(build(null)) + 8_192, 128_001, int.MaxValue);
        Assert.InRange(GccRequestSize.TokensOf(request) + request.MaxOutputTokens, 1, 128_000);

        var record = Evidence(recorded);
        Assert.False(record.GetProperty("whole").GetBoolean());
        var size = record.GetProperty("request");
        Assert.Equal(128_000, size.GetProperty("accepts").GetInt32());
        Assert.Equal(GccRequestSize.TokensOf(request), size.GetProperty("tokens").GetInt32());
        Assert.Equal(GccRequestSize.TokensOf(build(null)), size.GetProperty("withEverything").GetInt32());

        // Every passage is either in the request or named on the record as left out of it.
        var sent = request.Messages[1].Content;
        var chosen = record.GetProperty("chosen").EnumerateArray().Select(c => c.GetProperty("url").GetString()!).ToList();
        var leftOut = record.GetProperty("leftOut").EnumerateArray().Select(l => l.GetProperty("url").GetString()!).ToList();
        Assert.NotEmpty(leftOut);
        Assert.Equal(pages.Select(page => page.Url).OrderBy(url => url), chosen.Concat(leftOut).OrderBy(url => url));
        Assert.All(chosen, url => Assert.Contains($"({url})", sent, StringComparison.Ordinal));
        Assert.All(leftOut, url => Assert.DoesNotContain($"({url})", sent, StringComparison.Ordinal));
        Assert.Contains(GccGenerateService.ResearchIsAPartNote, sent, StringComparison.Ordinal);

        // The room is used, not a number of the code's own: well over the 40,000 characters a partner
        // was held to before, and as much for one partner as for another.
        var partners = record.GetProperty("partners").EnumerateArray().ToList();
        Assert.Equal(5, partners.Count);
        Assert.All(partners, partner => Assert.InRange(partner.GetProperty("carried").GetProperty("chars").GetInt32(), 40_001, int.MaxValue));
        Assert.Single(partners.Select(partner => partner.GetProperty("carried").GetProperty("passages").GetInt32()).Distinct());
        // Most of what the model has room for is filled.
        Assert.InRange(size.GetProperty("withReply").GetInt32(), 110_000, 128_000);
    }

    [Fact]
    public async Task What_a_request_was_sent_counts_for_the_next_and_what_was_only_tried_does_not()
    {
        var pages = Pages(partners: 5, each: 30);
        var create = CreateWith(pages);
        var research = new GccResearchPerRequest(pages, null);
        var recorded = Recording();
        var model = new Model("gpt-4o");
        IReadOnlyList<string> Chosen(int nth) =>
        [
            .. JsonDocument.Parse(recorded.Where(e => e.Kind == "evidence").ElementAt(nth).PayloadJson).RootElement
                .GetProperty("chosen").EnumerateArray().Select(c => c.GetProperty("url").GetString()!),
        ];

        await GccGenerateService.RequestThatFitsAsync(model, research, [], "Tool page 'X' opening", 0, 5, Builder(create));
        await GccGenerateService.RequestThatFitsAsync(model, research, [], "Tool page 'X' sections 1-2", 1, 5, Builder(create));

        // The second request reaches passages the first did not carry.
        Assert.NotEmpty(Chosen(1).Except(Chosen(0)));
        // Each request is recorded once, however many choices were tried before one fitted.
        Assert.Equal(2, recorded.Count(e => e.Kind == "evidence"));
    }

    [Fact]
    public async Task A_request_over_the_limit_with_none_of_the_research_in_it_is_refused_and_says_its_size()
    {
        var pages = Pages(partners: 1, each: 3);
        var create = CreateWith(pages);
        // The fixed part alone is more than the model accepts: nothing that can be chosen from would help.
        var tooMuch = string.Concat(Enumerable.Repeat(Filler + " ", 100));
        var recorded = Recording();

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => GccGenerateService.RequestThatFitsAsync(
            new Model("gpt-4o"), new GccResearchPerRequest(pages, null), [], "Blog body sections 1-2", 1, 3,
            Builder(create, fixedText: tooMuch)));

        Assert.StartsWith("Refused: Blog body sections 1-2 is ", refused.Message, StringComparison.Ordinal);
        Assert.Contains("gpt-4o accepts 128,000", refused.Message, StringComparison.Ordinal);
        Assert.Contains("nothing was billed", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(recorded, e => e.Kind == "evidence");
    }

    [Fact]
    public async Task A_request_for_a_model_that_is_not_listed_is_refused_before_it_is_counted_or_recorded()
    {
        var pages = Pages(partners: 1, each: 3);
        var recorded = Recording();
        var model = new Model("gpt-5");

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => GccGenerateService.RequestThatFitsAsync(
            model, new GccResearchPerRequest(pages, null), [], "Pillar opening", 0, 5, Builder(CreateWith(pages))));

        Assert.Equal(OpenAiModelLimits.Refusal("gpt-5"), refused.Message);
        Assert.Empty(recorded);
        Assert.Equal(0, model.Sent);
    }

    // ---- where the request is sent ------------------------------------------------------------------

    private sealed class OpenAi(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(respond(request));
        }
    }

    private static OpenAiProvider Provider(HttpMessageHandler handler, string model, string? utility = null) =>
        new(
            new HttpClient(handler),
            Options.Create(new LlmProvidersOptions
            {
                OpenAi =
                {
                    ApiKey = "k", Model = model, UtilityModel = utility ?? string.Empty,
                    BaseUrl = "http://localhost/v1/chat/completions",
                },
            }),
            NullLogger<OpenAiProvider>.Instance);

    private static HttpResponseMessage Answer(string model, params (string Name, string Value)[] headers)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"model\":\"" + model + "\",\"choices\":[{\"message\":{\"content\":\"written\"},\"finish_reason\":\"stop\"}],"
                + "\"usage\":{\"prompt_tokens\":9,\"completion_tokens\":1}}",
                Encoding.UTF8, "application/json"),
        };
        foreach (var (name, value) in headers) response.Headers.TryAddWithoutValidation(name, value);
        return response;
    }

    private static ChatCompletionRequest Hello(LlmTaskClass taskClass = LlmTaskClass.Writing) =>
        new([new ChatMessage(ChatRole.User, "hello world")], TaskClass: taskClass);

    [Fact]
    public async Task OpenAi_is_sent_nothing_for_a_model_that_is_not_listed()
    {
        var openAi = new OpenAi(_ => Answer("gpt-5"));

        var refused = await Assert.ThrowsAsync<ContentGenerationException>(() => Provider(openAi, "gpt-5").CompleteAsync(Hello()));

        Assert.Equal(OpenAiModelLimits.Refusal("gpt-5"), refused.Message);
        Assert.Equal(0, openAi.Requests);

        // The model for one kind of work is checked as the one for writing is: a listed writing model
        // does not let an unlisted utility model through.
        var mixed = new OpenAi(_ => Answer("gpt-4.1"));
        var provider = Provider(mixed, "gpt-4.1", utility: "gpt-5-mini");
        await provider.CompleteAsync(Hello());
        await Assert.ThrowsAsync<ContentGenerationException>(() => provider.CompleteAsync(Hello(LlmTaskClass.Utility)));
        Assert.Equal(1, mixed.Requests);
    }

    [Fact]
    public void The_model_a_request_is_counted_against_is_the_one_it_is_sent_to()
    {
        var provider = Provider(new OpenAi(_ => Answer("gpt-4.1")), "gpt-4.1", utility: "gpt-4o-mini");

        Assert.Equal("gpt-4.1", provider.ModelFor(Hello()));
        Assert.Equal("gpt-4o-mini", provider.ModelFor(Hello(LlmTaskClass.Utility)));
        // No extraction model is named, so that work goes to the writing model.
        Assert.Equal("gpt-4.1", provider.ModelFor(Hello(LlmTaskClass.Extraction)));
        Assert.Equal("gpt-4o", provider.ModelFor(Hello() with { Model = "gpt-4o" }));
    }

    [Fact]
    public async Task The_allowance_openai_states_on_a_reply_is_on_the_result()
    {
        var stated = await Provider(
            new OpenAi(_ => Answer(
                "gpt-4.1-2025-04-14",
                ("x-ratelimit-limit-tokens", "2000000"),
                ("x-ratelimit-remaining-tokens", "1847000"),
                ("x-ratelimit-reset-tokens", "4.59s"))),
            "gpt-4.1").CompleteAsync(Hello());

        Assert.Equal(new LlmRateLimit(2_000_000, 1_847_000, "4.59s"), stated.RateLimit);

        // A reply that states none carries none, and nothing is made up for it.
        var silent = await Provider(new OpenAi(_ => Answer("gpt-4.1")), "gpt-4.1").CompleteAsync(Hello());
        Assert.Null(silent.RateLimit);
    }

    /// <summary>
    /// Every request of a run is counted before it is sent, and its record carries that count beside
    /// what its model accepts, OpenAI's own count of it and the allowance OpenAI stated. So the counter is
    /// checked by every run, request by request, and the allowance is a number read and not worked out.
    /// </summary>
    [Fact]
    public async Task The_runs_record_of_a_request_carries_its_counted_size_what_its_model_accepts_and_the_allowance()
    {
        var recorded = Recording();
        var provider = new GccRecordingProvider(Provider(
            new OpenAi(_ => Answer(
                "gpt-4.1-2025-04-14",
                ("x-ratelimit-limit-tokens", "2000000"),
                ("x-ratelimit-remaining-tokens", "1999991"),
                ("x-ratelimit-reset-tokens", "0s"))),
            "gpt-4.1"));

        await provider.CompleteAsync(Hello());

        var call = JsonDocument.Parse(Assert.Single(recorded, e => e.Kind == "call").PayloadJson).RootElement;
        Assert.Equal(9, call.GetProperty("countedTokens").GetInt32());
        // What this stand-in for OpenAI reported: the two are side by side on every request.
        Assert.Equal(9, call.GetProperty("promptTokens").GetInt32());
        Assert.Equal(1_047_576, call.GetProperty("accepts").GetInt32());
        var allowance = call.GetProperty("rateLimit");
        Assert.Equal(2_000_000, allowance.GetProperty("limitTokens").GetInt64());
        Assert.Equal(1_999_991, allowance.GetProperty("remainingTokens").GetInt64());
        Assert.Equal("0s", allowance.GetProperty("resetTokens").GetString());
    }

    [Fact]
    public async Task A_request_that_fails_still_has_its_counted_size_on_the_record()
    {
        var recorded = Recording();
        var provider = new GccRecordingProvider(Provider(
            new OpenAi(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("{\"error\":{\"message\":\"Rate limit reached\"}}", Encoding.UTF8, "application/json"),
            }),
            "gpt-4.1"));

        var failure = await Assert.ThrowsAsync<ContentGenerationException>(() => provider.CompleteAsync(Hello()));

        // As any failed request: the page it belongs to fails, once, and nothing is sent again.
        Assert.Contains("(429)", failure.Message, StringComparison.Ordinal);
        var call = JsonDocument.Parse(Assert.Single(recorded, e => e.Kind == "call").PayloadJson).RootElement;
        Assert.Equal(9, call.GetProperty("countedTokens").GetInt32());
        Assert.Equal(1_047_576, call.GetProperty("accepts").GetInt32());
        Assert.Contains("Rate limit reached", call.GetProperty("error").GetString(), StringComparison.Ordinal);
    }
}
