namespace GeekAPI.Services.Workflow.Services;

/// <summary>
/// How a tool-page run ended. Tool slots come from the crawl -- HierarchyToolsByHeading, else the
/// crawl tool list -- so the only way to have none is for the crawl to name none.
///
/// <para>
/// Three of the four values used to describe a pillar's own Tools section, back when tool pages were
/// read out of one. Nothing reads a pillar for tools now, and the pillar is not allowed to carry a
/// Tools H2 at all (Jeff, 2026-09-26: "shouldn't be a dedicated Tool section"), so naming that
/// section in an outcome was reporting on something that cannot exist.
/// </para>
/// </summary>
public enum ToolGenerationOutcome
{
    Success,
    NoToolsInCrawl
}
