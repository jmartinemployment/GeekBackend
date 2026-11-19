using GeekApplication.Models.Voice;
using GeekApplication.Results;

namespace GeekApplication.Interfaces.Voice;

/// <summary>
/// Sends one transactional email and reports whether it left. The success value is the provider's message id; a failure
/// carries a code — <c>email_not_configured</c> when the provider is not set up, <c>email_send_failed</c> when the provider
/// refused or was unreachable. The sender never retains or logs the recipient, the subject or the body — only the purpose
/// label, the provider's status and that id — so an email carrying a per-call summary or a taken message leaks nothing to
/// the logs. The caller decides what a failure means: a per-call summary that cannot be sent is the occasion for the
/// dead-letter path, and a dead-letter email that cannot be sent is an operator-visible error, never a swallowed one.
/// </summary>
public interface IEmailSender
{
    Task<Result<string>> SendAsync(EmailMessage message, CancellationToken ct = default);
}
