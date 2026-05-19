using GeekApplication.Interfaces.Seo;
using GeekApplication.Models.Seo;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace GeekAPI.Hubs;

public sealed class SeoContentScoringHub(IContentScoringService scoring, IHttpContextAccessor httpContext) : Hub
{
    public async Task JoinDocument(string documentId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"doc:{documentId}");
    }

    public async Task ContentChanged(string documentId, string contentHtml, string targetKeyword)
    {
        if (!Guid.TryParse(documentId, out var docId))
            return;

        var userId = GetUserId();
        if (userId == Guid.Empty)
        {
            await Clients.Caller.SendAsync("ScoreError", new { message = "Not authenticated. Set NEXT_PUBLIC_DEV_USER_ID or sign in." });
            return;
        }

        var result = await scoring.ProcessContentChangedAsync(userId, docId, contentHtml, targetKeyword);

        if (!result.IsSuccess)
        {
            await Clients.Caller.SendAsync("ScoreError", new { message = result.Error });
            return;
        }

        if (result.Value?.PendingReason is not null)
        {
            await Clients.Caller.SendAsync("ScorePending", new { reason = result.Value.PendingReason });
            return;
        }

        if (result.Value?.ScoreUpdate is not null)
            await Clients.Group($"doc:{documentId}").SendAsync("ScoreUpdate", result.Value.ScoreUpdate);
    }

    public async Task KeywordChanged(
        string documentId,
        string newKeyword,
        string location,
        string contentHtml)
    {
        if (!Guid.TryParse(documentId, out var docId))
            return;

        var userId = GetUserId();
        if (userId == Guid.Empty)
        {
            await Clients.Caller.SendAsync("ScoreError", new { message = "Not authenticated." });
            return;
        }

        await Clients.Caller.SendAsync("BenchmarkRefreshing", new { keyword = newKeyword, location });

        var result = await scoring.ProcessKeywordChangedAsync(
            userId, docId, contentHtml, newKeyword, location);

        if (!result.IsSuccess)
        {
            await Clients.Caller.SendAsync("ScoreError", new { message = result.Error });
            return;
        }

        if (result.Value?.PendingReason is not null)
        {
            await Clients.Caller.SendAsync("ScorePending", new { reason = result.Value.PendingReason });
            return;
        }

        if (result.Value?.ScoreUpdate is not null)
            await Clients.Group($"doc:{documentId}").SendAsync("ScoreUpdate", result.Value.ScoreUpdate);
    }

    private Guid GetUserId()
    {
        var sub = Context.User?.FindFirst("sub")?.Value ?? Context.UserIdentifier;
        if (Guid.TryParse(sub, out var id) && id != Guid.Empty)
            return id;

        var header = httpContext.HttpContext?.Request.Headers["X-User-Id"].ToString();
        if (Guid.TryParse(header, out var fromHeader))
            return fromHeader;

        var accessToken = httpContext.HttpContext?.Request.Query["access_token"].ToString();
        if (Guid.TryParse(accessToken, out var fromQuery))
            return fromQuery;

        return Guid.Empty;
    }
}
