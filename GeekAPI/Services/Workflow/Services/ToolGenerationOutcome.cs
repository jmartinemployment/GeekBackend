namespace GeekAPI.Services.Workflow.Services;

public enum ToolGenerationOutcome
{
    Success,
    NoToolsSection,
    ToolsSectionNotFoundInBody,
    ToolsSectionEmpty
}

public sealed record ToolExtractionResult(
    ToolGenerationOutcome Outcome,
    IReadOnlyList<SchemaBuilders.SoftwareApplicationDescriptor> Applications);
