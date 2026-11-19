namespace GeekApplication.Models.Voice;

/// <summary>
/// One transactional email GeekAPI sends an owner: a message the receptionist took, a per-call summary, a payload a storage
/// connector could not take (the dead-letter path), or a sign-in code. <see cref="Purpose"/> is a short, fixed label —
/// never caller content — that an email sender may log so an operator can see which send failed; <see cref="To"/>,
/// <see cref="Subject"/>, <see cref="TextBody"/> and <see cref="HtmlBody"/> are never logged, because a summary or a taken
/// message is call content and this platform retains none of it.
/// </summary>
public sealed record EmailMessage(string To, string Subject, string TextBody, string? HtmlBody, string Purpose);
