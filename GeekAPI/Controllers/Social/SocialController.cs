using System.Security.Claims;
using GeekAPI.Auth;
using GeekAPI.Services.Social.Connector;
using GeekAPI.Services.Social.LanguageModel;
using GeekAPI.Services.Social.Meta;
using GeekAPI.Services.Social.Orchestration;
using GeekApplication.Interfaces.Social;
using GeekApplication.Models.Social;
using GeekApplication.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.Social;

/// <summary>
/// The GeekSocial screen's routes (docs/GEEK-SOCIAL-API.md). Every one needs geeksocial.manage. Reads
/// go to the store; every change goes through <see cref="SocialOperatorActions"/>, the orchestrator or
/// the connect flow, never to an agent or a platform directly. A failure is answered with its message
/// and its code; a Reviewer refusal is 422 review_refused with the rules broken. No route returns a token.
/// </summary>
[ApiController]
[Route("api/social")]
[Authorize(Policy = GeekSocialAuthConstants.ManagePolicy)]
public sealed class SocialController : ControllerBase
{
    internal const int DefaultLimit = 50;
    internal const int MaxLimit = 200;
    internal const string ReviewRefused = "review_refused";

    private readonly ISocialStore _store;
    private readonly SocialOperatorActions _actions;
    private readonly SocialOrchestrator _orchestrator;
    private readonly SocialConnectService _connect;
    private readonly SocialAiClearance _clearance;
    private readonly SocialModelSettings _ai;
    private readonly MetaOptions _meta;

    public SocialController(
        ISocialStore store,
        SocialOperatorActions actions,
        SocialOrchestrator orchestrator,
        SocialConnectService connect,
        SocialAiClearance clearance,
        SocialModelSettings ai,
        MetaOptions meta)
    {
        _store = store;
        _actions = actions;
        _orchestrator = orchestrator;
        _connect = connect;
        _clearance = clearance;
        _ai = ai;
        _meta = meta;
    }

    // ----- Status -----

    /// <summary>
    /// The signed-in person's tenant's status, and what GeekSocial is running with. <c>comments</c> and
    /// <c>drafts</c> count only this tenant's rows; <c>failures</c> are the steps that could not complete
    /// for this tenant, and those that belong to no one tenant (<c>forEveryTenant</c>), newest first, kept
    /// 30 days. <c>aiProvider</c> is the provider GeekAPI is set to call, its model and whether its key is
    /// set (null when no provider is set; the model is null when it is not set; the key itself is never
    /// returned); <c>clearedPlatforms</c> are the platforms cleared to send comment text to that provider,
    /// so it is empty when no provider is set. The route reads the settings, not the model client: only
    /// the agents reach the model.
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var (tenantId, refusal) = await TenantAsync(ct);
        if (refusal is not null) return refusal;

        var read = await _store.GetTenantStatusAsync(tenantId, SocialStepFailures.MaxListed, ct);
        if (!read.IsSuccess) return Failure(read.Error!, read.ErrorCode);

        var status = read.Value!;
        return Ok(new
        {
            comments = status.Comments,
            drafts = status.Drafts,
            failures = status.Failures.Select(f => new { f.Step, f.Code, At = f.OccurredAt, ForEveryTenant = f.TenantId is null }),
            aiProvider = AiProviderStatus(),
            clearedPlatforms = _clearance.ClearedFor(_ai.Provider),
            metaConfigured = _meta.AppId is not null && _meta.AppSecret is not null && _meta.VerifyToken is not null && _meta.LoginConfigId is not null,
            sendLimits = new
            {
                repliesPerAccountPerHour = SocialSendLimits.RepliesPerAccountPerHour,
                coolDownMinutes = (int)SocialSendLimits.CoolDown.TotalMinutes,
            },
        });
    }

    private object? AiProviderStatus() => _ai.Provider is null
        ? null
        : new
        {
            id = _ai.Provider,
            name = SocialModelProviders.DisplayName(_ai.Provider),
            model = string.IsNullOrWhiteSpace(_ai.Model) ? null : _ai.Model,
            apiKeySet = !string.IsNullOrWhiteSpace(_ai.ApiKey),
        };

    // ----- Tenant -----

    public sealed class CreateTenantBody
    {
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>The signed-in person's tenant. 404 until they have signed up for GeekSocial.</summary>
    [HttpGet("tenant")]
    public async Task<IActionResult> GetTenant(CancellationToken ct)
    {
        var owner = OwnerId();
        if (owner is null) return Failure("The token's subject is not a user id.", SocialFailureCodes.Forbidden);
        return Answer(await _store.GetTenantByOwnerAsync(owner.Value, ct));
    }

    /// <summary>Signs the person up for GeekSocial. 409 when they already have a tenant.</summary>
    [HttpPost("tenant")]
    public async Task<IActionResult> CreateTenant([FromBody] CreateTenantBody body, CancellationToken ct)
    {
        var owner = OwnerId();
        if (owner is null) return Failure("The token's subject is not a user id.", SocialFailureCodes.Forbidden);
        return Answer(await _store.CreateTenantAsync(new CreateSocialTenantCommand { OwnerUserId = owner.Value, Name = body?.Name ?? string.Empty }, ct));
    }

    /// <summary>The person accepts the AI terms. Until they do, none of their content reaches the AI.</summary>
    [HttpPost("tenant/ai-terms")]
    public async Task<IActionResult> AcceptAiTerms(CancellationToken ct)
    {
        var (tenantId, refusal) = await TenantAsync(ct);
        if (refusal is not null) return refusal;
        return Answer(await _store.AcceptAiTermsAsync(tenantId, DateTime.UtcNow, ct));
    }

    // ----- Accounts -----

    [HttpGet("accounts")]
    public async Task<IActionResult> Accounts(CancellationToken ct)
    {
        var (tenantId, refusal) = await TenantAsync(ct);
        if (refusal is not null) return refusal;
        return Answer(await _store.ListAccountsAsync(tenantId, ct));
    }

    [HttpPut("accounts/{id:guid}/settings")]
    public async Task<IActionResult> UpdateSettings(Guid id, [FromBody] UpdateSocialAccountSettingsCommand body, CancellationToken ct)
    {
        var (tenantId, refusal) = await TenantAsync(ct);
        if (refusal is not null) return refusal;
        return Answer(await _actions.UpdateSettingsAsync(tenantId, id, body, ct));
    }

    [HttpPost("accounts/{id:guid}/resync")]
    public async Task<IActionResult> Resync(Guid id, CancellationToken ct)
    {
        var (tenantId, refusal) = await TenantAsync(ct);
        if (refusal is not null) return refusal;
        return Answer(await _actions.ResyncAsync(tenantId, id, ct));
    }

    // ----- Connecting -----

    public sealed class ConnectStartBody
    {
        public string RedirectUri { get; set; } = string.Empty;
    }

    public sealed class ConnectCompleteBody
    {
        public string Code { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string RedirectUri { get; set; } = string.Empty;
    }

    [HttpPost("connect/meta/start")]
    public async Task<IActionResult> ConnectMetaStart([FromBody] ConnectStartBody body, CancellationToken ct)
    {
        if (!Uri.TryCreate(body.RedirectUri, UriKind.Absolute, out var redirect))
            return Failure("redirectUri must be an absolute address.", SocialFailureCodes.InvalidRequest);

        // A person who has not signed up cannot start connecting an account: it would end at complete with nowhere to put it.
        var (_, refusal) = await TenantAsync(ct);
        if (refusal is not null) return refusal;

        var url = _connect.Start(SocialPlatforms.Facebook, UserId(), redirect);
        return url.IsSuccess ? Ok(new { authorizationUrl = url.Value!.ToString() }) : Failure(url.Error!, url.ErrorCode);
    }

    [HttpPost("connect/meta/complete")]
    public async Task<IActionResult> ConnectMetaComplete([FromBody] ConnectCompleteBody body, CancellationToken ct)
    {
        if (!Uri.TryCreate(body.RedirectUri, UriKind.Absolute, out var redirect))
            return Failure("redirectUri must be an absolute address.", SocialFailureCodes.InvalidRequest);
        if (string.IsNullOrWhiteSpace(body.Code) || string.IsNullOrWhiteSpace(body.State))
            return Failure("code and state are required.", SocialFailureCodes.InvalidRequest);

        var (tenantId, refusal) = await TenantAsync(ct);
        if (refusal is not null) return refusal;

        var webhook = new Uri($"{Request.Scheme}://{Request.Host}/api/social/webhooks/meta");
        return Answer(await _connect.CompleteAsync(tenantId, SocialPlatforms.Facebook, UserId(), body.Code, body.State, redirect, webhook, ct));
    }

    // ----- Drafts -----

    public sealed class EditDraftBody
    {
        public string DraftText { get; set; } = string.Empty;
    }

    [HttpGet("drafts")]
    public async Task<IActionResult> Drafts([FromQuery] string? status, [FromQuery] Guid? accountId, [FromQuery] int? limit, CancellationToken ct)
    {
        if (status is not null && !ReplyDraftStatuses.IsKnown(status))
            return Failure($"Unknown status '{status}'.", SocialFailureCodes.InvalidRequest);

        var (tenantId, refusal) = await TenantAsync(ct);
        if (refusal is not null) return refusal;
        var drafts = await _store.ListDraftsAsync(tenantId, status, accountId, Limit(limit), ct);
        if (!drafts.IsSuccess)
            return Failure(drafts.Error!, drafts.ErrorCode);

        var views = await Task.WhenAll(drafts.Value!.Select(d => ViewAsync(d, ct)));
        var failed = views.FirstOrDefault(v => !v.IsSuccess);
        return failed is not null ? Failure(failed.Error!, failed.ErrorCode) : Ok(views.Select(v => v.Value));
    }

    [HttpGet("drafts/{id:guid}")]
    public async Task<IActionResult> Draft(Guid id, CancellationToken ct)
    {
        var (tenantId, refusal) = await TenantAsync(ct);
        if (refusal is not null) return refusal;
        var draft = await _store.GetDraftAsync(tenantId, id, ct);
        return draft.IsSuccess ? Answer(await ViewAsync(draft.Value!, ct)) : Failure(draft.Error!, draft.ErrorCode);
    }

    [HttpPut("drafts/{id:guid}")]
    public async Task<IActionResult> EditDraft(Guid id, [FromBody] EditDraftBody body, CancellationToken ct)
    {
        var (tenantId, refusal) = await TenantAsync(ct);
        if (refusal is not null) return refusal;
        var edited = await _actions.EditAsync(tenantId, id, body.DraftText, ct);
        if (!edited.IsSuccess)
            return Failure(edited.Error!, edited.ErrorCode);
        if (edited.Value!.Violations is { } violations)
            return Refused(violations);
        return Answer(await ViewAsync(edited.Value.Value!, ct));
    }

    [HttpPost("drafts/{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
    {
        var (tenantId, refusal) = await TenantAsync(ct);
        if (refusal is not null) return refusal;
        var approved = await _actions.ApproveAsync(tenantId, id, UserId(), ct);
        if (!approved.IsSuccess)
            return Failure(approved.Error!, approved.ErrorCode);
        if (approved.Value!.Violations is { } violations)
            return Refused(violations);

        var approval = approved.Value.Value!;
        var view = await ViewAsync(approval.Draft, ct);
        return view.IsSuccess ? Ok(new { draft = view.Value, send = SendView(approval.Send) }) : Failure(view.Error!, view.ErrorCode);
    }

    [HttpPost("drafts/{id:guid}/send")]
    public async Task<IActionResult> Send(Guid id, CancellationToken ct)
    {
        var (tenantId, refusal) = await TenantAsync(ct);
        if (refusal is not null) return refusal;
        var sent = await _actions.SendAsync(tenantId, id, UserId(), ct);
        if (!sent.IsSuccess)
            return Failure(sent.Error!, sent.ErrorCode);

        var view = sent.Value!.Draft is { } draft ? await ViewAsync(draft, ct) : null;
        if (view is { IsSuccess: false })
            return Failure(view.Error!, view.ErrorCode);
        return Ok(new { draft = view?.Value, send = SendView(sent.Value) });
    }

    [HttpPost("drafts/{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, CancellationToken ct)
    {
        var (tenantId, refusal) = await TenantAsync(ct);
        if (refusal is not null) return refusal;
        var rejected = await _actions.RejectAsync(tenantId, id, UserId(), ct);
        return rejected.IsSuccess ? Answer(await ViewAsync(rejected.Value!, ct)) : Failure(rejected.Error!, rejected.ErrorCode);
    }

    // ----- Comments -----

    public sealed class ReactBody
    {
        public string ReactionType { get; set; } = string.Empty;
    }

    [HttpGet("comments")]
    public async Task<IActionResult> Comments([FromQuery] string? state, [FromQuery] Guid? accountId, [FromQuery] int? limit, CancellationToken ct)
    {
        if (!TriageOutcomes.IsKnownFilter(state))
            return Failure($"state must be one of: {string.Join(", ", TriageOutcomes.Filters)}.", SocialFailureCodes.InvalidRequest);

        var (tenantId, refusal) = await TenantAsync(ct);
        if (refusal is not null) return refusal;
        var events = await _store.ListEventsAsync(tenantId, state!, accountId, Limit(limit), ct);
        return events.IsSuccess ? Ok(events.Value!.Select(CommentView)) : Failure(events.Error!, events.ErrorCode);
    }

    [HttpPost("comments/{id:guid}/rerun")]
    public async Task<IActionResult> Rerun(Guid id, CancellationToken ct)
    {
        var (tenantId, refusal) = await TenantAsync(ct);
        if (refusal is not null) return refusal;
        var rerun = await _orchestrator.RerunAsync(tenantId, id, ct);
        return rerun.IsSuccess
            ? Ok(new { outcome = rerun.Value!.TriageOutcome, failureCode = rerun.Value.TriageFailureCode })
            : Failure(rerun.Error!, rerun.ErrorCode);
    }

    [HttpPost("comments/{id:guid}/react")]
    public async Task<IActionResult> React(Guid id, [FromBody] ReactBody body, CancellationToken ct)
    {
        var (tenantId, refusal) = await TenantAsync(ct);
        if (refusal is not null) return refusal;
        var reacted = await _actions.ReactAsync(tenantId, id, body.ReactionType, UserId(), ct);
        return reacted.IsSuccess ? Ok(new { platformReactionId = reacted.Value!.PlatformReactionId }) : Failure(reacted.Error!, reacted.ErrorCode);
    }

    // ----- Posts -----

    public sealed class PublishBody
    {
        public Guid AccountId { get; set; }
        public SocialPostRequest Post { get; set; } = new();
    }

    [HttpGet("posts")]
    public async Task<IActionResult> Posts([FromQuery] Guid? accountId, [FromQuery] int? limit, CancellationToken ct)
    {
        if (accountId is not { } id)
            return Failure("accountId is required.", SocialFailureCodes.InvalidRequest);
        var (tenantId, refusal) = await TenantAsync(ct);
        if (refusal is not null) return refusal;
        return Answer(await _store.ListPostsAsync(tenantId, id, Limit(limit), ct));
    }

    [HttpPost("posts/copy")]
    public async Task<IActionResult> PostCopy([FromBody] PostCopyRequest body, CancellationToken ct)
    {
        var (tenantId, refusal) = await TenantAsync(ct);
        if (refusal is not null) return refusal;
        return Answer(await _actions.SuggestPostCopyAsync(tenantId, body, ct));
    }

    [HttpPost("posts")]
    public async Task<IActionResult> Publish([FromBody] PublishBody body, CancellationToken ct)
    {
        if (body.Post.ScheduledFor is { } at)
            body.Post.ScheduledFor = DateTime.SpecifyKind(at.ToUniversalTime(), DateTimeKind.Utc);

        var (tenantId, refusal) = await TenantAsync(ct);
        if (refusal is not null) return refusal;
        var published = await _actions.PublishAsync(tenantId, body.AccountId, body.Post, UserId(), ct);
        if (!published.IsSuccess)
            return Failure(published.Error!, published.ErrorCode);
        return published.Value!.Violations is { } violations ? Refused(violations) : Ok(published.Value.Value);
    }

    // ----- Audit -----

    [HttpGet("audit")]
    public async Task<IActionResult> Audit([FromQuery] Guid? accountId, [FromQuery] int? limit, CancellationToken ct)
    {
        var (tenantId, refusal) = await TenantAsync(ct);
        if (refusal is not null) return refusal;
        return Answer(await _store.ListAuditAsync(tenantId, accountId, Limit(limit), ct));
    }

    // ----- Views -----

    /// <summary>The draft with the comment it answers. The comment's text and author are null once their retention has run out.</summary>
    private async Task<Result<object>> ViewAsync(ReplyDraft draft, CancellationToken ct)
    {
        var ev = await _store.GetEventAsync(draft.TenantId, draft.EngagementEventId, ct);
        if (!ev.IsSuccess)
            return Result<object>.Failure(ev.Error!, ev.ErrorCode ?? SocialFailureCodes.StoreUnavailable);

        return Result<object>.Success(new
        {
            draft.Id,
            draft.AccountId,
            draft.Platform,
            draft.Category,
            draft.DraftText,
            draft.NeedsHumanReason,
            draft.ReviewRefusal,
            draft.Status,
            draft.SentPlatformCommentId,
            draft.FailureCode,
            draft.CreatedAt,
            draft.DecidedAt,
            draft.SendingStartedAt,
            Comment = new
            {
                EventId = ev.Value!.Id,
                ev.Value.Text,
                ev.Value.AuthorDisplayName,
                ev.Value.PlatformPostId,
                ev.Value.PlatformCommentId,
                ev.Value.OccurredAt,
            },
        });
    }

    private static object CommentView(EngagementEvent ev) => new
    {
        ev.Id,
        ev.AccountId,
        ev.Platform,
        ev.Kind,
        ev.PlatformPostId,
        ev.PlatformCommentId,
        ev.AuthorDisplayName,
        ev.Text,
        ev.OccurredAt,
        ev.TriageOutcome,
        ev.TriageFailureCode,
        ev.TriagedAt,
    };

    private static object SendView(SocialSendOutcome send) => new
    {
        send.Sent,
        send.Held,
        send.Code,
        send.Error,
        send.RetryAfter,
    };

    // ----- Answers -----

    private Guid? OwnerId() => Guid.TryParse(User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && id != Guid.Empty ? id : null;

    /// <summary>
    /// The tenant the signed-in person owns, or the refusal to answer with. A person with no tenant is told to sign
    /// up; a tenant is never made on the way past, so a read can never create one.
    /// </summary>
    private async Task<(Guid TenantId, IActionResult? Refusal)> TenantAsync(CancellationToken ct)
    {
        var owner = OwnerId();
        if (owner is null)
            return (Guid.Empty, Failure("The token's subject is not a user id.", SocialFailureCodes.Forbidden));

        var tenant = await _store.GetTenantByOwnerAsync(owner.Value, ct);
        if (tenant.IsSuccess)
            return (tenant.Value!.Id, null);

        return tenant.ErrorCode == SocialFailureCodes.NotFound
            ? (Guid.Empty, Failure("Sign up for GeekSocial before using it.", SocialFailureCodes.Forbidden))
            : (Guid.Empty, Failure(tenant.Error!, tenant.ErrorCode));
    }

    private string UserId() =>
        User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new UnauthorizedAccessException("The token carries no subject.");

    private static int Limit(int? limit) => limit is null or <= 0 ? DefaultLimit : Math.Min(limit.Value, MaxLimit);

    private IActionResult Answer<T>(Result<T> result) =>
        result.IsSuccess ? Ok(result.Value) : Failure(result.Error!, result.ErrorCode);

    private IActionResult Refused(IReadOnlyList<string> violations) =>
        StatusCode(StatusCodes.Status422UnprocessableEntity, new
        {
            error = $"The text breaks: {string.Join(", ", violations)}.",
            code = ReviewRefused,
            violations,
        });

    private ObjectResult Failure(string error, string? code) =>
        StatusCode(StatusFor(code), new { error, code });

    /// <summary>The HTTP status a failure code is answered with (docs/GEEK-SOCIAL-API.md).</summary>
    internal static int StatusFor(string? code) => code switch
    {
        SocialFailureCodes.InvalidRequest => StatusCodes.Status400BadRequest,
        SocialFailureCodes.Forbidden => StatusCodes.Status403Forbidden,
        SocialFailureCodes.NotFound => StatusCodes.Status404NotFound,
        SocialFailureCodes.Conflict => StatusCodes.Status409Conflict,
        SocialFailureCodes.TokenInvalid => StatusCodes.Status409Conflict,
        SocialFailureCodes.ClearanceRequired => StatusCodes.Status409Conflict,
        SocialFailureCodes.NotSupported => StatusCodes.Status422UnprocessableEntity,
        SocialFailureCodes.RateLimited => StatusCodes.Status429TooManyRequests,
        _ => StatusCodes.Status503ServiceUnavailable,
    };
}
