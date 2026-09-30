using System;
using System.Net.Http;
using System.Text.Json;
using GeekAPI.Middleware;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace GeekBackend.Tests;

/// <summary>
/// An unhandled exception used to reach the client as a 500 with an empty body, and three crawls
/// were purged on it — acumatica 752 pages, avalara 255, parseur 185. The crawler's post-mortem
/// records the entire response it was given: <c>"→ 500: "</c>. The cause (every GeekRepository call
/// failing with <c>Connection reset by peer</c>) was only recoverable from Railway's logs two days
/// later.
/// </summary>
public class UnhandledExceptionResponseTests
{
    [Fact]
    public void AnUnreachableDependencyIsReportedAsBadGateway()
    {
        // This is the exception that actually fired: HttpRequestException wrapping
        // SocketException(104). Reporting it as 500 claims the fault is in GeekAPI and makes it
        // indistinguishable from a bug in the handler.
        var ex = new HttpRequestException(
            "An error occurred while sending the request.",
            new System.IO.IOException("Unable to read data from the transport connection: "
                + "Connection reset by peer."));

        Assert.Equal(StatusCodes.Status502BadGateway, UnhandledExceptionResponse.StatusFor(ex));
    }

    [Fact]
    public void AnythingElseStaysFiveHundred()
    {
        Assert.Equal(
            StatusCodes.Status500InternalServerError,
            UnhandledExceptionResponse.StatusFor(new InvalidOperationException("boom")));
    }

    [Fact]
    public void TheBodyNamesTheExceptionTypeAndCarriesATraceId()
    {
        var ex = new HttpRequestException("An error occurred while sending the request.");
        var json = UnhandledExceptionResponse.BodyFor(
            ex, "0HNOTE2FHATK6:00000001", StatusCodes.Status502BadGateway, includeDetail: false);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // The crawler's allowlist reads `title`/`type`/`traceId` to tell a GeekAPI error apart from
        // a platform proxy page. A body with none of those is indistinguishable from the edge's.
        Assert.Equal("https://geekatyourspot.com/errors/unhandled", root.GetProperty("type").GetString());
        Assert.Equal(502, root.GetProperty("status").GetInt32());
        Assert.Equal("0HNOTE2FHATK6:00000001", root.GetProperty("traceId").GetString());
        Assert.Equal(
            "System.Net.Http.HttpRequestException",
            root.GetProperty("exceptionType").GetString());
        Assert.Equal(
            "A service this request depends on could not be reached.",
            root.GetProperty("title").GetString());
    }

    [Fact]
    public void TheMessageIsWithheldInProductionAndKeptOutsideIt()
    {
        // An exception message on this boundary can carry a connection string or an internal
        // hostname. The type name and traceId locate the logged exception without returning either.
        var ex = new InvalidOperationException(
            "mongodb://user:secret@internal-host:27017/geek_crawler");

        using var hidden = JsonDocument.Parse(UnhandledExceptionResponse.BodyFor(
            ex, "trace-1", StatusCodes.Status500InternalServerError, includeDetail: false));
        Assert.Equal(JsonValueKind.Null, hidden.RootElement.GetProperty("detail").ValueKind);

        using var shown = JsonDocument.Parse(UnhandledExceptionResponse.BodyFor(
            ex, "trace-1", StatusCodes.Status500InternalServerError, includeDetail: true));
        Assert.Contains("internal-host", shown.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public void TheBodyIsNeverEmpty()
    {
        // The defect itself: an empty body is the one response that tells the client nothing, and
        // the client is the machine still holding the pages.
        foreach (var ex in new Exception[]
        {
            new HttpRequestException("x"),
            new InvalidOperationException("y"),
            new TimeoutException(),
        })
        {
            var status = UnhandledExceptionResponse.StatusFor(ex);
            var body = UnhandledExceptionResponse.BodyFor(ex, "t", status, includeDetail: false);
            Assert.False(string.IsNullOrWhiteSpace(body));
        }
    }
}
