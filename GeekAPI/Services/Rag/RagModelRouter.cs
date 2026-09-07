namespace GeekAPI.Services.Rag;

/// <summary>
/// Phase F — resolve writer model by retrieval family.
/// Long-form defaults to o3; short-form/battlecard/slides use GPT-class unless overridden.
/// </summary>
public static class RagModelRouter
{
    public const string DefaultLongFormModel = "o3";
    public const string DefaultStandardModel = "gpt-4o";

    public static string ResolveModel(RagRetrievalFamily family)
    {
        return family switch
        {
            RagRetrievalFamily.LongForm => EnvOr("GEEK_RAG_LONGFORM_MODEL", DefaultLongFormModel),
            RagRetrievalFamily.ShortForm => EnvOr("GEEK_RAG_SHORTFORM_MODEL", DefaultStandardModel),
            RagRetrievalFamily.Battlecard => EnvOr("GEEK_RAG_BATTLECARD_MODEL", DefaultStandardModel),
            RagRetrievalFamily.Slides => EnvOr("GEEK_RAG_SLIDES_MODEL", DefaultStandardModel),
            _ => EnvOr("GEEK_RAG_SHORTFORM_MODEL", DefaultStandardModel),
        };
    }

    public static bool IsReasoningModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return false;
        var m = model.Trim().ToLowerInvariant();
        return m.StartsWith("o1", StringComparison.Ordinal)
               || m.StartsWith("o3", StringComparison.Ordinal)
               || m.StartsWith("o4", StringComparison.Ordinal);
    }

    private static string EnvOr(string key, string fallback)
    {
        var v = Environment.GetEnvironmentVariable(key)?.Trim();
        return string.IsNullOrWhiteSpace(v) ? fallback : v;
    }
}
