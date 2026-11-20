using System.Text.Json;
using GeekAPI.Services.GeekCrawler;
using GeekAPI.Services.Voice.Mcp;
using GeekApplication.Interfaces.Receptionist;
using GeekApplication.Models.ContentCreator;
using GeekApplication.Models.Voice;
using GeekApplication.Results;

namespace GeekAPI.Services.Receptionist.Knowledge;

/// <summary>
/// The one tool that lets a receptionist answer a caller's question about the business itself — what it does, what it charges, where it
/// works, what its policy is — instead of taking a message for everything that is not a booking.
///
/// <para>
/// It is the Content Creator's grounding pattern with a caller in place of a draft (<c>GccGroundingResolver</c>): a natural-language
/// question goes to the Library, the Library embeds it and searches Qdrant, and passages come back. Nothing here embeds anything. The
/// model that runs on the VPS is the only embedder in the estate, and a second implementation of it in another language is exactly the
/// drift this codebase has paid for once already.
/// </para>
///
/// <para>
/// The business's own crawled site is the scope, by its run id. A tenant with no indexed run has no knowledge tool at all rather than a
/// tool that always answers nothing: a receptionist that cannot look anything up should not be told it can.
/// </para>
///
/// <para>
/// <b>It never answers from nothing.</b> A failed query and an empty result are both refusals, and the model is given a code. That is the
/// same rule the content pipeline states as "a refusal is information; a confident ungrounded draft is not" — and it matters more on a
/// phone call, because a caller cannot see a citation and has no way to tell an invented price from a real one. What the receptionist does
/// with a refusal is take a message, which is what it would have done without the tool.
/// </para>
///
/// <para>Zero Data Retention is unaffected: the caller's question is sent to the Library to be matched and is never stored by it or here,
/// and no log line carries the question or the answer.</para>
/// </summary>
public sealed class KnowledgeToolbox : IReceptionistToolbox
{
    public const string LookupTool = "look_up_business_info";

    /// <summary>Passages per question. Enough for an answer, few enough to stay inside a spoken reply's latency.</summary>
    internal const int TopK = 4;

    /// <summary>Paragraphs kept per passage, for the same reason.</summary>
    internal const int ParagraphsPerPage = 3;

    private readonly Guid _tenantId;
    private readonly Guid _runId;
    private readonly IGeekCrawlerRagClient _rag;
    private readonly ILogger _logger;

    private KnowledgeToolbox(Guid tenantId, Guid runId, IGeekCrawlerRagClient rag, ILogger logger)
    {
        _tenantId = tenantId;
        _runId = runId;
        _rag = rag;
        _logger = logger;
    }

    public IReadOnlyList<ReceptionistToolDefinition> Tools { get; } = [Definition()];

    /// <summary>
    /// A knowledge toolbox for this tenant, or null when the receptionist should simply not have one: the business has no indexed site, or
    /// the Library is not configured. Null is not a failure of the call — the receptionist answers the phone and takes messages, as it did
    /// before this tool existed.
    /// </summary>
    public static KnowledgeToolbox? Create(Guid tenantId, Guid? knowledgeRunId, IGeekCrawlerRagClient rag, ILogger logger)
    {
        if (!rag.IsEnabled)
        {
            logger.LogError("Tenant {TenantId}: the evidence library is not configured, so this call has no knowledge tool.", tenantId);
            return null;
        }

        if (knowledgeRunId is not { } runId || runId == Guid.Empty)
        {
            logger.LogInformation("Tenant {TenantId}: no indexed site, so this call has no knowledge tool.", tenantId);
            return null;
        }

        return new KnowledgeToolbox(tenantId, runId, rag, logger);
    }

    private static ReceptionistToolDefinition Definition()
    {
        // The question is the caller's own, in their own words: it is what gets embedded, so narrowing it to keywords loses the meaning
        // half of the search the Library does.
        using var schema = JsonDocument.Parse(
            """
            {
              "type": "object",
              "properties": {
                "question": {
                  "type": "string",
                  "description": "What the caller wants to know about this business, in their own words."
                }
              },
              "required": ["question"]
            }
            """);

        return new ReceptionistToolDefinition(
            LookupTool,
            "Look up something about this business — what it offers, what it charges, where it works, its policies or its hours — from the "
            + "business's own published information. Use it before answering any question about the business. If it finds nothing, say you "
            + "will take a message rather than guessing.",
            schema.RootElement.Clone());
    }

    public async Task<Result<string>> RunAsync(ReceptionistToolCall call, CancellationToken ct = default)
    {
        if (!string.Equals(call.Name, LookupTool, StringComparison.Ordinal))
        {
            _logger.LogError("Tenant {TenantId}: the receptionist asked for the tool {Tool}, which it was not offered.", _tenantId, call.Name);
            return Result<string>.Failure("That tool is not offered.", McpFailureCodes.ToolNotAllowed);
        }

        var question = Question(call.ArgumentsJson);
        if (question is null)
        {
            _logger.LogError("Tenant {TenantId}: the receptionist's lookup arguments carry no question.", _tenantId);
            return Result<string>.Failure("The lookup needs a question.", VoiceFailureCodes.InvalidRequest);
        }

        var result = await _rag.QueryAsync(new GeekCrawlerRagQuery(question, _runId, TopK: TopK), ct).ConfigureAwait(false);

        // Disabled mid-call, or the HTTP call failed. Either way nothing was searched, which is not the same as nothing being found.
        if (result is null || result.Failed)
        {
            _logger.LogError("Tenant {TenantId}: the business information could not be searched.", _tenantId);
            return Result<string>.Failure("The business's information could not be searched.", KnowledgeFailureCodes.LookupUnavailable);
        }

        var passages = Passages(result.Pages);
        if (passages.Count == 0)
        {
            // Searched, and the business has not published an answer. The model is told so plainly, because the alternative is a
            // receptionist inventing a price.
            _logger.LogInformation("Tenant {TenantId}: a caller's question matched nothing in the business's information.", _tenantId);
            return Result<string>.Failure(
                "The business's information does not cover that. Take a message instead of answering.",
                KnowledgeFailureCodes.NothingFound);
        }

        return Result<string>.Success(JsonSerializer.Serialize(new { passages }));
    }

    /// <summary>
    /// What came back, as text a model reads: the page's title and its first paragraphs. Headings, anchors, digests and crawl timestamps
    /// are the content pipeline's provenance needs and are nothing to a caller waiting on the line.
    /// </summary>
    private static List<KnowledgePassage> Passages(IReadOnlyList<GccQuoteablePage> pages)
    {
        var passages = new List<KnowledgePassage>(pages.Count);
        foreach (var page in pages)
        {
            var text = string.Join(
                " ",
                page.Paragraphs.Where(p => !string.IsNullOrWhiteSpace(p)).Take(ParagraphsPerPage).Select(p => p.Trim()));
            if (text.Length == 0) continue;
            passages.Add(new KnowledgePassage(string.IsNullOrWhiteSpace(page.Title) ? null : page.Title, text));
        }
        return passages;
    }

    private static string? Question(string? argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson)) return null;
        try
        {
            using var parsed = JsonDocument.Parse(argumentsJson);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!parsed.RootElement.TryGetProperty("question", out var question)) return null;
            if (question.ValueKind != JsonValueKind.String) return null;
            var text = question.GetString();
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record KnowledgePassage(string? Title, string Text);
}

/// <summary>Why a lookup gave the receptionist no answer. The model is given the code, and both codes mean "do not answer from nothing".</summary>
public static class KnowledgeFailureCodes
{
    /// <summary>Nothing was searched: the Library failed or is unreachable.</summary>
    public const string LookupUnavailable = "lookup_unavailable";

    /// <summary>The business's information was searched and covers nothing of what was asked.</summary>
    public const string NothingFound = "nothing_found";
}
