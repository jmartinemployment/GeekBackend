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
        return await SendAsync(request, ct).ConfigureAwait(false);
    }

    /// <summary>POST <c>/{version}/{path}</c> with a form body, as the holder of <paramref name="token"/>.</summary>
    public async Task<Result<JsonElement>> PostFormAsync(
        string path, SocialToken token, IEnumerable<KeyValuePair<string, string>> form, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{MetaOptions.GraphVersion}/{path.TrimStart('/')}")
        {
            Content = new FormUrlEncodedContent(form),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Reveal());
        return await SendAsync(request, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// GET without a bearer token, for the calls that prove the app itself (the code and long-lived
    /// token exchanges). The app secret is part of the query those calls need, so the address that
    /// carries it is built by the caller and never logged here.
    /// </summary>
    public async Task<Result<JsonElement>> GetAsAppAsync(
        string path, IEnumerable<KeyValuePair<string, string>> query, CancellationToken ct)
    {
        var address = $"{MetaOptions.GraphVersion}/{path.TrimStart('/')}{QueryString(query)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        return await SendAsync(request, ct).ConfigureAwait(false);
    }

    private async Task<Result<JsonElement>> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return Result<JsonElement>.Failure("Meta could not be reached.", SocialFailureCodes.PlatformUnavailable);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return Result<JsonElement>.Failure("Meta did not answer in time.", SocialFailureCodes.PlatformUnavailable);
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

            return Result<JsonElement>.Failure(DescribeError(root, response.StatusCode), CodeFor(root, response.StatusCode));
        }
    }

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
