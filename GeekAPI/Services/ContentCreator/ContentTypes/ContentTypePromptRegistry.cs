namespace GeekAPI.Services.ContentCreator.ContentTypes;

/// <summary>
/// Resolves a content type to the prompt set that defines it.
///
/// The property worth having: a type is implemented exactly when a set is registered for it. Today
/// "implemented" is asserted separately, by a hardcoded DisabledContentTypes array of thirteen
/// strings plus a `default:` dispatch branch that exists only so an unimplemented type does not
/// error. Those are two places claiming the same fact, and they can disagree. Once every type
/// resolves through here, they collapse into this lookup -- and writing a prompt set becomes
/// literally the act of enabling a type.
///
/// Registered today: Pillar, Blog, Tool. Deliberately not the disabled types (Jeff, 2026-09-23:
/// "disabled types do not need to be included initially, because we may discover or learn the best
/// way to accomplish the goal") -- prove the shape on the three live ones first.
/// </summary>
public interface IContentTypePromptRegistry
{
    /// <summary>The set for this type, or null when nothing implements it.</summary>
    IContentTypePrompts? Find(string? contentType);
}

public sealed class ContentTypePromptRegistry : IContentTypePromptRegistry
{
    private readonly Dictionary<string, IContentTypePrompts> _byKey;

    public ContentTypePromptRegistry(IEnumerable<IContentTypePrompts> sets)
    {
        _byKey = sets.ToDictionary(s => Normalize(s.Key), StringComparer.Ordinal);
    }

    public IContentTypePrompts? Find(string? contentType) =>
        string.IsNullOrWhiteSpace(contentType)
            ? null
            : _byKey.GetValueOrDefault(Normalize(contentType));

    /// <summary>
    /// Letters only, lowercased -- the same normalisation the dispatch already applies, so
    /// "tech-article", "techArticle" and "Tech Article" reach one key, and "aiTool"/"tool" are not
    /// two different types to register.
    /// </summary>
    private static string Normalize(string value) =>
        new(value.Where(char.IsLetter).Select(char.ToLowerInvariant).ToArray());
}
