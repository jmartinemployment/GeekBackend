using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GeekApplication.Results;

namespace GeekAPI.Services.Voice.Telephony;

/// <summary>Why telephony could not be set up. The controller turns the code into a status; the wording is for people and never decides one.</summary>
public static class TelephonyFailureCodes
{
    public const string AreaCodeInvalid = "area_code_invalid";
    public const string TelephonyNotConfigured = "telephony_not_configured";
    public const string ProvisioningFailed = "telephony_provisioning_failed";
}

/// <summary>
/// What Twilio now holds for a tenant. <see cref="AuthToken"/> is the subaccount's token in plain text, for the caller to encrypt
/// before it is stored; <see cref="SipUsername"/> and <see cref="SipPassword"/> are the trunk's termination credentials, which
/// Retell needs once when it imports the number and nothing stores.
/// </summary>
public sealed record ProvisionedTelephony(
    string SubaccountSid,
    string AuthToken,
    string TrunkSid,
    string TerminationUri,
    string SipUsername,
    string SipPassword,
    string PhoneNumber,
    string PhoneSid);

/// <summary>
/// Sets up a tenant's Twilio side (docs/Geek-Receptionist.md section 5, item 3): an isolated subaccount, an Elastic SIP trunk on it
/// whose origination address is Retell and whose termination is guarded by credentials, and a local US number in the tenant's area
/// code assigned to that trunk. A call to the number goes from Twilio straight to Retell, so no voice webhook is set. For a tenant
/// with SMS notifications on, the number's SMS webhook is GeekAPI's inbound text route for that tenant.
/// Every call is made once. When a step fails, one attempt is made to close the subaccount, which releases the number and everything
/// else on it, and the failure names the step. The clean-up result is logged: a subaccount that could not be closed is logged by its
/// SID at Error so it can be closed by hand. Logs carry ids, steps and statuses, never a token, a password or a body.
/// </summary>
public sealed class TwilioProvisioner
{
    public const string RetellOriginationUri = "sip:sip.retellai.com";

    private const string ApiBase = "https://api.twilio.com/2010-04-01/";
    private const string TrunkingBase = "https://trunking.twilio.com/v1/";

    private readonly HttpClient _http;
    private readonly TwilioOptions _options;
    private readonly ILogger<TwilioProvisioner> _logger;

    public TwilioProvisioner(HttpClient http, TwilioOptions options, ILogger<TwilioProvisioner> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
    }

    /// <summary>The variables a signup of this kind needs, so activation can refuse before it creates anything.</summary>
    public IReadOnlyList<string> Missing(bool smsEnabled) => _options.Missing(smsEnabled);

    public async Task<Result<ProvisionedTelephony>> ProvisionAsync(Guid tenantId, string? areaCode, bool smsEnabled, CancellationToken ct = default)
    {
        if (areaCode is null || areaCode.Length != 3 || !areaCode.All(char.IsAsciiDigit))
            return Result<ProvisionedTelephony>.Failure("The preferred area code must be three digits.", TelephonyFailureCodes.AreaCodeInvalid);

        var missing = _options.Missing(smsEnabled);
        if (missing.Count > 0)
        {
            _logger.LogError("Telephony cannot be provisioned for tenant {TenantId}: {Variables} not set.", tenantId, string.Join(", ", missing));
            return Result<ProvisionedTelephony>.Failure("Telephony is not configured.", TelephonyFailureCodes.TelephonyNotConfigured);
        }

        var master = (_options.MasterAccountSid!, _options.MasterAuthToken!);

        var account = await PostAsync("create the subaccount", tenantId, $"{ApiBase}Accounts.json", master,
            new() { ["FriendlyName"] = $"Geek Receptionist {tenantId:N}" }, ct).ConfigureAwait(false);
        if (!account.IsSuccess) return Failed(account);
        string? subSid, subToken;
        using (var doc = account.Value!)
        {
            subSid = Text(doc, "sid");
            subToken = Text(doc, "auth_token");
        }
        if (subSid is null || subToken is null)
        {
            _logger.LogError("Twilio answered the subaccount without a sid or token for tenant {TenantId}.", tenantId);
            return Result<ProvisionedTelephony>.Failure("Twilio did not return the new subaccount.", TelephonyFailureCodes.ProvisioningFailed);
        }

        var sub = (subSid, subToken);
        var result = await BuildAsync(tenantId, areaCode, smsEnabled, sub, ct).ConfigureAwait(false);
        if (result.IsSuccess) return result;

        await AbandonAsync(subSid, tenantId, ct).ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// One attempt to close a subaccount, which releases its number and everything else on it. Used when this class fails partway and
    /// by activation when a later step (the Retell agent, the stored record) fails. Returns whether it closed; a failure is logged at
    /// Error with the SID, so it can be closed by hand.
    /// </summary>
    public Task<bool> AbandonAsync(string subaccountSid, Guid tenantId, CancellationToken ct = default) =>
        CloseAsync(subaccountSid, tenantId, "after a failed activation", ct);

    /// <summary>
    /// One attempt to close a cancelled tenant's subaccount, which releases its number so it bills nothing more. Returns whether it
    /// closed; a failure is logged at Error with the SID, so it can be closed by hand.
    /// </summary>
    public Task<bool> ReleaseAsync(string subaccountSid, Guid tenantId, CancellationToken ct = default) =>
        CloseAsync(subaccountSid, tenantId, "because the tenant was cancelled", ct);

    private async Task<bool> CloseAsync(string subaccountSid, Guid tenantId, string occasion, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.MasterAccountSid) || string.IsNullOrWhiteSpace(_options.MasterAuthToken))
        {
            _logger.LogError("Twilio subaccount {SubaccountSid} for tenant {TenantId} was left open: the parent credentials are not set.", subaccountSid, tenantId);
            return false;
        }

        var closed = await PostAsync("close the subaccount", tenantId, $"{ApiBase}Accounts/{Uri.EscapeDataString(subaccountSid)}.json",
            (_options.MasterAccountSid, _options.MasterAuthToken), new() { ["Status"] = "closed" }, ct).ConfigureAwait(false);
        if (!closed.IsSuccess)
        {
            _logger.LogError("Twilio subaccount {SubaccountSid} for tenant {TenantId} could not be closed and must be closed by hand.", subaccountSid, tenantId);
            return false;
        }

        closed.Value!.Dispose();
        _logger.LogInformation("Twilio subaccount {SubaccountSid} for tenant {TenantId} was closed {Occasion}.", subaccountSid, tenantId, occasion);
        return true;
    }

    private async Task<Result<ProvisionedTelephony>> BuildAsync(Guid tenantId, string areaCode, bool smsEnabled, (string Sid, string Token) sub, CancellationToken ct)
    {
        var username = "gr" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
        var password = NewPassword();
        var domain = $"gr-{tenantId:N}.pstn.twilio.com";

        var list = await PostAsync("create the credential list", tenantId, $"{ApiBase}Accounts/{sub.Sid}/SIP/CredentialLists.json", sub,
            new() { ["FriendlyName"] = "Retell termination" }, ct).ConfigureAwait(false);
        if (!list.IsSuccess) return Failed(list);
        string? listSid;
        using (var doc = list.Value!) listSid = Text(doc, "sid");
        if (listSid is null) return Missing("the credential list");

        var credential = await PostAsync("add the SIP credential", tenantId, $"{ApiBase}Accounts/{sub.Sid}/SIP/CredentialLists/{listSid}/Credentials.json", sub,
            new() { ["Username"] = username, ["Password"] = password }, ct).ConfigureAwait(false);
        if (!credential.IsSuccess) return Failed(credential);
        credential.Value!.Dispose();

        var trunk = await PostAsync("create the SIP trunk", tenantId, $"{TrunkingBase}Trunks", sub,
            new() { ["FriendlyName"] = "Retell", ["DomainName"] = domain }, ct).ConfigureAwait(false);
        if (!trunk.IsSuccess) return Failed(trunk);
        string? trunkSid, terminationUri;
        using (var doc = trunk.Value!)
        {
            trunkSid = Text(doc, "sid");
            terminationUri = Text(doc, "domain_name");
        }
        if (trunkSid is null || terminationUri is null) return Missing("the SIP trunk");

        var origination = await PostAsync("set the trunk's origination address", tenantId, $"{TrunkingBase}Trunks/{trunkSid}/OriginationUrls", sub,
            new() { ["FriendlyName"] = "Retell", ["SipUrl"] = RetellOriginationUri, ["Weight"] = "1", ["Priority"] = "1", ["Enabled"] = "true" }, ct).ConfigureAwait(false);
        if (!origination.IsSuccess) return Failed(origination);
        origination.Value!.Dispose();

        var attached = await PostAsync("guard the trunk's termination", tenantId, $"{TrunkingBase}Trunks/{trunkSid}/CredentialLists", sub,
            new() { ["CredentialListSid"] = listSid }, ct).ConfigureAwait(false);
        if (!attached.IsSuccess) return Failed(attached);
        attached.Value!.Dispose();

        var purchase = new Dictionary<string, string>
        {
            ["AreaCode"] = areaCode,
            ["TrunkSid"] = trunkSid,
            ["FriendlyName"] = "Geek Receptionist",
        };
        if (smsEnabled)
        {
            purchase["SmsUrl"] = $"{_options.PublicBaseUrl}/api/receptionist/twilio/sms/incoming/{tenantId}";
            purchase["SmsMethod"] = "POST";
        }

        var number = await PostAsync("buy the phone number", tenantId, $"{ApiBase}Accounts/{sub.Sid}/IncomingPhoneNumbers.json", sub, purchase, ct).ConfigureAwait(false);
        if (!number.IsSuccess) return Failed(number);
        string? phoneSid, phoneNumber;
        using (var doc = number.Value!)
        {
            phoneSid = Text(doc, "sid");
            phoneNumber = Text(doc, "phone_number");
        }
        if (phoneSid is null || phoneNumber is null) return Missing("the phone number");

        return Result<ProvisionedTelephony>.Success(new ProvisionedTelephony(sub.Sid, sub.Token, trunkSid, terminationUri, username, password, phoneNumber, phoneSid));

        Result<ProvisionedTelephony> Missing(string what)
        {
            _logger.LogError("Twilio answered {What} without the fields documented for it, for tenant {TenantId}.", what, tenantId);
            return Result<ProvisionedTelephony>.Failure($"Twilio did not return {what}.", TelephonyFailureCodes.ProvisioningFailed);
        }
    }

    /// <summary>One form POST. A success is the parsed body, which the caller disposes; every failure is a failed result naming the step.</summary>
    private async Task<Result<JsonDocument>> PostAsync(string step, Guid tenantId, string url, (string Sid, string Token) credentials, Dictionary<string, string> form, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) };
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{credentials.Sid}:{credentials.Token}")));

            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Twilio answered {Status} (code {TwilioCode}) when asked to {Step} for tenant {TenantId}.",
                    (int)response.StatusCode, await ErrorCodeAsync(response, ct).ConfigureAwait(false), step, tenantId);
                return Failure(step);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return Result<JsonDocument>.Success(await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false));
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Twilio was unreachable when asked to {Step} for tenant {TenantId}.", step, tenantId);
            return Failure(step);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogError("Twilio timed out when asked to {Step} for tenant {TenantId}.", step, tenantId);
            return Failure(step);
        }
        catch (JsonException)
        {
            _logger.LogError("Twilio answered a body that did not parse when asked to {Step} for tenant {TenantId}.", step, tenantId);
            return Failure(step);
        }
    }

    /// <summary>Twilio's numeric error code from a refusal's body, or null. Only the number is read; the message is never logged.</summary>
    private static async Task<int?> ErrorCodeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            return doc.RootElement.TryGetProperty("code", out var code) && code.TryGetInt32(out var value) ? value : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Result<JsonDocument> Failure(string step) =>
        Result<JsonDocument>.Failure($"Twilio could not {step}.", TelephonyFailureCodes.ProvisioningFailed);

    private static Result<ProvisionedTelephony> Failed(Result<JsonDocument> failure) =>
        Result<ProvisionedTelephony>.Failure(failure.Error!, failure.ErrorCode!);

    private static string? Text(JsonDocument doc, string name) =>
        doc.RootElement.ValueKind == JsonValueKind.Object
        && doc.RootElement.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    /// <summary>Twilio wants twelve or more characters with a digit and mixed case; this is forty, with one of each guaranteed and the order shuffled.</summary>
    private static string NewPassword()
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var chars = RandomNumberGenerator.GetItems<char>(alphabet, 40);
        chars[0] = RandomNumberGenerator.GetItems<char>("ABCDEFGHIJKLMNOPQRSTUVWXYZ", 1)[0];
        chars[1] = RandomNumberGenerator.GetItems<char>("abcdefghijklmnopqrstuvwxyz", 1)[0];
        chars[2] = RandomNumberGenerator.GetItems<char>("0123456789", 1)[0];
        RandomNumberGenerator.Shuffle<char>(chars);
        return new string(chars);
    }
}
