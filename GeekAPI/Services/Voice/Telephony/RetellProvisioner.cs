using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GeekApplication.Results;

namespace GeekAPI.Services.Voice.Telephony;

/// <summary>Why the Retell side could not be set up. The controller turns the code into a status; the wording is for people and never decides one.</summary>
public static class RetellFailureCodes
{
    public const string NotConfigured = "retell_not_configured";
    public const string ProvisioningFailed = "retell_provisioning_failed";
}

/// <summary>What Retell now holds for a tenant: its language model, its agent, and its number imported and bound to that agent.</summary>
public sealed record ProvisionedRetell(string LlmId, string AgentId, string PhoneNumber);

/// <summary>What a tenant's agent says and how it sounds: the first words, the prompt, and Retell's voice id.</summary>
public sealed record RetellAgentScript(string Greeting, string Prompt, string VoiceId);

/// <summary>
/// Sets up a tenant's Retell side (docs/Geek-Receptionist.md section 5, items 3 and 4): a Retell LLM that runs the model named by
/// RETELL_LLM_MODEL at temperature 0.2 with the tenant's prompt and the end_call tool, an agent on that LLM whose webhook is the
/// tenant's Retell route, and the tenant's Twilio number imported over its SIP trunk with that agent answering inbound calls.
/// Every call is made once. When a step fails, one attempt is made to delete what this call created, and the failure names the step.
/// A resource that could not be deleted is logged by its id at Error so it can be deleted by hand. Logs carry ids, steps and
/// statuses, never a key, a password, a prompt or a body.
/// </summary>
public sealed class RetellProvisioner
{
    public const double Temperature = 0.2;

    private const string ApiBase = "https://api.retellai.com/";

    private readonly HttpClient _http;
    private readonly RetellOptions _options;
    private readonly ILogger<RetellProvisioner> _logger;

    public RetellProvisioner(HttpClient http, RetellOptions options, ILogger<RetellProvisioner> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
    }

    /// <summary>The variables a signup needs, so activation can refuse before it creates anything.</summary>
    public IReadOnlyList<string> Missing() => _options.Missing();

    public async Task<Result<ProvisionedRetell>> ProvisionAsync(Guid tenantId, RetellAgentScript script, ProvisionedTelephony telephony, CancellationToken ct = default)
    {
        var missing = _options.Missing();
        if (missing.Count > 0)
        {
            _logger.LogError("Retell cannot be provisioned for tenant {TenantId}: {Variables} not set.", tenantId, string.Join(", ", missing));
            return Result<ProvisionedRetell>.Failure("Retell is not configured.", RetellFailureCodes.NotConfigured);
        }

        var llm = await PostAsync("create the language model", tenantId, "create-retell-llm", new
        {
            model_config = new { type = "cascading", primary = new { model = _options.LlmModel }, temperature = Temperature },
            general_prompt = script.Prompt,
            begin_message = script.Greeting,
            start_speaker = "agent",
            general_tools = new[] { new { type = "end_call", name = "end_call", description = "End the call with the caller." } },
        }, ct).ConfigureAwait(false);
        if (!llm.IsSuccess) return Failed(llm);
        string? llmId;
        using (var doc = llm.Value!) llmId = Text(doc, "llm_id");
        if (llmId is null) return Missing(tenantId, "the language model");

        var agent = await PostAsync("create the agent", tenantId, "create-agent", new
        {
            response_engine = new { type = "retell-llm", llm_id = llmId },
            voice_id = script.VoiceId,
            agent_name = $"Geek Receptionist {tenantId:N}",
            webhook_url = $"{_options.PublicBaseUrl}/api/receptionist/retell/webhook/{tenantId}",
        }, ct).ConfigureAwait(false);
        if (!agent.IsSuccess) return await UndoAsync(tenantId, new ProvisionedRetell(llmId, "", ""), Failed(agent), ct).ConfigureAwait(false);
        string? agentId;
        using (var doc = agent.Value!) agentId = Text(doc, "agent_id");
        if (agentId is null) return await UndoAsync(tenantId, new ProvisionedRetell(llmId, "", ""), Missing(tenantId, "the agent"), ct).ConfigureAwait(false);

        var number = await PostAsync("import the phone number", tenantId, "import-phone-number", new
        {
            phone_number = telephony.PhoneNumber,
            termination_uri = telephony.TerminationUri,
            sip_trunk_auth_username = telephony.SipUsername,
            sip_trunk_auth_password = telephony.SipPassword,
            inbound_agents = new[] { new { agent_id = agentId, weight = 1 } },
            nickname = $"Geek Receptionist {tenantId:N}",
        }, ct).ConfigureAwait(false);
        if (!number.IsSuccess) return await UndoAsync(tenantId, new ProvisionedRetell(llmId, agentId, ""), Failed(number), ct).ConfigureAwait(false);
        string? imported;
        using (var doc = number.Value!) imported = Text(doc, "phone_number");
        if (imported is null) return await UndoAsync(tenantId, new ProvisionedRetell(llmId, agentId, ""), Missing(tenantId, "the imported number"), ct).ConfigureAwait(false);

        return Result<ProvisionedRetell>.Success(new ProvisionedRetell(llmId, agentId, imported));
    }

    /// <summary>
    /// One attempt to delete each thing that exists, the number first, then the agent, then the LLM (Retell refuses to delete an LLM an
    /// agent still uses). Returns whether everything that exists was deleted. A failure is logged at Error with the id, so it can be
    /// deleted by hand. A field that is empty was never created.
    /// </summary>
    public async Task<bool> AbandonAsync(ProvisionedRetell created, Guid tenantId, CancellationToken ct = default)
    {
        var all = true;
        if (created.PhoneNumber.Length > 0)
            all &= await DeleteAsync("delete the imported number", tenantId, $"delete-phone-number/{Uri.EscapeDataString(created.PhoneNumber)}", created.PhoneNumber, ct).ConfigureAwait(false);
        if (created.AgentId.Length > 0)
            all &= await DeleteAsync("delete the agent", tenantId, $"delete-agent/{Uri.EscapeDataString(created.AgentId)}", created.AgentId, ct).ConfigureAwait(false);
        if (created.LlmId.Length > 0)
            all &= await DeleteAsync("delete the language model", tenantId, $"delete-retell-llm/{Uri.EscapeDataString(created.LlmId)}", created.LlmId, ct).ConfigureAwait(false);
        return all;
    }

    private async Task<Result<ProvisionedRetell>> UndoAsync(Guid tenantId, ProvisionedRetell created, Result<ProvisionedRetell> failure, CancellationToken ct)
    {
        await AbandonAsync(created, tenantId, ct).ConfigureAwait(false);
        return failure;
    }

    private async Task<bool> DeleteAsync(string step, Guid tenantId, string route, string id, CancellationToken ct)
    {
        try
        {
            using var request = Request(HttpMethod.Delete, route);
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode) return true;
            _logger.LogError("Retell answered {Status} when asked to {Step} for tenant {TenantId}; {ResourceId} must be removed by hand.",
                (int)response.StatusCode, step, tenantId, id);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Retell was unreachable when asked to {Step} for tenant {TenantId}; {ResourceId} must be removed by hand.", step, tenantId, id);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogError("Retell timed out when asked to {Step} for tenant {TenantId}; {ResourceId} must be removed by hand.", step, tenantId, id);
        }

        return false;
    }

    /// <summary>One JSON POST. A success is the parsed body, which the caller disposes; every failure is a failed result naming the step.</summary>
    private async Task<Result<JsonDocument>> PostAsync(string step, Guid tenantId, string route, object body, CancellationToken ct)
    {
        try
        {
            using var request = Request(HttpMethod.Post, route);
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Retell answered {Status} when asked to {Step} for tenant {TenantId}.", (int)response.StatusCode, step, tenantId);
                return Failure(step);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return Result<JsonDocument>.Success(await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false));
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Retell was unreachable when asked to {Step} for tenant {TenantId}.", step, tenantId);
            return Failure(step);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogError("Retell timed out when asked to {Step} for tenant {TenantId}.", step, tenantId);
            return Failure(step);
        }
        catch (JsonException)
        {
            _logger.LogError("Retell answered a body that did not parse when asked to {Step} for tenant {TenantId}.", step, tenantId);
            return Failure(step);
        }
    }

    private HttpRequestMessage Request(HttpMethod method, string route)
    {
        var request = new HttpRequestMessage(method, ApiBase + route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        return request;
    }

    private Result<ProvisionedRetell> Missing(Guid tenantId, string what)
    {
        _logger.LogError("Retell answered {What} without the fields documented for it, for tenant {TenantId}.", what, tenantId);
        return Result<ProvisionedRetell>.Failure($"Retell did not return {what}.", RetellFailureCodes.ProvisioningFailed);
    }

    private static Result<JsonDocument> Failure(string step) =>
        Result<JsonDocument>.Failure($"Retell could not {step}.", RetellFailureCodes.ProvisioningFailed);

    private static Result<ProvisionedRetell> Failed(Result<JsonDocument> failure) =>
        Result<ProvisionedRetell>.Failure(failure.Error!, failure.ErrorCode!);

    private static string? Text(JsonDocument doc, string name) =>
        doc.RootElement.ValueKind == JsonValueKind.Object
        && doc.RootElement.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;
}
