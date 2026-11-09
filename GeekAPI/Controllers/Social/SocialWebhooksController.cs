using GeekAPI.Services.Social.Orchestration;
using GeekApplication.Interfaces.Social;
using GeekApplication.Models.Social;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.Social;

/// <summary>
/// Where the platforms deliver. These routes are public by necessity (Meta and LinkedIn cannot send
/// our API key), so ApiKeyMiddleware lets exactly these paths through and the platform's own
/// signature is what authenticates a delivery: a body that fails its check stores nothing. The
/// handshake answers from the reader's in-memory settings and touches neither the store nor the
/// network. A delivery is answered 200 only after every event in it is stored; any other answer
/// makes the platform redeliver, and the store keeps one row however many times it does.
/// </summary>
[ApiController]
[Route("api/social/webhooks")]
public sealed class SocialWebhooksController : ControllerBase
{
    /// <summary>Largest delivery accepted. Meta batches up to 1000 updates, far below this.</summary>
    internal const int MaxBodyBytes = 2 * 1024 * 1024;

    private readonly IServiceProvider _services;
    private readonly ISocialStore _store;
    private readonly SocialWake _wake;
    private readonly ILogger<SocialWebhooksController> _logger;

    public SocialWebhooksController(
        IServiceProvider services, ISocialStore store, SocialWake wake, ILogger<SocialWebhooksController> logger)
    {
        _services = services;
        _store = store;
        _wake = wake;
        _logger = logger;
    }

    /// <summary>Meta's handshake: it asks once, when the callback is saved in the dashboard, and answers 200 with hub.challenge.</summary>
    [HttpGet("meta")]
    public IActionResult MetaHandshake()
    {
        var reader = ReaderFor(SocialPlatforms.Facebook);
        if (reader is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "No Meta reader is registered.", code = SocialFailureCodes.NotConfigured });

        var query = Request.Query.ToDictionary(pair => pair.Key, pair => pair.Value.ToString(), StringComparer.Ordinal);
        var answer = reader.AnswerHandshake(new SocialHandshakeRequest { Query = query });
        if (!answer.IsSuccess)
        {
            _logger.LogWarning("Meta handshake refused: {Code}.", answer.ErrorCode);
            return Failure(answer.Error!, answer.ErrorCode);
        }

        return Content(answer.Value!.Body, answer.Value.ContentType);
    }

    /// <summary>A Meta delivery: Page feed changes today. Signature first, then each event stored once, then the orchestrator woken.</summary>
    [HttpPost("meta")]
    public async Task<IActionResult> MetaDelivery(CancellationToken ct)
    {
        var reader = ReaderFor(SocialPlatforms.Facebook);
        if (reader is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "No Meta reader is registered.", code = SocialFailureCodes.NotConfigured });

        var body = await ReadBodyAsync(ct).ConfigureAwait(false);
        if (body is null)
            return StatusCode(StatusCodes.Status413PayloadTooLarge, new { error = "The delivery is larger than this endpoint accepts.", code = SocialFailureCodes.InvalidRequest });

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in Request.Headers)
            headers[header.Key] = header.Value.ToString();

        var parsed = reader.ParseWebhook(new SocialWebhookRequest { RawBody = body, Headers = headers });
        if (!parsed.IsSuccess)
        {
            // A bad signature is a stranger or a wrong secret; the rest are ours to fix.
            if (parsed.ErrorCode == SocialFailureCodes.InvalidSignature)
                _logger.LogWarning("Meta delivery refused: {Code}.", parsed.ErrorCode);
            else
                _logger.LogError("Meta delivery could not be read: {Code}. {Error}", parsed.ErrorCode, parsed.Error);
            return Failure(parsed.Error!, parsed.ErrorCode);
        }

        var stored = 0;
        var repeated = 0;
        foreach (var engagementEvent in parsed.Value!)
        {
            engagementEvent.Id = Guid.NewGuid();
            var recorded = await _store.RecordEventAsync(engagementEvent, ct).ConfigureAwait(false);
            if (!recorded.IsSuccess)
            {
                // Not stored, so not acknowledged: the platform redelivers and the events already stored dedupe.
                _logger.LogError("Meta delivery could not be stored: {Code}. {Error}", recorded.ErrorCode, recorded.Error);
                return Failure(recorded.Error!, recorded.ErrorCode);
            }

            if (recorded.Value)
            {
                stored++;
                _wake.Wake(engagementEvent.Id);
            }
            else
            {
                repeated++;
            }
        }

        return Ok(new { received = stored + repeated, stored, repeated });
    }

    private ISocialEngagementReader? ReaderFor(string platform) =>
        _services.GetKeyedService<ISocialEngagementReader>(platform);

    /// <summary>The body exactly as received, or null when it is larger than <see cref="MaxBodyBytes"/>.</summary>
    private async Task<byte[]?> ReadBodyAsync(CancellationToken ct)
    {
        if (Request.ContentLength is > MaxBodyBytes)
            return null;

        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await Request.Body.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes)
                return null;
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private ObjectResult Failure(string error, string? code) =>
        StatusCode(StatusFor(code), new { error, code });

    /// <summary>The HTTP status a failure code is answered with. Anything unlisted is the store or the settings being unavailable, so the platform retries.</summary>
    internal static int StatusFor(string? code) => code switch
    {
        SocialFailureCodes.InvalidSignature => StatusCodes.Status401Unauthorized,
        SocialFailureCodes.Forbidden => StatusCodes.Status403Forbidden,
        SocialFailureCodes.InvalidRequest => StatusCodes.Status400BadRequest,
        SocialFailureCodes.NotSupported => StatusCodes.Status422UnprocessableEntity,
        SocialFailureCodes.NotFound => StatusCodes.Status404NotFound,
        SocialFailureCodes.Conflict => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status503ServiceUnavailable,
    };
}
