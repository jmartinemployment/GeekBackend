using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Infrastructure;

/// <summary>
/// Diagnostic containment for optimistic concurrency conflicts. Does not by itself
/// retry workers or force terminal states — callers must handle 409 explicitly.
/// </summary>
public sealed class DbUpdateConcurrencyExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not DbUpdateConcurrencyException)
            return false;

        httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "Concurrency conflict",
            Detail = "The resource was modified concurrently. Re-read and retry, or use the force-terminal path for ingestion failures.",
            Type = "https://httpstatuses.com/409",
        }, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
