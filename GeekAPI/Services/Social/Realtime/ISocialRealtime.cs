namespace GeekAPI.Services.Social.Realtime;

/// <summary>
/// Tells the GeekSocial screen that something changed, over SignalR (/hubs/social-realtime). A message
/// carries ids and states only, never comment or draft text: the screen reads the text through
/// api/social/*. A message that cannot be sent is logged by the implementation and does not undo or
/// fail the change it reports, which is already stored; the screen sees it at its next read.
/// </summary>
public interface ISocialRealtime
{
    Task DraftChangedAsync(Guid draftId, Guid accountId, string status, CancellationToken ct = default);
    Task CommentChangedAsync(Guid eventId, Guid accountId, string outcome, CancellationToken ct = default);
    Task AccountChangedAsync(Guid accountId, string status, CancellationToken ct = default);
}
