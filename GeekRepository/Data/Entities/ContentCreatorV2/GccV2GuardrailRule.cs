namespace GeekRepository.Data.Entities.ContentCreatorV2;

/// <summary>
/// Admin-editable guardrail rule for Content Creator v2 only. Replaces v1's eight hardcoded
/// <c>ContentGuardrail</c> rules for v2 jobs — v1's static class is never edited.
/// </summary>
public class GccV2GuardrailRule
{
    public Guid Id { get; set; }
    /// <summary>Phrase or regex pattern to match (stored as plain phrase; compiled with word boundaries).</summary>
    public string Pattern { get; set; } = "";
    /// <summary><c>strip</c> | <c>replace</c> | <c>restructure</c></summary>
    public string Action { get; set; } = "strip";
    public string? ReplaceWith { get; set; }
    public bool Enabled { get; set; } = true;
    /// <summary>Optional scope filter (e.g. content type slug). Null = all types.</summary>
    public string? Scope { get; set; }
    public string? ReasonCode { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
