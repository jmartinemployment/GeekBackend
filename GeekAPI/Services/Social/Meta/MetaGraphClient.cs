using System.Net;
using System.Text.Json;
using GeekApplication.Models.Social;
using GeekApplication.Results;

namespace GeekAPI.Services.Social.Meta;

/// <summary>
/// One HTTP call to Meta's Graph API. The token travels in the Authorization header and never in the
/// address, so it cannot land in a request log. A failure is a failed Result carrying a
/// <see cref="SocialFailureCodes"/> value; its message names Meta's error code and trace id and
/// nothing Meta put in the body, because a body can carry a person's name or a comment.
/// </summary>
public sealed class MetaGraphClient
{
    /// <summary>The name of the HttpClient registration that points at graph.facebook.com.</summary>
    public const string HttpClientName = "MetaGraph";

    private readonly HttpClient _http;

    public MetaGraphClient(HttpClient http) => _http = http;

    /// <summary>GET <c>/{version}/{path}</c> with the query given, as the holder of <paramref name="token"/>.</summary>
    public async Task<Result<JsonElement>> GetAsync(
        string path, SocialToken token, IEnumerable<KeyValuePair<string, string>> query, CancellationToken ct)
    {
        var address = $"{MetaOptions.GraphVersion}/{path.TrimStart('/')}{QueryString(query)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Reveal());
        return await SendAsync(request, write: false, ct).ConfigureAwait(false);
    }

    /// <summary>POST <c>/{version}/{path}</c> with a form body, as the holder of <paramref name="token"/>, for a call that only sets something up (a webhook subscription). A failure is read the way a read's is.</summary>
    public async Task<Result<JsonElement>> PostFormAsync(
        string path, SocialToken token, IEnumerable<KeyValuePair<string, string>> form, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{MetaOptions.GraphVersion}/{path.TrimStart('/')}")
        {
            Content = new FormUrlEncodedContent(form),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Reveal());
        return await SendAsync(request, write: false, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// POST that creates something people see (a post, a reply, a like). It is one attempt. Any answer
    /// that does not prove Meta refused the call is outcome_unknown: a timeout, a connection that broke
    /// after the request left, a 5xx, Meta's own "unknown error", or a 2xx whose body cannot be read. The
    /// caller records it and never sends it again; the operator checks the Page.
    /// </summary>
    public async Task<Result<JsonElement>> PostWriteAsync(
        string path, SocialToken token, IEnumerable<KeyValuePair<string, string>> form, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{MetaOptions.GraphVersion}/{path.TrimStart('/')}")
        {
            Content = new FormUrlEncodedContent(form),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Reveal());
        return await SendAsync(request, write: true, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// POST <c>/{version}/{path}</c> with a form body and no bearer token, for the calls that prove the
    /// app itself (the code and long-lived token exchanges). The app secret and the token being traded
    /// travel in the body, never in the address, so no request log can carry them.
    /// </summary>
    public async Task<Result<JsonElement>> PostAsAppAsync(
        string path, IEnumerable<KeyValuePair<string, string>> form, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{MetaOptions.GraphVersion}/{path.TrimStart('/')}")
        {
            Content = new FormUrlEncodedContent(form),
        };
        return await SendAsync(request, write: false, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// GET with the app's own token (<c>{app-id}|{app-secret}</c>) in the Authorization header, for
    /// <c>debug_token</c>. The app token never appears in the address.
    /// </summary>
    public async Task<Result<JsonElement>> GetWithAppTokenAsync(
        string path, string appToken, IEnumerable<KeyValuePair<string, string>> query, CancellationToken ct)
    {
        var address = $"{MetaOptions.GraphVersion}/{path.TrimStart('/')}{QueryString(query)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", appToken);
        return await SendAsync(request, write: false, ct).ConfigureAwait(false);
    }

    private const string UnknownAdvice = " It may have been applied: check the Page before doing it again.";

    private async Task<Result<JsonElement>> SendAsync(HttpRequestMessage request, bool write, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            if (write && !NeverLeft(ex))
                return Result<JsonElement>.Failure("The connection to Meta broke during the call." + UnknownAdvice, SocialFailureCodes.OutcomeUnknown);
            return Result<JsonElement>.Failure("Meta could not be reached.", SocialFailureCodes.PlatformUnavailable);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return write
                ? Result<JsonElement>.Failure("Meta did not answer in time." + UnknownAdvice, SocialFailureCodes.OutcomeUnknown)
                : Result<JsonElement>.Failure("Meta did not answer in time.", SocialFailureCodes.PlatformUnavailable);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            JsonElement root;
            try
            {
                using var document = JsonDocument.Parse(body);
                root = document.RootElement.Clone();
            }
            catch (JsonException)
            {
                if (write && (response.IsSuccessStatusCode || response.StatusCode >= HttpStatusCode.InternalServerError))
                    return Result<JsonElement>.Failure(
                        $"Meta answered HTTP {(int)response.StatusCode} with a body that is not JSON." + UnknownAdvice,
                        SocialFailureCodes.OutcomeUnknown);

                return response.IsSuccessStatusCode
                    ? Result<JsonElement>.Failure("Meta answered with a body that is not JSON.", SocialFailureCodes.PlatformRejected)
                    : Result<JsonElement>.Failure(
                        $"Meta answered HTTP {(int)response.StatusCode}.",
                        response.StatusCode >= HttpStatusCode.InternalServerError
                            ? SocialFailureCodes.PlatformUnavailable
                            : SocialFailureCodes.PlatformRejected);
            }

            if (response.IsSuccessStatusCode && !HasError(root))
                return Result<JsonElement>.Success(root);

            var code = CodeFor(root, response.StatusCode);
            if (write && code == SocialFailureCodes.PlatformUnavailable)
                return Result<JsonElement>.Failure(DescribeError(root, response.StatusCode) + UnknownAdvice, SocialFailureCodes.OutcomeUnknown);

            return Result<JsonElement>.Failure(DescribeError(root, response.StatusCode), code);
        }
    }

    /// <summary>True when the request cannot have reached Meta: the name did not resolve or no connection was made.</summary>
    private static bool NeverLeft(HttpRequestException ex) => ex.HttpRequestError is
        HttpRequestError.NameResolutionError
        or HttpRequestError.ConnectionError
        or HttpRequestError.SecureConnectionError
        or HttpRequestError.ProxyTunnelError;

    private static bool HasError(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object;

    /// <summary>Maps Meta's error code to the stable failure the contracts speak. The documented codes only; anything else is Meta's refusal.</summary>
    public static string CodeFor(JsonElement root, HttpStatusCode status)
    {
        if (HasError(root))
        {
            var error = root.GetProperty("error");
            var code = IntOf(error, "code");
            var subcode = IntOf(error, "error_subcode");
            switch (code)
            {
                case 190:
                case 102:
                    return SocialFailureCodes.TokenInvalid;
                case 4:
                case 17:
                case 32:
                case 613:
                    return SocialFailureCodes.RateLimited;
                case >= 80000 and <= 80014:
                    return SocialFailureCodes.RateLimited;
                case 10:
                case >= 200 and <= 299:
                    return SocialFailureCodes.Forbidden;
                case 100 when subcode == 33:
                    return SocialFailureCodes.NotFound;
                case 1:
                case 2:
                    return SocialFailureCodes.PlatformUnavailable;
            }
        }

        return status >= HttpStatusCode.InternalServerError
            ? SocialFailureCodes.PlatformUnavailable
            : SocialFailureCodes.PlatformRejected;
    }

    private static string DescribeError(JsonElement root, HttpStatusCode status)
    {
        if (!HasError(root))
            return $"Meta answered HTTP {(int)status}.";

        var error = root.GetProperty("error");
        var code = IntOf(error, "code");
        var subcode = IntOf(error, "error_subcode");
        var trace = error.TryGetProperty("fbtrace_id", out var traceElement) && traceElement.ValueKind == JsonValueKind.String
            ? traceElement.GetString()
            : null;
        var parts = new List<string> { $"HTTP {(int)status}", $"error {code?.ToString() ?? "unknown"}" };
        if (subcode is not null) parts.Add($"subcode {subcode}");
        if (!string.IsNullOrEmpty(trace)) parts.Add($"trace {trace}");
        return $"Meta refused the call ({string.Join(", ", parts)}).";
    }

    private static int? IntOf(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;

    private static string QueryString(IEnumerable<KeyValuePair<string, string>> query)
    {
        var pairs = query.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}").ToList();
        return pairs.Count == 0 ? string.Empty : "?" + string.Join('&', pairs);
    }
}
