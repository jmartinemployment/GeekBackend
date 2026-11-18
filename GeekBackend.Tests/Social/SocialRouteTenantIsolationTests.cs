using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using GeekAPI.Controllers.Social;
using GeekAPI.Services.Social;
using GeekAPI.Services.Social.Connector;
using GeekAPI.Services.Social.LanguageModel;
using GeekAPI.Services.Social.Listener;
using GeekAPI.Services.Social.Meta;
using GeekAPI.Services.Social.Orchestration;
using GeekAPI.Services.Social.Publishing;
using GeekAPI.Services.Social.Review;
using GeekAPI.Services.Social.Sending;
using GeekApplication.Interfaces.Social;
using GeekApplication.Models.Social;
using GeekApplication.Results;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeekBackend.Tests.Social;

/// <summary>
/// Every GeekAPI social route, called by one tenant's person with another tenant's ids (roadmap Phase 0, item 7; these
/// gate every release). The controller runs over the real operator actions, send step, sender, token provider, resync,
/// orchestrator and connect service, with a store that keeps two tenants apart as SocialRepository's SQL does; only the
/// platform and the AI provider are fakes. Joining a live-update group is covered in SocialTenantIsolationTests.
/// </summary>
public class SocialRouteTenantIsolationTests
{
    private static readonly Guid PersonA = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid PersonB = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");
    private static readonly Guid PersonWithNoTenant = Guid.Parse("cccccccc-0000-0000-0000-00000000000c");
    private const string RedirectUri = "https://geek-social.example.test/connect/meta/callback";
    private static readonly CancellationToken Ct = CancellationToken.None;

    private sealed class RecordingConnector : ISocialAccountConnector
    {
        public string? LastState { get; private set; }
        public int Exchanges { get; private set; }

        public string Platform => SocialPlatforms.Facebook;

        public Result<Uri> BuildAuthorizationUrl(string state, Uri redirectUri)
        {
            LastState = state;
            return Result<Uri>.Success(new Uri($"https://www.facebook.example.test/dialog/oauth?state={Uri.EscapeDataString(state)}"));
        }

        public Task<Result<SocialConnection>> ExchangeAuthorizationCodeAsync(string code, Uri redirectUri, CancellationToken ct = default)
        {
            Exchanges++;
            return Task.FromResult(Result<SocialConnection>.Failure("Meta refused the code.", SocialFailureCodes.TokenInvalid));
        }

        public Task<Result<SocialToken>> RefreshTokenAsync(SocialToken token, CancellationToken ct = default) =>
            Task.FromResult(Result<SocialToken>.Failure("Refreshing is not part of these tests.", SocialFailureCodes.NotSupported));

        public Task<Result<SocialTokenStatus>> GetTokenStatusAsync(SocialToken token, CancellationToken ct = default) =>
            Task.FromResult(Result<SocialTokenStatus>.Failure("Token status is not part of these tests.", SocialFailureCodes.NotSupported));

        public Task<Result<SocialWebhookSubscription>> EnsureWebhookSubscriptionAsync(SocialAccount account, SocialToken token, Uri callbackUrl, CancellationToken ct = default) =>
            Task.FromResult(Result<SocialWebhookSubscription>.Failure("Subscribing is not part of these tests.", SocialFailureCodes.NotSupported));
    }

    private sealed class Harness
    {
        public required TwoTenantSocialStore Store { get; init; }
        public required TenantRows A { get; init; }
        public required TenantRows B { get; init; }
        public required SocialSendStepTests.FakeWriter Platform { get; init; }
        public required RecordingConnector Connector { get; init; }
        public required FakeModelClient Model { get; init; }
        public required SocialConnectService Connect { get; init; }
        public required Func<Guid, SocialController> As { get; init; }
    }

    /// <summary>
    /// Tenant A (person A) has accepted the AI terms; tenant B (person B) has not, so its acceptance shows whether A's
    /// acceptance reached it. B has one more sent draft and one more declined comment, so the two tenants' counts differ.
    /// </summary>
    private static Harness Build()
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var protector = new SocialCredentialProtector(() => key);
        var store = new TwoTenantSocialStore();
        var a = store.Seed(PersonA, "Alpha Bookkeeping", aiTermsAccepted: true, protector);
        var b = store.Seed(PersonB, "Bravo Bakery", aiTermsAccepted: false, protector);
        store.AddSentDraftAndDeclinedComment(b);
        store.AddFailureForEveryTenant("purge");

        var platform = new SocialSendStepTests.FakeWriter();
        var connector = new RecordingConnector();
        var services = new ServiceCollection()
            .AddKeyedSingleton<ISocialEngagementWriter>(SocialPlatforms.Facebook, platform)
            .AddKeyedSingleton<ISocialPublisher>(SocialPlatforms.Facebook, platform)
            .AddKeyedSingleton<ISocialAccountConnector>(SocialPlatforms.Facebook, connector)
            .AddScoped<ISocialStore>(_ => store)
            .BuildServiceProvider();

        var clock = new MetaEngagementReaderTests.FixedClock(new DateTimeOffset(TwoTenantSocialStore.Now));
        var realtime = new FakeRealtime();
        var reviewer = new OutboundTextReviewer();
        var tokens = new SocialTokenProvider(store, protector, services, clock, new RecordingLog<SocialTokenProvider>());
        var sender = new SocialSender(services, store, clock, new RecordingLog<SocialSender>());
        var sendStep = new SocialSendStep(store, tokens, sender, reviewer, realtime, clock, new RecordingLog<SocialSendStep>());
        var resync = new SocialResync(services, store, tokens, new SocialWake(), clock, new RecordingLog<SocialResync>());
        var model = new FakeModelClient(_ => FakeModelClient.Answer("""{"facebook":"Our new guide is up."}"""));
        var actions = new SocialOperatorActions(store, reviewer, sendStep, tokens, sender, resync,
            new PostWriter(model, new RecordingLog<PostWriter>()), realtime, clock, new RecordingLog<SocialOperatorActions>());
        var orchestrator = new SocialOrchestrator(
            new SocialWake(), services.GetRequiredService<IServiceScopeFactory>(), realtime, clock, new RecordingLog<SocialOrchestrator>());
        var connect = new SocialConnectService(services, store, protector, new EphemeralDataProtectionProvider(), realtime, clock,
            new RecordingLog<SocialConnectService>());

        return new Harness
        {
            Store = store,
            A = a,
            B = b,
            Platform = platform,
            Connector = connector,
            Model = model,
            Connect = connect,
            As = person => SignedIn(new SocialController(store, actions, orchestrator, connect,
                SocialAiClearance.From(_ => null), SocialModelSettings.From(_ => null), new MetaOptions()), person),
        };
    }

    private static SocialController SignedIn(SocialController controller, Guid person)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", person.ToString()) }, "test")),
        };
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("api.example.test");
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return controller;
    }

    private static int StatusOf(IActionResult result) => result switch
    {
        ObjectResult o => o.StatusCode ?? StatusCodes.Status200OK,
        StatusCodeResult s => s.StatusCode,
        _ => throw new InvalidOperationException($"A route answered with an unexpected {result.GetType().Name}."),
    };

    private static string Json(IActionResult result) =>
        JsonSerializer.Serialize(Assert.IsAssignableFrom<ObjectResult>(result).Value);

    private static SocialPostRequest LinkPost() => new()
    {
        Kind = SocialPostKinds.Link,
        Text = "Our new guide is up.",
        LinkUrl = "https://example.test/guide",
    };

    // ----- The route table -----

    /// <summary>Routes that act on one row by its id: another tenant's id must answer exactly as an id that exists nowhere.</summary>
    private static readonly string[] ByIdRoutes =
    {
        "PUT accounts/{id:guid}/settings",
        "POST accounts/{id:guid}/resync",
        "GET drafts/{id:guid}",
        "PUT drafts/{id:guid}",
        "POST drafts/{id:guid}/approve",
        "POST drafts/{id:guid}/send",
        "POST drafts/{id:guid}/reject",
        "POST comments/{id:guid}/rerun",
        "POST comments/{id:guid}/react",
        "POST posts",
    };

    /// <summary>Routes that list rows, filtered by an account or not: they hold only the caller's tenant's rows.</summary>
    private static readonly string[] ListRoutes = { "GET accounts", "GET drafts", "GET comments", "GET posts", "GET audit" };

    /// <summary>Routes that take no row id and act on the caller's own tenant; each has its own test below.</summary>
    private static readonly string[] OwnTenantRoutes =
    {
        "GET status",
        "GET platforms",
        "POST tenant/ai-terms",
        "POST connect/meta/start",
        "POST connect/meta/complete",
        "POST posts/copy",
    };

    /// <summary>Routes that find the tenant by the signed-in person and make no tenant-scoped call before it exists.</summary>
    private static readonly string[] OwnerRoutes = { "GET tenant", "POST tenant" };

    public static TheoryData<string> ByIdRouteData() => new(ByIdRoutes);

    public static TheoryData<string> TenantRouteData() => new(ByIdRoutes.Concat(ListRoutes).Concat(OwnTenantRoutes));

    /// <summary>Calls <paramref name="route"/> on <paramref name="controller"/> with the ids in <paramref name="rows"/>.</summary>
    private static Task<IActionResult> CallAsync(SocialController controller, string route, TenantRows rows) => route switch
    {
        "GET status" => controller.Status(Ct),
        "GET platforms" => controller.Platforms(Ct),
        "POST tenant/ai-terms" => controller.AcceptAiTerms(Ct),
        "GET accounts" => controller.Accounts(Ct),
        "PUT accounts/{id:guid}/settings" => controller.UpdateSettings(rows.AccountId, new UpdateSocialAccountSettingsCommand { SendsDisabled = true }, Ct),
        "POST accounts/{id:guid}/resync" => controller.Resync(rows.AccountId, Ct),
        "POST connect/meta/start" => controller.ConnectMetaStart(new SocialController.ConnectStartBody { RedirectUri = RedirectUri }, Ct),
        "POST connect/meta/complete" => controller.ConnectMetaComplete(
            new SocialController.ConnectCompleteBody { Code = "CODE", State = "STATE", RedirectUri = RedirectUri }, Ct),
        "GET drafts" => controller.Drafts(null, rows.AccountId, null, Ct),
        "GET drafts/{id:guid}" => controller.Draft(rows.PendingDraftId, Ct),
        "PUT drafts/{id:guid}" => controller.EditDraft(rows.PendingDraftId, new SocialController.EditDraftBody { DraftText = "Thanks for asking. We open at nine on Saturdays." }, Ct),
        "POST drafts/{id:guid}/approve" => controller.Approve(rows.PendingDraftId, Ct),
        "POST drafts/{id:guid}/send" => controller.Send(rows.ApprovedDraftId, Ct),
        "POST drafts/{id:guid}/reject" => controller.Reject(rows.PendingDraftId, Ct),
        "GET comments" => controller.Comments(TriageOutcomes.ReplyNeeded, rows.AccountId, null, Ct),
        "POST comments/{id:guid}/rerun" => controller.Rerun(rows.EventId, Ct),
        "POST comments/{id:guid}/react" => controller.React(rows.EventId, new SocialController.ReactBody { ReactionType = "LIKE" }, Ct),
        "GET posts" => controller.Posts(rows.AccountId, null, Ct),
        "POST posts/copy" => controller.PostCopy(new PostCopyRequest
        {
            ArticleUrl = "https://example.test/guide", ArticleTitle = "Our guide", ArticleSummary = "What to track each month.",
            Platforms = new List<string> { SocialPlatforms.Facebook },
        }, Ct),
        "POST posts" => controller.Publish(new SocialController.PublishBody { AccountId = rows.AccountId, Post = LinkPost() }, Ct),
        "GET audit" => controller.Audit(rows.AccountId, null, Ct),
        _ => throw new ArgumentOutOfRangeException(nameof(route), route, "Not in the route table."),
    };

    [Fact]
    public void Every_route_on_the_controller_is_in_the_route_table_once()
    {
        var onController = typeof(SocialController).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>())
            .SelectMany(a => a.HttpMethods.Select(verb => $"{verb} {a.Template}"))
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToList();
        var inTable = ByIdRoutes.Concat(ListRoutes).Concat(OwnTenantRoutes).Concat(OwnerRoutes).OrderBy(r => r, StringComparer.Ordinal).ToList();

        Assert.Equal(onController, inTable);
    }

    // ----- Another tenant's ids -----

    [Theory]
    [MemberData(nameof(ByIdRouteData))]
    public async Task Another_tenants_id_answers_as_an_id_that_exists_nowhere_and_nothing_of_theirs_changes(string route)
    {
        var h = Build();
        var before = h.Store.Snapshot(h.B.TenantId);

        var theirs = await CallAsync(h.As(PersonA), route, h.B);

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(theirs));
        Assert.Equal(Json(await CallAsync(h.As(PersonA), route, TenantRows.Nowhere())), Json(theirs));
        Assert.Equal(before, h.Store.Snapshot(h.B.TenantId));
        Assert.All(h.Store.TenantsAsked, t => Assert.Equal(h.A.TenantId, t));
        Assert.All(h.Store.FailuresKept, f => Assert.Equal(h.A.TenantId, f.TenantId));
        Assert.Empty(h.Store.AcrossTenantCalls);
        Assert.Empty(h.Platform.Replies);
        Assert.Empty(h.Platform.Reactions);
        Assert.Empty(h.Platform.Posts);
        Assert.Empty(h.Model.Requests);
    }

    [Theory]
    [MemberData(nameof(ByIdRouteData))]
    public async Task The_same_ids_resolve_for_the_tenant_that_owns_them(string route)
    {
        var h = Build();

        var own = await CallAsync(h.As(PersonB), route, h.B);

        Assert.NotEqual(StatusCodes.Status404NotFound, StatusOf(own));
        Assert.All(h.Store.TenantsAsked, t => Assert.Equal(h.B.TenantId, t));
        Assert.Empty(h.Store.AcrossTenantCalls);
    }

    [Fact]
    public async Task A_list_filtered_by_another_tenants_account_is_empty_and_an_unfiltered_one_holds_only_ones_own_rows()
    {
        var h = Build();
        var asA = h.As(PersonA);

        foreach (var route in ListRoutes.Where(r => r != "GET accounts"))
        {
            var filtered = await CallAsync(asA, route, h.B);
            Assert.Equal(StatusCodes.Status200OK, StatusOf(filtered));
            Assert.Equal("[]", Json(filtered));
        }

        var accounts = Json(await asA.Accounts(Ct));
        var drafts = Json(await asA.Drafts(null, null, null, Ct));
        var comments = Json(await asA.Comments(TriageOutcomes.ReplyNeeded, null, null, Ct));
        var audit = Json(await asA.Audit(null, null, Ct));
        var posts = Json(await asA.Posts(h.A.AccountId, null, Ct));

        Assert.Contains(h.A.AccountId.ToString(), accounts, StringComparison.Ordinal);
        Assert.Contains(h.A.PendingDraftId.ToString(), drafts, StringComparison.Ordinal);
        Assert.Contains(h.A.EventId.ToString(), comments, StringComparison.Ordinal);
        Assert.Contains(h.A.AuditId.ToString(), audit, StringComparison.Ordinal);
        Assert.Contains(h.A.PostId.ToString(), posts, StringComparison.Ordinal);
        foreach (var answer in new[] { accounts, drafts, comments, audit, posts })
        {
            foreach (var id in new[] { h.B.AccountId, h.B.EventId, h.B.PendingDraftId, h.B.ApprovedDraftId, h.B.PostId, h.B.AuditId })
                Assert.DoesNotContain(id.ToString(), answer, StringComparison.Ordinal);
            Assert.DoesNotContain("Bravo", answer, StringComparison.Ordinal);
        }
        Assert.All(h.Store.TenantsAsked, t => Assert.Equal(h.A.TenantId, t));
        Assert.Empty(h.Store.AcrossTenantCalls);
    }

    // ----- Routes on one's own tenant -----

    [Fact]
    public async Task Status_counts_only_ones_own_rows_and_lists_ones_own_failures_and_those_for_every_tenant()
    {
        var h = Build();

        var status = Json(await h.As(PersonA).Status(Ct));

        using var doc = JsonDocument.Parse(status);
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("comments").GetProperty("ReplyNeeded").GetInt32());
        Assert.Equal(0, root.GetProperty("comments").GetProperty("NoReplyNeeded").GetInt32());
        Assert.Equal(1, root.GetProperty("drafts").GetProperty("Pending").GetInt32());
        Assert.Equal(1, root.GetProperty("drafts").GetProperty("Approved").GetInt32());
        Assert.Equal(0, root.GetProperty("drafts").GetProperty("Sent").GetInt32());
        var failures = root.GetProperty("failures").EnumerateArray().Select(f => f.GetProperty("Step").GetString()).ToList();
        Assert.Contains(h.A.FailureStep, failures);
        Assert.Contains("purge", failures);
        Assert.DoesNotContain(h.B.FailureStep, failures);
        Assert.All(h.Store.TenantsAsked, t => Assert.Equal(h.A.TenantId, t));
    }

    [Fact]
    public async Task The_platform_matrix_carries_nothing_of_any_tenant()
    {
        var h = Build();

        var platforms = Json(await h.As(PersonA).Platforms(Ct));

        Assert.Equal(platforms, Json(await h.As(PersonB).Platforms(Ct)));
        Assert.DoesNotContain("Alpha", platforms, StringComparison.Ordinal);
        Assert.DoesNotContain("Bravo", platforms, StringComparison.Ordinal);
        Assert.Empty(h.Store.TenantsAsked);
    }

    [Fact]
    public async Task The_tenant_and_the_AI_terms_are_ones_own_and_signing_up_again_touches_no_other_tenant()
    {
        var h = Build();
        var before = h.Store.Snapshot(h.B.TenantId);
        var asA = h.As(PersonA);

        var tenant = Json(await asA.GetTenant(Ct));
        var accepted = await asA.AcceptAiTerms(Ct);
        var again = await asA.CreateTenant(new SocialController.CreateTenantBody { Name = "Bravo Bakery" }, Ct);

        Assert.Contains("Alpha Bookkeeping", tenant, StringComparison.Ordinal);
        Assert.DoesNotContain("Bravo", tenant, StringComparison.Ordinal);
        Assert.Equal(StatusCodes.Status200OK, StatusOf(accepted));
        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(again));
        Assert.Null(h.Store.Tenant(h.B.TenantId).AiConsentAcceptedAt);
        Assert.Equal(before, h.Store.Snapshot(h.B.TenantId));
        Assert.All(h.Store.TenantsAsked, t => Assert.Equal(h.A.TenantId, t));
    }

    [Fact]
    public async Task Post_copy_names_ones_own_business_and_never_another_tenants()
    {
        var h = Build();

        var copy = await CallAsync(h.As(PersonA), "POST posts/copy", h.A);

        Assert.Equal(StatusCodes.Status200OK, StatusOf(copy));
        var sent = Assert.Single(h.Model.Requests);
        Assert.Contains("Alpha Bookkeeping", sent.UserMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("Bravo", sent.UserMessage + sent.SystemPrompt, StringComparison.Ordinal);
        Assert.All(h.Store.TenantsAsked, t => Assert.Equal(h.A.TenantId, t));
    }

    [Fact]
    public async Task A_connection_another_tenants_person_started_is_refused_before_the_code_is_exchanged()
    {
        var h = Build();
        var before = h.Store.Snapshot(h.B.TenantId);
        Assert.Equal(StatusCodes.Status200OK, StatusOf(await h.As(PersonB).ConnectMetaStart(new SocialController.ConnectStartBody { RedirectUri = RedirectUri }, Ct)));
        var theirState = h.Connector.LastState ?? throw new InvalidOperationException("Starting did not build an authorization address.");
        var body = new SocialController.ConnectCompleteBody { Code = "CODE", State = theirState, RedirectUri = RedirectUri };

        var taken = await h.As(PersonA).ConnectMetaComplete(body, Ct);

        Assert.Equal(StatusCodes.Status403Forbidden, StatusOf(taken));
        Assert.Equal(0, h.Connector.Exchanges);
        Assert.Equal(before, h.Store.Snapshot(h.B.TenantId));

        var own = await h.As(PersonB).ConnectMetaComplete(body, Ct);
        Assert.NotEqual(StatusCodes.Status403Forbidden, StatusOf(own));
        Assert.Equal(1, h.Connector.Exchanges);
    }

    // ----- No tenant -----

    [Theory]
    [MemberData(nameof(TenantRouteData))]
    public async Task A_person_with_no_tenant_is_refused_on_every_tenant_route_and_reaches_no_tenants_rows(string route)
    {
        var h = Build();

        var refused = await CallAsync(h.As(PersonWithNoTenant), route, h.B);

        Assert.Equal(StatusCodes.Status403Forbidden, StatusOf(refused));
        Assert.Contains("Sign up for GeekSocial", Json(refused), StringComparison.Ordinal);
        Assert.Empty(h.Store.TenantsAsked);
        Assert.Empty(h.Store.AcrossTenantCalls);
        Assert.Empty(h.Model.Requests);
    }
}
