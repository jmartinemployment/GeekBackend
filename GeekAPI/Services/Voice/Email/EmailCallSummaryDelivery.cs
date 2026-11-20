using System.Net;
using System.Text;
using GeekApplication.Interfaces.Receptionist;
using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;

namespace GeekAPI.Services.Voice.Email;

/// <summary>
/// Delivers a finished call's record to the business by email, the destination the plan names for taking messages
/// (plans/client-and-app-plan.md, "Take messages" and "Instant notifications").
///
/// Because the platform stores nothing of a call, this email is the owner's only lasting record of it, so it is written to be read on
/// a phone: what happened first, then how to reach the caller back, then the detail. A call whose summary could not be written is still
/// sent, saying so plainly, because a lead that is lost silently is worse than one delivered with an honest gap.
///
/// Everything in it came from a caller or from a model, so every value is HTML-encoded into the HTML part: a caller who says their name
/// is "&lt;script&gt;" gets their name shown, not run. The text part is sent as well, so a client that shows no HTML still shows the record.
/// Nothing here is logged: the log line names the outcome and the provider's message id only.
/// </summary>
public sealed class EmailCallSummaryDelivery(IEmailSender sender, ILogger<EmailCallSummaryDelivery> logger) : ICallSummaryDelivery
{
    public const string Purpose = "per-call-summary";

    public async Task<Result<string>> DeliverAsync(FinishedCall call, CancellationToken ct = default)
    {
        var message = new EmailMessage(call.Destination, Subject(call), TextBody(call), HtmlBody(call), Purpose);

        var sent = await sender.SendAsync(message, ct).ConfigureAwait(false);
        if (!sent.IsSuccess)
        {
            logger.LogError("A call record could not be emailed ({Code}).", sent.ErrorCode);
            return sent;
        }

        logger.LogInformation("A call record was emailed ({Kind}).", call.Kind);
        return sent;
    }

    internal static string Subject(FinishedCall call)
    {
        var who = string.IsNullOrWhiteSpace(call.CallerNumber) ? "caller withheld" : call.CallerNumber;
        var when = call.EndedAt.ToString("MMM d, h:mm tt", System.Globalization.CultureInfo.InvariantCulture);
        var what = call.Kind switch
        {
            FinishedCallKind.Summarized => call.Summary!.Outcome,
            FinishedCallKind.SummaryUnavailable => "Call record — summary unavailable",
            _ => "Call with no caller speech",
        };
        return $"[{what}] {who} — {when}";
    }

    private static string Duration(TimeSpan duration) =>
        duration < TimeSpan.Zero ? "unknown"
        : duration.TotalMinutes >= 1 ? $"{(int)duration.TotalMinutes} min {duration.Seconds} sec"
        : $"{duration.Seconds} sec";

    internal static string TextBody(FinishedCall call)
    {
        var text = new StringBuilder();
        text.AppendLine(Subject(call)).AppendLine();

        switch (call.Kind)
        {
            case FinishedCallKind.Summarized:
                text.AppendLine(call.Summary!.Summary).AppendLine();
                if (call.Summary.Fields.Count > 0)
                {
                    text.AppendLine("What the caller gave:");
                    foreach (var field in call.Summary.Fields) text.AppendLine($"  {field.Label}: {field.Value}");
                    text.AppendLine();
                }
                if (call.Summary.Actions.Count > 0)
                {
                    text.AppendLine("What your receptionist did:");
                    foreach (var action in call.Summary.Actions) text.AppendLine($"  - {action}");
                    text.AppendLine();
                }
                if (call.Summary.Unanswered.Count > 0)
                {
                    text.AppendLine("It could not answer:");
                    foreach (var question in call.Summary.Unanswered) text.AppendLine($"  - {question}");
                    text.AppendLine();
                }
                break;

            case FinishedCallKind.SummaryUnavailable:
                text.AppendLine("A caller spoke to your receptionist, and the summary of the call could not be written.")
                    .AppendLine("The call itself is below if you asked for transcripts; otherwise please call the number above back.")
                    .AppendLine();
                break;

            default:
                text.AppendLine("A call connected and the caller said nothing of substance before it ended.").AppendLine();
                break;
        }

        if (call.Transcript is { Count: > 0 })
        {
            text.AppendLine("The call:");
            foreach (var turn in call.Transcript) text.AppendLine($"  {Speaker(turn)}: {turn.Text}");
            text.AppendLine();
        }

        text.AppendLine($"{call.BusinessName} - {call.EndedAt:f} - lasted {Duration(call.Duration)}.");
        text.AppendLine("Your receptionist keeps no copy of this call. This email is the record.");
        return text.ToString();
    }

    private static string Speaker(ReceptionistTurn turn) => turn.Speaker switch
    {
        ReceptionistSpeaker.Caller => "Caller",
        ReceptionistSpeaker.ToolCall => $"Receptionist used {turn.ToolName}",
        ReceptionistSpeaker.ToolResult => $"{turn.ToolName} answered",
        _ => "Receptionist",
    };

    internal static string HtmlBody(FinishedCall call)
    {
        var html = new StringBuilder();
        html.Append("<div style=\"font-family:system-ui,-apple-system,sans-serif;max-width:600px;margin:0 auto;padding:24px 16px;color:#111\">");

        html.Append("<p style=\"margin:0 0 4px;font-size:13px;color:#666\">").Append(E(call.BusinessName)).Append("</p>");
        html.Append("<h1 style=\"margin:0 0 16px;font-size:20px;line-height:1.3\">").Append(E(Headline(call))).Append("</h1>");

        if (!string.IsNullOrWhiteSpace(call.CallerNumber))
        {
            html.Append("<p style=\"margin:0 0 20px;font-size:17px\"><a href=\"tel:").Append(E(call.CallerNumber))
                .Append("\" style=\"color:#0b57d0;text-decoration:none;font-weight:600\">").Append(E(call.CallerNumber)).Append("</a></p>");
        }

        if (call.Kind == FinishedCallKind.Summarized)
        {
            html.Append("<p style=\"margin:0 0 20px;font-size:15px;line-height:1.5\">").Append(E(call.Summary!.Summary)).Append("</p>");
            Rows(html, "What the caller gave", call.Summary.Fields);
            List(html, "What your receptionist did", call.Summary.Actions);
            List(html, "It could not answer", call.Summary.Unanswered);
        }
        else if (call.Kind == FinishedCallKind.SummaryUnavailable)
        {
            html.Append("<p style=\"margin:0 0 20px;padding:12px 14px;background:#fff4e5;border-radius:6px;font-size:15px;line-height:1.5\">")
                .Append("A caller spoke to your receptionist, and the summary of this call could not be written. ")
                .Append("Nothing has been invented in its place. Please call the number above back.</p>");
        }
        else
        {
            html.Append("<p style=\"margin:0 0 20px;font-size:15px;line-height:1.5\">")
                .Append("A call connected and the caller said nothing of substance before it ended.</p>");
        }

        if (call.Transcript is { Count: > 0 })
        {
            html.Append("<h2 style=\"margin:24px 0 8px;font-size:13px;text-transform:uppercase;letter-spacing:.06em;color:#666\">The call</h2>");
            html.Append("<div style=\"font-size:14px;line-height:1.6\">");
            foreach (var turn in call.Transcript)
            {
                html.Append("<p style=\"margin:0 0 6px\"><strong>").Append(E(Speaker(turn))).Append(":</strong> ")
                    .Append(E(turn.Text)).Append("</p>");
            }
            html.Append("</div>");
        }

        html.Append("<hr style=\"border:none;border-top:1px solid #eee;margin:24px 0 12px\" />");
        html.Append("<p style=\"margin:0;font-size:12px;color:#888\">")
            .Append(E(call.EndedAt.ToString("f", System.Globalization.CultureInfo.InvariantCulture)))
            .Append(" &middot; lasted ").Append(E(Duration(call.Duration))).Append("<br />")
            .Append("Your receptionist keeps no copy of this call. This email is the record.</p>");
        html.Append("</div>");
        return html.ToString();
    }

    private static string Headline(FinishedCall call) => call.Kind switch
    {
        FinishedCallKind.Summarized => call.Summary!.Outcome,
        FinishedCallKind.SummaryUnavailable => "Call record — summary unavailable",
        _ => "Call with no caller speech",
    };

    private static void Rows(StringBuilder html, string title, IReadOnlyList<ReceptionistSummaryField> fields)
    {
        if (fields.Count == 0) return;
        html.Append("<h2 style=\"margin:24px 0 8px;font-size:13px;text-transform:uppercase;letter-spacing:.06em;color:#666\">")
            .Append(E(title)).Append("</h2><table style=\"width:100%;border-collapse:collapse;font-size:15px\">");
        foreach (var field in fields)
        {
            html.Append("<tr><td style=\"padding:6px 12px 6px 0;color:#666;vertical-align:top;white-space:nowrap\">")
                .Append(E(field.Label)).Append("</td><td style=\"padding:6px 0\">").Append(E(field.Value)).Append("</td></tr>");
        }
        html.Append("</table>");
    }

    private static void List(StringBuilder html, string title, IReadOnlyList<string> items)
    {
        if (items.Count == 0) return;
        html.Append("<h2 style=\"margin:24px 0 8px;font-size:13px;text-transform:uppercase;letter-spacing:.06em;color:#666\">")
            .Append(E(title)).Append("</h2><ul style=\"margin:0;padding-left:20px;font-size:15px;line-height:1.6\">");
        foreach (var item in items) html.Append("<li>").Append(E(item)).Append("</li>");
        html.Append("</ul>");
    }

    /// <summary>Everything in this email came from a caller or a model, so nothing is put into the HTML unencoded.</summary>
    private static string E(string value) => WebUtility.HtmlEncode(value);
}
