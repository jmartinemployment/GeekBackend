namespace GeekAPI.Services.Workflow.Services.PromptBuilders;

internal static class PillarPlanMetadataNormalizer
{
    // Normalize commented out — if the title is a question or starts with How, leave it.
    // Prompt says the title must not be a question; do not silently replace it with
    // "{Keyword}: Implementation Guide".
    // public static ArticleMetadataDraft Normalize(ArticleMetadataDraft metadata, string targetKeyword)
    // {
    //     var title = metadata.Title.Trim();
    //     if (title.EndsWith('?') || title.StartsWith("How ", StringComparison.OrdinalIgnoreCase))
    //     {
    //         title = ToDefinitiveTitle(targetKeyword);
    //     }
    //
    //     return metadata with { Title = title };
    // }
    //
    // private static string ToDefinitiveTitle(string targetKeyword) =>
    //     $"{char.ToUpper(targetKeyword[0])}{targetKeyword[1..]}: Implementation Guide";
}
