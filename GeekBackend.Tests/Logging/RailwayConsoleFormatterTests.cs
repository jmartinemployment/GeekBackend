using System.Text.Json;
using GeekAPI.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GeekBackend.Tests.Logging;

/// <summary>
/// Every log line is one JSON object with the rendered text in a top-level "message" and the level in
/// "level" -- the shape Railway's log search reads. The stock JSON writer left "message" empty, so a
/// search for a line found nothing while the line was there (2026-10-06).
/// </summary>
public sealed class RailwayConsoleFormatterTests
{
    [Fact]
    public void The_line_carries_the_rendered_message_the_level_the_category_and_the_state()
    {
        var formatter = new RailwayConsoleFormatter();
        var writer = new StringWriter();
        var state = new List<KeyValuePair<string, object?>>
        {
            new("CreateId", Guid.Parse("11111111-1111-1111-1111-111111111111")),
            new("PageCount", 102),
            new("{OriginalFormat}", "Grounding resolved for create {CreateId}: {PageCount} partner"),
        };
        var entry = new LogEntry<List<KeyValuePair<string, object?>>>(
            LogLevel.Warning, "GeekAPI.Services.ContentCreator.GccGroundingResolver", new EventId(7), state,
            new InvalidOperationException("boom"),
            (s, _) => "Grounding resolved for create 11111111-1111-1111-1111-111111111111: 102 partner");

        formatter.Write(in entry, scopeProvider: null, writer);

        using var json = JsonDocument.Parse(writer.ToString().TrimEnd());
        var root = json.RootElement;
        Assert.Equal("warn", root.GetProperty("level").GetString());
        Assert.Equal("Grounding resolved for create 11111111-1111-1111-1111-111111111111: 102 partner", root.GetProperty("message").GetString());
        Assert.Equal("GeekAPI.Services.ContentCreator.GccGroundingResolver", root.GetProperty("category").GetString());
        Assert.Equal(7, root.GetProperty("eventId").GetInt32());
        Assert.Contains("boom", root.GetProperty("exception").GetString(), StringComparison.Ordinal);
        Assert.Equal(102, root.GetProperty("state").GetProperty("PageCount").GetInt32());
        Assert.False(root.GetProperty("state").TryGetProperty("{OriginalFormat}", out _));
        Assert.Single(writer.ToString().TrimEnd().Split('\n'));
    }

    [Fact]
    public void An_entry_with_no_message_and_no_exception_writes_nothing()
    {
        var formatter = new RailwayConsoleFormatter();
        var writer = new StringWriter();
        var entry = new LogEntry<string>(LogLevel.Information, "c", new EventId(0), "", null, (_, _) => "");

        formatter.Write(in entry, scopeProvider: null, writer);

        Assert.Equal(string.Empty, writer.ToString());
    }
}
