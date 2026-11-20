using System.Text.Json;
using GeekAPI.Services.GeekCrawler;
using GeekAPI.Services.Voice.Mcp;
using GeekApplication.Interfaces.Receptionist;
using GeekApplication.Models.ContentCreator;
using GeekApplication.Models.Voice;
using GeekApplication.Results;

namespace GeekAPI.Services.Receptionist.Knowledge;

/// <summary>
/// The door between the call and <see cref="KnowledgeOrchestrator"/>: the one tool a receptionist may use to answer a caller's question
/// about the business itself, instead of taking a message for everything that is not a booking.
///
/// <para>
/// It owns none of the answering. Working out what was asked, searching, judging what came back, writing the answer and checking it
/// against the business's own words are five jobs with five owners, coordinated by the orchestrator. This decides whether the tool
/// exists for this business, hands the call over, and turns the outcome into what the receptionist is told.
/// </para>
///
/// <para>
/// <b>It takes no arguments.</b> The orchestrator reads the call's own turns, so what gets searched is what the caller said rather than
/// the call model's paraphrase of it - which was a quiet place for the question to drift before anyone could see it.
/// </para>
///
/// <para>
/// <b>What comes back has been checked.</b> A success carries an answer that a separate grounding call confirmed the passages support,
/// never the passages themselves: handing those over would put the call's own model between the evidence and the caller with nothing in
/// the way. Every refusal tells the receptionist to take a message, which is what it would have done without the tool.
/// </para>
///
/// <para>Zero Data Retention is unaffected: nothing here is stored, and no log line carries the question or the answer.</para>
/// </summary>
public sealed class KnowledgeToolbox : IReceptionistToolbox
{
    public const string LookupTool = "look_up_business_info";


    private readonly Guid _tenantId;
    private readonly Guid _runId;
    private readonly IGeekCrawlerRagClient _rag;
    private readonly KnowledgeOrchestrator _orchestrator;
    private readonly Func<IReadOnlyList<ReceptionistTurn>> _turns;
    private readonly ILogger _logger;

    private KnowledgeToolbox(
        Guid tenantId, Guid runId, IGeekCrawlerRagClient rag, KnowledgeOrchestrator orchestrator,
        Func<IReadOnlyList<ReceptionistTurn>> turns, ILogger logger)
    {
        _tenantId = tenantId;
        _runId = runId;
        _rag = rag;
        _orchestrator = orchestrator;
        _turns = turns;
        _logger = logger;
    }

    public IReadOnlyList<ReceptionistToolDefinition> Tools { get; } = [Definition()];

    /// <summary>
    /// A knowledge toolbox for this tenant, or null when the receptionist should simply not have one: the business has no indexed site, or
    /// the Library is not configured. Null is not a failure of the call — the receptionist answers the phone and takes messages, as it did
    /// before this tool existed.
    /// </summary>
    public static KnowledgeToolbox? Create(
        Guid tenantId, Guid? knowledgeRunId, IGeekCrawlerRagClient rag, KnowledgeOrchestrator orchestrator,
        Func<IReadOnlyList<ReceptionistTurn>> turns, ILogger logger)
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

        return new KnowledgeToolbox(tenantId, runId, rag, orchestrator, turns, logger);
    }

    private static ReceptionistToolDefinition Definition()
    {
        // The question is the caller's own, in their own words: it is what gets embedded, so narrowing it to keywords loses the meaning
        // half of the search the Library does.
        using var schema = JsonDocument.Parse("""{"type": "object", "properties": {}}""");

        return new ReceptionistToolDefinition(
            LookupTool,
            "Look up what the caller just asked about this business - what it offers, what it charges, where it works, its policies or "
            + "its hours - in the business's own published information. Use it before answering any question about the business. It reads "
            + "the call itself, so it takes no arguments. It answers with a checked answer to say, or tells you to take a message.",
            schema.RootElement.Clone());
    }

    public async Task<Result<string>> RunAsync(ReceptionistToolCall call, CancellationToken ct = default)
    {
        if (!string.Equals(call.Name, LookupTool, StringComparison.Ordinal))
        {
            _logger.LogError("Tenant {TenantId}: the receptionist asked for a tool it was not offered.", _tenantId);
            return Result<string>.Failure("That tool is not offered.", McpFailureCodes.ToolNotAllowed);
        }

        var outcome = await _orchestrator.AnswerAsync(_turns(), _runId, _rag, ct).ConfigureAwait(false);
        if (outcome.Answered) return Result<string>.Success(JsonSerializer.Serialize(new { answer = outcome.Answer }));

        // Every refusal means the same thing to the receptionist, and the code says which it was. The answer is not handed over as
        // passages for the call's own model to retell: an answer that was checked and a passage that was not are different things, and
        // only one of them has been through the grounding check.
        return Result<string>.Failure(Refusal(outcome.Refusal!), outcome.Refusal!);
    }

    /// <summary>What the receptionist is told, which is always some form of "you do not have this, so take a message".</summary>
    private static string Refusal(string code) => code switch
    {
        AnswerRefusals.NothingAsked => "There was no question about the business to look up.",
        AnswerRefusals.NotPublished => "The business's information does not cover that. Take a message instead of answering.",
        AnswerRefusals.Unsupported => "The business's information does not support an answer to that. Take a message instead of answering.",
        AnswerRefusals.CouldNotCheck => "The business's information could not be checked. Take a message instead of answering.",

        // A refusal nobody has written a sentence for. Reporting it as one of the four above would tell the receptionist something that
        // was never established, which is the whole failure this path exists to avoid.
        _ => throw new InvalidOperationException($"There is no sentence for the refusal '{code}'."),
    };

}

