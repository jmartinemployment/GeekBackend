using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;

namespace GeekAPI.Services.Voice.Email;

/// <summary>Why an email could not be sent. The code is stable; the wording is for people and never decides anything.</summary>
public static class EmailFailureCodes
{
    public const string EmailNotConfigured = "email_not_configured";
    public const string SendFailed = "email_send_failed";
}

/// <summary>
/// Sends transactional email through Resend's HTTP API (https://resend.com/docs/api-reference/emails/send-email, read
/// 2026-11-19), the same provider the public site already uses. The call is made once; a non-2xx answer, a timeout, an
/// unreachable host, a missing message id or an unparseable body all fail the send with an <c>email_send_failed</c> result.
/// Log lines carry the purpose label, the HTTP status and the provider's message id only — never the recipient, the subject
/// or the body, and never the API key — because a taken message or a per-call summary is call content this platform keeps
/// none of.
/// </summary>
public sealed class ResendEmailSender : IEmailSender
{
    private const string SendUrl = "https://api.resend.com/emails";

    private readonly HttpClient _http;
    private readonly EmailOptions _options;
    private readonly ILogger<ResendEmailSender> _logger;

    public ResendEmailSender(HttpClient http, EmailOptions options, ILogger<ResendEmailSender> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
    }

    public async Task<Result<string>> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var missing = _options.Missing();
        if (missing.Count > 0)
        {
            _logger.LogError("Email ({Purpose}) was not sent: {Variables} not set.", message.Purpose, string.Join(", ", missing));
            return Result<string>.Failure("Email is not configured.", EmailFailureCodes.EmailNotConfigured);
        }

        var payload = new Dictionary<string, object>
        {
            ["from"] = _options.FromHeader(),
            ["to"] = new[] { message.To },
            ["subject"] = message.Subject,
            ["text"] = message.TextBody,
        };
        if (!string.IsNullOrWhiteSpace(message.HtmlBody))
            payload["html"] = message.HtmlBody;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, SendUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Resend answered {Status} when sending the {Purpose} email.", (int)response.StatusCode, message.Purpose);
                return Result<string>.Failure("The email provider refused the message.", EmailFailureCodes.SendFailed);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            var id = doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("id", out var value)
                && value.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(value.GetString())
                    ? value.GetString()
                    : null;
            if (id is null)
            {
                _logger.LogError("Resend accepted the {Purpose} email without returning a message id.", message.Purpose);
                return Result<string>.Failure("The email provider did not confirm the message.", EmailFailureCodes.SendFailed);
            }

            return Result<string>.Success(id);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Resend was unreachable when sending the {Purpose} email.", message.Purpose);
            return Result<string>.Failure("The email provider was unreachable.", EmailFailureCodes.SendFailed);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogError("Resend timed out when sending the {Purpose} email.", message.Purpose);
            return Result<string>.Failure("The email provider timed out.", EmailFailureCodes.SendFailed);
        }
        catch (JsonException)
        {
            _logger.LogError("Resend answered a body that did not parse when sending the {Purpose} email.", message.Purpose);
            return Result<string>.Failure("The email provider answered unexpectedly.", EmailFailureCodes.SendFailed);
        }
    }
}
