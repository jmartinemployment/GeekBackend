using System.Net;
using System.Text;
using System.Text.Json;
using GeekApplication.Interfaces.Crm;
using GeekApplication.Models.Crm;
using GeekApplication.Results;

namespace GeekAPI.HttpClients;

/// <summary>
/// repo/crm/* over the GeekRepository client: the <see cref="ICrmStore"/> GeekAPI calls. 404 with a
/// message is not_found, 400 is invalid_request, 409 is conflict, and anything else (an unreachable
/// GeekRepository included) is store_unavailable. Logs carry the route and status, never a body:
/// bodies hold people's names and numbers.
/// </summary>
public sealed class HttpCrmRepository(HttpClient http, ILogger<HttpCrmRepository> logger) : ICrmStore, ICrmPlatformStore, ICrmWorkStore
{
    private const int MessageLimit = 300;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public Task<Result<EnsureParticipantResult>> EnsureParticipantByPhoneAsync(EnsureParticipantByPhoneCommand command, CancellationToken ct = default) =>
        SendAsync<EnsureParticipantResult>(HttpMethod.Post, "repo/crm/participants/ensure-by-phone", command, ct);

    public Task<Result<RecordActivityResult>> RecordActivityAsync(RecordActivityCommand command, CancellationToken ct = default) =>
        SendAsync<RecordActivityResult>(HttpMethod.Post, "repo/crm/activities", command, ct);

    public Task<Result<bool>> SetSmsOptOutAsync(SetSmsOptOutCommand command, CancellationToken ct = default) =>
        SendAsync<bool>(HttpMethod.Post, "repo/crm/contact-points/sms-opt-out", command, ct);

    public Task<Result<ImportProspectsResult>> ImportProspectsAsync(ImportProspectsCommand command, CancellationToken ct = default) =>
        SendAsync<ImportProspectsResult>(HttpMethod.Post, "repo/crm/imports", command, ct);

    public async Task<Result<IReadOnlyList<CrmParticipantSummary>>> ListParticipantsAsync(Guid tenantId, string? lifecycleStage, string? kind, int limit, CancellationToken ct = default)
    {
        var query = new List<string> { $"limit={limit}" };
        if (lifecycleStage is not null) query.Add("stage=" + Uri.EscapeDataString(lifecycleStage));
        if (kind is not null) query.Add("kind=" + Uri.EscapeDataString(kind));
        return AsReadOnly(await SendAsync<List<CrmParticipantSummary>>(HttpMethod.Get,
            $"repo/crm/tenants/{tenantId}/participants?{string.Join('&', query)}", null, ct).ConfigureAwait(false));
    }

    public Task<Result<CrmParticipant>> GetParticipantAsync(Guid tenantId, Guid participantId, CancellationToken ct = default) =>
        SendAsync<CrmParticipant>(HttpMethod.Get, $"repo/crm/tenants/{tenantId}/participants/{participantId}", null, ct);

    public Task<Result<CrmDeal>> CreateDealAsync(CreateDealCommand command, CancellationToken ct = default) =>
        SendAsync<CrmDeal>(HttpMethod.Post, "repo/crm/deals", command, ct);

    public Task<Result<CrmDeal>> MoveDealStageAsync(Guid dealId, MoveDealStageCommand command, CancellationToken ct = default) =>
        SendAsync<CrmDeal>(HttpMethod.Post, $"repo/crm/deals/{dealId}/stage", command, ct);

    public async Task<Result<IReadOnlyList<CrmDeal>>> ListDealsAsync(Guid tenantId, bool openOnly, int limit, CancellationToken ct = default) =>
        AsReadOnly(await SendAsync<List<CrmDeal>>(HttpMethod.Get,
            $"repo/crm/tenants/{tenantId}/deals?openOnly={(openOnly ? "true" : "false")}&limit={limit}", null, ct).ConfigureAwait(false));

    public async Task<Result<IReadOnlyList<CrmNaicsCode>>> SearchNaicsAsync(string? query, int? level, int limit, CancellationToken ct = default)
    {
        var parts = new List<string> { $"limit={limit}" };
        if (query is not null) parts.Add("q=" + Uri.EscapeDataString(query));
        if (level is { } l) parts.Add($"level={l}");
        return AsReadOnly(await SendAsync<List<CrmNaicsCode>>(HttpMethod.Get, $"repo/crm/lookups/naics?{string.Join('&', parts)}", null, ct).ConfigureAwait(false));
    }

    public async Task<Result<IReadOnlyList<CrmPropertyDefinition>>> ListPropertyDefinitionsAsync(Guid tenantId, string? objectType, bool includeRetired, bool includeSensitive, CancellationToken ct = default)
    {
        var parts = new List<string>
        {
            $"includeRetired={(includeRetired ? "true" : "false")}",
            $"includeSensitive={(includeSensitive ? "true" : "false")}",
        };
        if (objectType is not null) parts.Add("objectType=" + Uri.EscapeDataString(objectType));
        return AsReadOnly(await SendAsync<List<CrmPropertyDefinition>>(HttpMethod.Get,
            $"repo/crm/tenants/{tenantId}/properties/definitions?{string.Join('&', parts)}", null, ct).ConfigureAwait(false));
    }

    public Task<Result<CrmPropertyDefinition>> CreatePropertyDefinitionAsync(CreatePropertyDefinitionCommand command, CancellationToken ct = default) =>
        SendAsync<CrmPropertyDefinition>(HttpMethod.Post, "repo/crm/properties/definitions", command, ct);

    public Task<Result<CrmPropertyDefinition>> UpdatePropertyDefinitionAsync(Guid definitionId, UpdatePropertyDefinitionCommand command, CancellationToken ct = default) =>
        SendAsync<CrmPropertyDefinition>(HttpMethod.Put, $"repo/crm/properties/definitions/{definitionId}", command, ct);

    public Task<Result<CrmPropertyDefinition>> RetirePropertyDefinitionAsync(Guid tenantId, Guid definitionId, CancellationToken ct = default) =>
        SendAsync<CrmPropertyDefinition>(HttpMethod.Post, $"repo/crm/tenants/{tenantId}/properties/definitions/{definitionId}/retire", null, ct);

    public async Task<Result<IReadOnlyList<CrmPropertyValue>>> GetPropertyValuesAsync(Guid tenantId, string objectType, Guid recordId, bool includeSensitive, CancellationToken ct = default) =>
        AsReadOnly(await SendAsync<List<CrmPropertyValue>>(HttpMethod.Get,
            $"repo/crm/tenants/{tenantId}/records/{Uri.EscapeDataString(objectType)}/{recordId}/properties?includeSensitive={(includeSensitive ? "true" : "false")}",
            null, ct).ConfigureAwait(false));

    public async Task<Result<IReadOnlyList<CrmPropertyValue>>> SetPropertyValuesAsync(SetPropertyValuesCommand command, CancellationToken ct = default) =>
        AsReadOnly(await SendAsync<List<CrmPropertyValue>>(HttpMethod.Put, "repo/crm/records/properties", command, ct).ConfigureAwait(false));

    public async Task<Result<IReadOnlyList<CrmProduct>>> ListProductsAsync(Guid tenantId, string? kind, bool includeRetired, CancellationToken ct = default)
    {
        var parts = new List<string> { $"includeRetired={(includeRetired ? "true" : "false")}" };
        if (kind is not null) parts.Add("kind=" + Uri.EscapeDataString(kind));
        return AsReadOnly(await SendAsync<List<CrmProduct>>(HttpMethod.Get, $"repo/crm/tenants/{tenantId}/products?{string.Join('&', parts)}", null, ct).ConfigureAwait(false));
    }

    public Task<Result<CrmProduct>> CreateProductAsync(CreateProductCommand command, CancellationToken ct = default) =>
        SendAsync<CrmProduct>(HttpMethod.Post, "repo/crm/products", command, ct);

    public Task<Result<CrmProduct>> UpdateProductAsync(Guid productId, UpdateProductCommand command, CancellationToken ct = default) =>
        SendAsync<CrmProduct>(HttpMethod.Put, $"repo/crm/products/{productId}", command, ct);

    public Task<Result<CrmDealLineItems>> ListLineItemsAsync(Guid tenantId, Guid dealId, CancellationToken ct = default) =>
        SendAsync<CrmDealLineItems>(HttpMethod.Get, $"repo/crm/tenants/{tenantId}/deals/{dealId}/line-items", null, ct);

    public Task<Result<CrmDealLineItems>> AddLineItemAsync(Guid dealId, AddLineItemCommand command, CancellationToken ct = default) =>
        SendAsync<CrmDealLineItems>(HttpMethod.Post, $"repo/crm/deals/{dealId}/line-items", command, ct);

    public Task<Result<CrmDealLineItems>> UpdateLineItemAsync(Guid dealId, Guid lineItemId, UpdateLineItemCommand command, CancellationToken ct = default) =>
        SendAsync<CrmDealLineItems>(HttpMethod.Put, $"repo/crm/deals/{dealId}/line-items/{lineItemId}", command, ct);

    public Task<Result<CrmDealLineItems>> DeleteLineItemAsync(Guid tenantId, Guid dealId, Guid lineItemId, CancellationToken ct = default) =>
        SendAsync<CrmDealLineItems>(HttpMethod.Delete, $"repo/crm/tenants/{tenantId}/deals/{dealId}/line-items/{lineItemId}", null, ct);

    public Task<Result<WinDealResult>> WinDealAsync(Guid tenantId, Guid dealId, string changedBy, CancellationToken ct = default) =>
        SendAsync<WinDealResult>(HttpMethod.Post, $"repo/crm/tenants/{tenantId}/deals/{dealId}/win?changedBy={Uri.EscapeDataString(changedBy)}", null, ct);

    public async Task<Result<IReadOnlyList<CrmHolding>>> ListHoldingsAsync(Guid tenantId, Guid participantId, CancellationToken ct = default) =>
        AsReadOnly(await SendAsync<List<CrmHolding>>(HttpMethod.Get, $"repo/crm/tenants/{tenantId}/participants/{participantId}/holdings", null, ct).ConfigureAwait(false));

    public Task<Result<CrmHolding>> CreateHoldingAsync(CreateHoldingCommand command, CancellationToken ct = default) =>
        SendAsync<CrmHolding>(HttpMethod.Post, "repo/crm/holdings", command, ct);

    public Task<Result<CrmHolding>> UpdateHoldingAsync(Guid holdingId, UpdateHoldingCommand command, CancellationToken ct = default) =>
        SendAsync<CrmHolding>(HttpMethod.Put, $"repo/crm/holdings/{holdingId}", command, ct);

    public async Task<Result<IReadOnlyList<CrmProductPrice>>> ListPricesAsync(Guid tenantId, Guid productId, bool includeHistory, CancellationToken ct = default) =>
        AsReadOnly(await SendAsync<List<CrmProductPrice>>(HttpMethod.Get,
            $"repo/crm/tenants/{tenantId}/products/{productId}/prices?includeHistory={(includeHistory ? "true" : "false")}", null, ct).ConfigureAwait(false));

    public Task<Result<CrmProductPrice>> GetPriceInEffectAsync(Guid tenantId, Guid productId, string? size, DateOnly? on, CancellationToken ct = default)
    {
        var parts = new List<string>();
        if (size is not null) parts.Add("size=" + Uri.EscapeDataString(size));
        if (on is { } date) parts.Add("on=" + date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        return SendAsync<CrmProductPrice>(HttpMethod.Get, $"repo/crm/tenants/{tenantId}/products/{productId}/price?{string.Join('&', parts)}", null, ct);
    }

    public Task<Result<CrmProductPrice>> SetPriceAsync(Guid productId, SetProductPriceCommand command, CancellationToken ct = default) =>
        SendAsync<CrmProductPrice>(HttpMethod.Post, $"repo/crm/products/{productId}/prices", command, ct);

    public Task<Result<CrmPricingRules>> GetPricingRulesAsync(Guid tenantId, CancellationToken ct = default) =>
        SendAsync<CrmPricingRules>(HttpMethod.Get, $"repo/crm/tenants/{tenantId}/pricing-rules", null, ct);

    public Task<Result<CrmPricingRules>> SetPricingRulesAsync(SetPricingRulesCommand command, CancellationToken ct = default) =>
        SendAsync<CrmPricingRules>(HttpMethod.Put, "repo/crm/pricing-rules", command, ct);

    public async Task<Result<IReadOnlyList<CrmUpliftTier>>> ListUpliftTiersAsync(Guid tenantId, CancellationToken ct = default) =>
        AsReadOnly(await SendAsync<List<CrmUpliftTier>>(HttpMethod.Get, $"repo/crm/tenants/{tenantId}/pricing-uplift-tiers", null, ct).ConfigureAwait(false));

    public async Task<Result<IReadOnlyList<CrmUpliftTier>>> ReplaceUpliftTiersAsync(ReplaceUpliftTiersCommand command, CancellationToken ct = default) =>
        AsReadOnly(await SendAsync<List<CrmUpliftTier>>(HttpMethod.Put, "repo/crm/pricing-uplift-tiers", command, ct).ConfigureAwait(false));

    public async Task<Result<IReadOnlyList<CrmTenantSubscription>>> ListSubscriptionsAsync(Guid? subscriberTenantId, CancellationToken ct = default) =>
        AsReadOnly(await SendAsync<List<CrmTenantSubscription>>(HttpMethod.Get,
            subscriberTenantId is { } id ? $"repo/platform/subscriptions?subscriberTenantId={id}" : "repo/platform/subscriptions", null, ct).ConfigureAwait(false));

    public Task<Result<CrmTenantSubscription>> CreateSubscriptionAsync(CreateTenantSubscriptionCommand command, CancellationToken ct = default) =>
        SendAsync<CrmTenantSubscription>(HttpMethod.Post, "repo/platform/subscriptions", command, ct);

    public Task<Result<CrmTenantSubscription>> UpdateSubscriptionAsync(Guid subscriptionId, UpdateTenantSubscriptionCommand command, CancellationToken ct = default) =>
        SendAsync<CrmTenantSubscription>(HttpMethod.Put, $"repo/platform/subscriptions/{subscriptionId}", command, ct);

    public async Task<Result<IReadOnlyList<CrmProject>>> ListProjectsAsync(Guid tenantId, string? status, Guid? participantId, CancellationToken ct = default)
    {
        var parts = new List<string>();
        if (status is not null) parts.Add("status=" + Uri.EscapeDataString(status));
        if (participantId is { } id) parts.Add($"participantId={id}");
        return AsReadOnly(await SendAsync<List<CrmProject>>(HttpMethod.Get, $"repo/crm/tenants/{tenantId}/projects?{string.Join('&', parts)}", null, ct).ConfigureAwait(false));
    }

    public Task<Result<CrmProject>> CreateProjectAsync(CreateProjectCommand command, CancellationToken ct = default) =>
        SendAsync<CrmProject>(HttpMethod.Post, "repo/crm/projects", command, ct);

    public Task<Result<CrmProject>> UpdateProjectAsync(Guid projectId, UpdateProjectCommand command, CancellationToken ct = default) =>
        SendAsync<CrmProject>(HttpMethod.Put, $"repo/crm/projects/{projectId}", command, ct);

    public Task<Result<CrmCapacity>> GetCapacityAsync(Guid tenantId, int weeks, DateOnly? asOf, CancellationToken ct = default) =>
        SendAsync<CrmCapacity>(HttpMethod.Get,
            $"repo/crm/tenants/{tenantId}/capacity?weeks={weeks}" + (asOf is { } date ? "&asOf=" + date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : string.Empty), null, ct);

    public async Task<Result<IReadOnlyList<CrmActivity>>> ListActivitiesAsync(Guid tenantId, string? priority, string? channel, int limit, CancellationToken ct = default)
    {
        var parts = new List<string> { $"limit={limit}" };
        if (priority is not null) parts.Add("priority=" + Uri.EscapeDataString(priority));
        if (channel is not null) parts.Add("channel=" + Uri.EscapeDataString(channel));
        return AsReadOnly(await SendAsync<List<CrmActivity>>(HttpMethod.Get, $"repo/crm/tenants/{tenantId}/activities?{string.Join('&', parts)}", null, ct).ConfigureAwait(false));
    }

    public Task<Result<ReceptionistIntakeResult>> ReceptionistIntakeAsync(ReceptionistIntakeCommand command, CancellationToken ct = default) =>
        SendAsync<ReceptionistIntakeResult>(HttpMethod.Post, "repo/crm/receptionist/intake", command, ct);

    public Task<Result<CrmMembership>> GetMembershipAsync(Guid userId, CancellationToken ct = default) =>
        SendAsync<CrmMembership>(HttpMethod.Get, $"repo/platform/memberships/{userId}", null, ct);

    public async Task<Result<IReadOnlyList<CrmTenantUser>>> ListTenantUsersAsync(Guid? tenantId, CancellationToken ct = default) =>
        AsReadOnly(await SendAsync<List<CrmTenantUser>>(HttpMethod.Get,
            tenantId is { } id ? $"repo/platform/tenant-users?tenantId={id}" : "repo/platform/tenant-users", null, ct).ConfigureAwait(false));

    public Task<Result<CrmTenantUser>> AddTenantUserAsync(AddTenantUserCommand command, CancellationToken ct = default) =>
        SendAsync<CrmTenantUser>(HttpMethod.Post, "repo/platform/tenant-users", command, ct);

    public Task<Result<CrmTenantUser>> SetTenantUserRoleAsync(Guid id, SetTenantUserRoleCommand command, CancellationToken ct = default) =>
        SendAsync<CrmTenantUser>(HttpMethod.Put, $"repo/platform/tenant-users/{id}", command, ct);

    public Task<Result<bool>> RemoveTenantUserAsync(Guid id, CancellationToken ct = default) =>
        SendAsync<bool>(HttpMethod.Delete, $"repo/platform/tenant-users/{id}", null, ct);

    // ----- The work a person does at the keyboard (ICrmWorkStore) -----

    public async Task<Result<IReadOnlyList<CrmStage>>> ListStagesAsync(Guid tenantId, CancellationToken ct = default) =>
        AsReadOnly(await SendAsync<List<CrmStage>>(HttpMethod.Get, $"repo/crm/tenants/{tenantId}/stages", null, ct).ConfigureAwait(false));

    public Task<Result<CrmDashboard>> GetDashboardAsync(Guid tenantId, CancellationToken ct = default) =>
        SendAsync<CrmDashboard>(HttpMethod.Get, $"repo/crm/tenants/{tenantId}/dashboard", null, ct);

    public async Task<Result<IReadOnlyList<CrmParticipantSummary>>> SearchParticipantsAsync(Guid tenantId, string? search, string? lifecycleStage, string? kind, int limit, CancellationToken ct = default)
    {
        var parts = new List<string> { $"limit={limit}" };
        if (search is not null) parts.Add("q=" + Uri.EscapeDataString(search));
        if (lifecycleStage is not null) parts.Add("stage=" + Uri.EscapeDataString(lifecycleStage));
        if (kind is not null) parts.Add("kind=" + Uri.EscapeDataString(kind));
        return AsReadOnly(await SendAsync<List<CrmParticipantSummary>>(HttpMethod.Get,
            $"repo/crm/tenants/{tenantId}/participants/search?{string.Join('&', parts)}", null, ct).ConfigureAwait(false));
    }

    public Task<Result<CrmParticipant>> CreateParticipantAsync(CreateParticipantCommand command, CancellationToken ct = default) =>
        SendAsync<CrmParticipant>(HttpMethod.Post, "repo/crm/participants", command, ct);

    public Task<Result<CrmParticipant>> UpdateParticipantAsync(Guid participantId, UpdateParticipantCommand command, CancellationToken ct = default) =>
        SendAsync<CrmParticipant>(HttpMethod.Put, $"repo/crm/participants/{participantId}", command, ct);

    public Task<Result<bool>> ArchiveParticipantAsync(Guid tenantId, Guid participantId, CancellationToken ct = default) =>
        SendAsync<bool>(HttpMethod.Post, $"repo/crm/tenants/{tenantId}/participants/{participantId}/archive", null, ct);

    public Task<Result<bool>> DeleteParticipantAsync(Guid tenantId, Guid participantId, CancellationToken ct = default) =>
        SendAsync<bool>(HttpMethod.Delete, $"repo/crm/tenants/{tenantId}/participants/{participantId}", null, ct);

    public Task<Result<bool>> DeleteDealAsync(Guid tenantId, Guid dealId, CancellationToken ct = default) =>
        SendAsync<bool>(HttpMethod.Delete, $"repo/crm/tenants/{tenantId}/deals/{dealId}", null, ct);

    public Task<Result<bool>> DeleteActivityAsync(Guid tenantId, Guid activityId, CancellationToken ct = default) =>
        SendAsync<bool>(HttpMethod.Delete, $"repo/crm/tenants/{tenantId}/activities/{activityId}", null, ct);

    public async Task<Result<IReadOnlyList<CrmDealView>>> ListDealViewsAsync(Guid tenantId, bool openOnly, string? search, int limit, CancellationToken ct = default)
    {
        var parts = new List<string> { $"openOnly={(openOnly ? "true" : "false")}", $"limit={limit}" };
        if (search is not null) parts.Add("q=" + Uri.EscapeDataString(search));
        return AsReadOnly(await SendAsync<List<CrmDealView>>(HttpMethod.Get,
            $"repo/crm/tenants/{tenantId}/deals/views?{string.Join('&', parts)}", null, ct).ConfigureAwait(false));
    }

    public Task<Result<CrmDealView>> GetDealViewAsync(Guid tenantId, Guid dealId, CancellationToken ct = default) =>
        SendAsync<CrmDealView>(HttpMethod.Get, $"repo/crm/tenants/{tenantId}/deals/{dealId}/view", null, ct);

    public Task<Result<CrmDealView>> UpdateDealAsync(Guid dealId, UpdateDealCommand command, CancellationToken ct = default) =>
        SendAsync<CrmDealView>(HttpMethod.Put, $"repo/crm/deals/{dealId}", command, ct);

    public async Task<Result<IReadOnlyList<CrmActivityView>>> ListActivityViewsAsync(Guid tenantId, Guid? participantId, Guid? dealId, string? channel, bool openTasks, int limit, CancellationToken ct = default)
    {
        var parts = new List<string> { $"openTasks={(openTasks ? "true" : "false")}", $"limit={limit}" };
        if (participantId is { } pid) parts.Add($"participantId={pid}");
        if (dealId is { } did) parts.Add($"dealId={did}");
        if (channel is not null) parts.Add("channel=" + Uri.EscapeDataString(channel));
        return AsReadOnly(await SendAsync<List<CrmActivityView>>(HttpMethod.Get,
            $"repo/crm/tenants/{tenantId}/activities/views?{string.Join('&', parts)}", null, ct).ConfigureAwait(false));
    }

    public Task<Result<CrmActivity>> CreateActivityAsync(CreateActivityCommand command, CancellationToken ct = default) =>
        SendAsync<CrmActivity>(HttpMethod.Post, "repo/crm/activities/create", command, ct);

    public Task<Result<CrmActivity>> CompleteTaskAsync(Guid tenantId, Guid activityId, CancellationToken ct = default) =>
        SendAsync<CrmActivity>(HttpMethod.Post, $"repo/crm/tenants/{tenantId}/activities/{activityId}/complete", null, ct);

    private static Result<IReadOnlyList<T>> AsReadOnly<T>(Result<List<T>> result) =>
        result.IsSuccess
            ? Result<IReadOnlyList<T>>.Success(result.Value!)
            : Result<IReadOnlyList<T>>.Failure(result.Error ?? "The list failed.", result.ErrorCode ?? CrmFailureCodes.StoreUnavailable);

    private async Task<Result<T>> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null)
                request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOpts), Encoding.UTF8, "application/json");

            using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var text = (await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false)).Trim();
                var message = text.Length == 0 ? null : text.Length <= MessageLimit ? text : text[..MessageLimit];
                switch (response.StatusCode)
                {
                    case HttpStatusCode.NotFound when message is not null:
                        return Result<T>.Failure(message, CrmFailureCodes.NotFound);
                    case HttpStatusCode.BadRequest:
                        return Result<T>.Failure(message ?? "GeekRepository refused the request.", CrmFailureCodes.InvalidRequest);
                    case HttpStatusCode.Conflict:
                        return Result<T>.Failure(message ?? "The record changed; read it again.", CrmFailureCodes.Conflict);
                    default:
                        logger.LogError("GeekRepository answered {Status} for {Method} {Path}.", (int)response.StatusCode, method.Method, Route(path));
                        return Unavailable<T>($"GeekRepository answered {(int)response.StatusCode}.");
                }
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var value = await JsonSerializer.DeserializeAsync<T>(stream, JsonOpts, ct).ConfigureAwait(false);
            if (value is not null) return Result<T>.Success(value);
            logger.LogError("GeekRepository answered an empty body for {Method} {Path}.", method.Method, Route(path));
            return Unavailable<T>("GeekRepository answered an empty body.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "GeekRepository unreachable for {Method} {Path}.", method.Method, Route(path));
            return Unavailable<T>("GeekRepository unreachable.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogError("GeekRepository timed out for {Method} {Path}.", method.Method, Route(path));
            return Unavailable<T>("GeekRepository timed out.");
        }
        catch (JsonException)
        {
            logger.LogError("GeekRepository answered a body that did not parse for {Method} {Path}.", method.Method, Route(path));
            return Unavailable<T>("GeekRepository answered a body that did not parse.");
        }
    }

    private static Result<T> Unavailable<T>(string message) => Result<T>.Failure(message, CrmFailureCodes.StoreUnavailable);

    private static string Route(string path)
    {
        var q = path.IndexOf('?', StringComparison.Ordinal);
        return q < 0 ? path : path[..q];
    }
}
