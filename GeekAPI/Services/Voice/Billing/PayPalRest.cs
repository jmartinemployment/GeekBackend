using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GeekApplication.Results;

using static GeekAPI.Services.Voice.Billing.PayPalJson;

namespace GeekAPI.Services.Voice.Billing;

/// <summary>
/// PayPal's REST API as every Geek-Receptionist billing step calls it, written once: an access token from the app credentials, and a
/// request sent once. A PayPal 404 on a GET is the caller's not-found code; a refused key, a 5xx, a timeout and a body that is not a
/// JSON object are billing unavailable, logged with the status and the route and never with a body, an id or a secret. The logger is
/// the caller's, so each line is filed under the step that made the call.
/// </summary>
public sealed class PayPalRest(HttpClient http, PayPalOptions options, ILogger logger)
{
    public async Task<Result<string>> GetAccessTokenAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Url("v1/oauth2/token"))
        {
            Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("grant_type", "client_credentials")]),
        };
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.ClientId}:{options.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

        var response = await SendAsync(request, "POST v1/oauth2/token", ct).ConfigureAwait(false);
        if (response is null) return Unavailable<string>("PayPal could not be reached.");
        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("PayPal answered {Status} for POST v1/oauth2/token.", (int)response.StatusCode);
                return Unavailable<string>($"PayPal answered {(int)response.StatusCode}.");
            }

            try
            {
                using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct).ConfigureAwait(false);
                var accessToken = Text(doc.RootElement, "access_token");
                if (!string.IsNullOrWhiteSpace(accessToken)) return Result<string>.Success(accessToken);
            }
            catch (JsonException)
            {
            }

            logger.LogError("PayPal answered a token body that was not usable for POST v1/oauth2/token.");
            return Unavailable<string>("PayPal answered a body that was not usable.");
        }
    }

    /// <summary>GET a PayPal resource. A 404 is <paramref name="notFoundCode"/>; any other failure is billing unavailable.</summary>
    public async Task<Result<JsonDocument>> GetJsonAsync(string path, string accessToken, string notFoundCode, string notFoundMessage, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Url(path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var route = $"GET {RouteOf(path)}";
        var response = await SendAsync(request, route, ct).ConfigureAwait(false);
        if (response is null) return Unavailable<JsonDocument>("PayPal could not be reached.");
        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
                return Result<JsonDocument>.Failure(notFoundMessage, notFoundCode);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("PayPal answered {Status} for {Route}.", (int)response.StatusCode, route);
                return Unavailable<JsonDocument>($"PayPal answered {(int)response.StatusCode}.");
            }

            try
            {
                var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct).ConfigureAwait(false);
                if (doc.RootElement.ValueKind == JsonValueKind.Object) return Result<JsonDocument>.Success(doc);
                doc.Dispose();
            }
            catch (JsonException)
            {
            }

            logger.LogError("PayPal answered a body that did not parse for {Route}.", route);
            return Unavailable<JsonDocument>("PayPal answered a body that did not parse.");
        }
    }

    public async Task<HttpResponseMessage?> SendAsync(HttpRequestMessage request, string route, CancellationToken ct)
    {
        try
        {
            return await http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "PayPal unreachable for {Route}.", route);
            return null;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogError("PayPal timed out for {Route}.", route);
            return null;
        }
    }

    public Uri Url(string path) => new($"{options.BaseUrl}/{path}");

    /// <summary>The route in a log line: the resource type, never the id the visitor sent.</summary>
    public static string RouteOf(string path)
    {
        var parts = path.Split('/');
        return parts.Length >= 3 ? $"{parts[0]}/{parts[1]}/{parts[2]}/{{id}}" : path;
    }

    public static Result<T> Unavailable<T>(string message) => Result<T>.Failure(message, BillingFailureCodes.BillingUnavailable);
}

/// <summary>Reading PayPal's JSON: a field that is missing or of another kind is null, never a default.</summary>
public static class PayPalJson
{
    public static string? Text(JsonElement element, params string[] path)
    {
        var value = Walk(element, path);
        return value is { ValueKind: JsonValueKind.String } ? value.Value.GetString() : null;
    }

    public static decimal? Number(JsonElement element, params string[] path)
    {
        var value = Walk(element, path);
        if (value is null) return null;
        return value.Value.ValueKind switch
        {
            JsonValueKind.String when decimal.TryParse(value.Value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var s) => s,
            JsonValueKind.Number when value.Value.TryGetDecimal(out var n) => n,
            _ => null,
        };
    }

    public static DateTimeOffset? Time(JsonElement element, params string[] path)
    {
        var text = Text(element, path);
        return text is not null && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t.ToUniversalTime() : null;
    }

    private static JsonElement? Walk(JsonElement element, string[] path)
    {
        var current = element;
        foreach (var name in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out current)) return null;
        }

        return current;
    }
}
