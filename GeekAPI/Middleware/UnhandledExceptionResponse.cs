using System.Net.Http;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace GeekAPI.Middleware;

/// <summary>
/// Turns an unhandled exception into a described failure instead of a blank one.
///
/// Three crawls -- acumatica 752 pages, avalara 255, parseur 185 -- were discarded after
/// <c>POST .../pages/batch</c> answered <c>500</c> with an <b>empty body</b>. The crawler recorded
/// the whole of what it was told: <c>"→ 500: "</c>. Nothing about the cause reached the only machine
/// that still had the pages, so the post-mortem could not say why, and the run was purged on a
/// signal that carried no information.
///
/// The cause was found on 2026-09-30 from Railway's logs, not from the response: at 16:10:56 UTC on
/// 09-28 every GeekRepository call was failing with
/// <c>SocketException (104) Connection reset by peer</c> after ~2.5ms. It hit pages/batch, the RAG
/// index-status webhook and a background job worker within three seconds of each other -- the store
/// was gone, not the request bad. The specific path that threw, an ownership lookup outside its
/// try, was fixed the same evening in f024fe0 (22:29 UTC, six hours after avalara). This exists
/// because the next unhandled exception will be a different one, and a blank 500 will be just as
/// unreadable.
///
/// This is not a fallback and does not salvage anything: the request still fails, the exception is
/// still logged at Error, and nothing is retried or substituted. It changes only what the client is
/// told about a failure that already happened.
/// </summary>
public static class UnhandledExceptionResponse
{
    /// <summary>
    /// The status an unhandled exception should carry.
    ///
    /// <see cref="HttpRequestException"/> means a service this one depends on could not be reached,
    /// which is 502 -- the same answer <c>DenyIfNotOwnedAsync</c> already gives for exactly that
    /// condition on the ingest boundary. Reporting it as 500 claims the fault is here and makes two
    /// different failures indistinguishable to the client.
    /// </summary>
    public static int StatusFor(Exception ex) =>
        ex is HttpRequestException
            ? StatusCodes.Status502BadGateway
            : StatusCodes.Status500InternalServerError;

    /// <summary>
    /// The body, in GeekAPI's own error shape so a client can tell it apart from a proxy page.
    ///
    /// <paramref name="includeDetail"/> is false in production: the exception message can carry
    /// connection strings and internal hostnames. The type name and the traceId are enough to find
    /// the logged exception, and they are safe to return.
    /// </summary>
    public static string BodyFor(Exception ex, string traceId, int status, bool includeDetail)
    {
        var title = status == StatusCodes.Status502BadGateway
            ? "A service this request depends on could not be reached."
            : "The request failed with an unhandled exception.";

        return JsonSerializer.Serialize(new
        {
            type = "https://geekatyourspot.com/errors/unhandled",
            title,
            status,
            traceId,
            exceptionType = ex.GetType().FullName,
            detail = includeDetail ? ex.Message : null,
        });
    }
}
