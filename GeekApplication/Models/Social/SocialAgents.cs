using System.Text.Json;

namespace GeekApplication.Models.Social;

// What passes between the GeekSocial agents and the orchestrator (plans/geek-social-engagement-agent,
// "Agents and the orchestrator"). Each agent takes one of these and returns one of these; no agent
// holds another agent's type. Plain classes and not records, for the reason in SocialRecords.cs: a
// record prints every member, and these carry other people's comment text.

/// <summary>
/// Where Triage left a comment event. The value is stored on the event and nothing else holds it, so
/// "has this been handled" is a read and not a guess. <see cref="Untriaged"/> is a way to ask the
/// store for the events with no outcome yet; it is never stored.
/// </summary>
public static class TriageOutcomes
{
    /// <summary>Triage decided the comment needs a reply, and a draft for it exists.</summary>
    public const string ReplyNeeded = "reply_needed";

    /// <summary>Triage decided no reply is needed. Final.</summary>
    public const string NoReplyNeeded = "no_reply_needed";

    /// <summary>The platform has no operator clearance to send comment text to Claude, so Triage did not run. An operator action re-runs it.</summary>
    public const string BlockedNoClearance = "blocked_no_clearance";

    /// <summary>Triage could not complete; the event carries a <see cref="SocialFailureCodes"/> value. An operator action re-runs it.</summary>
    public const string Failed = "failed";

    /// <summary>A filter value for the store: comment events with no outcome recorded yet. Not a stored outcome.</summary>
    public const string Untriaged = "untriaged";

    /// <summary>The values the store holds.</summary>
    public static readonly IReadOnlyList<string> All = new[] { ReplyNeeded, NoReplyNeeded, BlockedNoClearance, Failed };

    /// <summary>Every value a caller may ask the store to list by.</summary>
    public static readonly IReadOnlyList<string> Filters = new[] { ReplyNeeded, NoReplyNeeded, BlockedNoClearance, Failed, Untriaged };

    /// <summary>The outcomes an operator action may replace by running Triage again. A decided event is never decided twice.</summary>
    public static readonly IReadOnlyList<string> Rerunnable = new[] { BlockedNoClearance, Failed };

    public static bool IsKnown(string? outcome) => outcome is not null && All.Contains(outcome);

    public static bool IsKnownFilter(string? value) => value is not null && Filters.Contains(value);
}

/// <summary>What Triage is given for one comment: only what it needs to decide what the comment is.</summary>
public sealed class TriageRequest
{
    public string Platform { get; set; } = string.Empty;
    public string AccountDisplayName { get; set; } = string.Empty;

    /// <summary>Another person's comment text. See <see cref="Interfaces.Social.IEngagementTriage"/> for where it may go.</summary>
    public string CommentText { get; set; } = string.Empty;

    /// <summary>The text of the post the comment is under, for context.</summary>
    public string? PostText { get; set; }

    public string? AuthorDisplayName { get; set; }
}

/// <summary>
/// Triage's decision for one comment. <see cref="Category"/> is always a <see cref="ReplyCategories"/>
/// value. <see cref="NeedsHumanReason"/> is set only when a reply is needed and a person must write it
/// rather than the Drafter; it is null when no reply is needed.
/// </summary>
public sealed class TriageResult
{
    public bool NeedsReply { get; set; }
    public string Category { get; set; } = ReplyCategories.Question;
    public string? NeedsHumanReason { get; set; }
}

/// <summary>Records what Triage decided, or why it did not, on one comment event.</summary>
public sealed class RecordTriageOutcomeCommand
{
    /// <summary>A <see cref="TriageOutcomes"/> value.</summary>
    public string Outcome { get; set; } = string.Empty;

    /// <summary>A <see cref="SocialFailureCodes"/> value, required when the outcome is failed and refused otherwise.</summary>
    public string? FailureCode { get; set; }

    public DateTime At { get; set; }
}

/// <summary>Whether the text being checked is a reply to a comment or a post, which differ in what a link may be.</summary>
public static class OutboundTextKinds
{
    public const string Reply = "reply";
    public const string Post = "post";

    public static readonly IReadOnlyList<string> All = new[] { Reply, Post };
}

/// <summary>
/// The rules the Reviewer refuses text for. The store keeps the names of the rules a refused draft
/// broke, and the database checks every one against this list, so a rule the code does not know cannot
/// be recorded.
/// </summary>
public static class ReviewRules
{
    public const string EmDash = "em_dash";
    public const string Smb = "smb";
    public const string HomeAddress = "home_address";
    public const string PhoneNumber = "phone_number";
    public const string EmailAddress = "email_address";
    public const string UnapprovedLink = "unapproved_link";
    public const string TooLong = "too_long";

    public static readonly IReadOnlyList<string> All = new[]
    {
        EmDash, Smb, HomeAddress, PhoneNumber, EmailAddress, UnapprovedLink, TooLong,
    };

    public static bool IsKnown(string? rule) => rule is not null && All.Contains(rule);
}

/// <summary>
/// The longest text the Reviewer lets through. These are house limits and not a platform's: none of
/// LinkedIn's Comments API page, Instagram's content-publishing guide or Meta's Graph comment reference
/// states a maximum for a comment or reply (read 2026-11-10), so no platform figure is quoted here. The
/// drafter and the Post Writer are told the same number the Reviewer enforces, so a draft over it is the
/// model ignoring its instructions and is refused, never trimmed.
/// </summary>
public static class ReviewLimits
{
    public const int ReplyCharacters = 1000;
    public const int PostCharacters = 2000;

    /// <summary>The limit for an <see cref="OutboundTextKinds"/> value; an unknown kind is a bug and throws.</summary>
    public static int For(string kind) => kind switch
    {
        OutboundTextKinds.Reply => ReplyCharacters,
        OutboundTextKinds.Post => PostCharacters,
        _ => throw new ArgumentException($"Unknown outbound text kind '{kind}'.", nameof(kind)),
    };
}

/// <summary>Text about to leave the system, for the Reviewer to check.</summary>
public sealed class OutboundText
{
    /// <summary>An <see cref="OutboundTextKinds"/> value.</summary>
    public string Kind { get; set; } = OutboundTextKinds.Reply;

    public string Platform { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;

    /// <summary>The links the text may contain. Any other link is refused.</summary>
    public List<string> ApprovedLinks { get; set; } = new();
}

/// <summary>
/// The Reviewer's verdict. A refusal is a successful review with <see cref="Passed"/> false and the
/// <see cref="ReviewRules"/> it broke; a failed Result means the Reviewer could not run.
/// </summary>
public sealed class OutboundTextReview
{
    public bool Passed { get; set; }
    public List<string> Violations { get; set; } = new();
}

/// <summary>What the Post Writer is given for one article or link.</summary>
public sealed class PostCopyRequest
{
    public string ArticleUrl { get; set; } = string.Empty;
    public string ArticleTitle { get; set; } = string.Empty;
    public string ArticleSummary { get; set; } = string.Empty;

    /// <summary>The <see cref="SocialPlatforms"/> values to write for.</summary>
    public List<string> Platforms { get; set; } = new();

    /// <summary>A publicly reachable image. Instagram posts need one, and the Post Writer never makes one.</summary>
    public string? ImageUrl { get; set; }
}

/// <summary>The copy for one platform, ready to show a person in the post form.</summary>
public sealed class PostCopy
{
    public string Platform { get; set; } = string.Empty;
    public SocialPostRequest Post { get; set; } = new();
}

/// <summary>The Post Writer's answer: one <see cref="PostCopy"/> for each platform asked for.</summary>
public sealed class PostCopySuggestion
{
    public List<PostCopy> Copies { get; set; } = new();
}

/// <summary>
/// One call to the language model behind <see cref="Interfaces.Social.ISocialModelClient"/>: a system
/// prompt, one user turn, and the structured answer the model must give, described by a JSON schema.
/// Each provider's client enforces the schema its own way (Anthropic a forced tool, OpenAI and Groq a
/// strict response format, Gemini a response schema, Perplexity a response format). The user turn may
/// carry another person's comment text, so this type is never logged and its <see cref="ToString"/>
/// names only the purpose.
/// </summary>
public sealed class SocialModelRequest
{
    /// <summary>A short name for the step making the call (for example "triage"), used in log lines in place of any text.</summary>
    public string Purpose { get; set; } = string.Empty;

    public string SystemPrompt { get; set; } = string.Empty;
    public string UserMessage { get; set; } = string.Empty;

    /// <summary>
    /// The answer's name, letters and digits only (for example <c>writeReply</c>), because every
    /// provider accepts that: Anthropic and OpenAI allow more, Perplexity allows only alphanumerics.
    /// </summary>
    public string AnswerName { get; set; } = string.Empty;

    public string AnswerDescription { get; set; } = string.Empty;

    /// <summary>
    /// The JSON schema the answer must match. It stays flat and minimal so every provider enforces it
    /// natively: one object of string, number, integer, boolean and enum fields, every property
    /// required, no additional properties (<c>SocialAnswerSchemaTests</c> checks each one in use).
    /// </summary>
    public string AnswerSchemaJson { get; set; } = string.Empty;

    public int MaxOutputTokens { get; set; }

    public override string ToString() => $"SocialModelRequest({Purpose})";
}

/// <summary>
/// What a model call returned: the structured answer, and who answered and at what size. The token
/// counts are what the per-reply AI budget and the cost per reply are measured from.
/// </summary>
public sealed class SocialModelAnswer
{
    public JsonElement Answer { get; init; }

    /// <summary>One of <see cref="SocialModelProviders.All"/>.</summary>
    public string Provider { get; init; } = string.Empty;

    /// <summary>The model the provider says served the call, or the configured model when it does not say.</summary>
    public string Model { get; init; } = string.Empty;

    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
}

/// <summary>
/// The AI providers GeekSocial can call. The provider is chosen by <c>LlmProviders__DefaultProvider</c>
/// on GeekAPI, the setting Content Creator also reads; GeekSocial accepts every value Content Creator
/// does (OpenAi, Anthropic, Groq, in any case) plus Gemini and Perplexity. No provider is a default.
/// </summary>
public static class SocialModelProviders
{
    public const string Anthropic = "anthropic";
    public const string OpenAi = "openai";
    public const string Gemini = "gemini";
    public const string Perplexity = "perplexity";
    public const string Groq = "groq";

    public static readonly IReadOnlyList<string> All = new[] { Anthropic, OpenAi, Gemini, Perplexity, Groq };

    /// <summary>The provider a setting names, ignoring case, or null when it names none of them.</summary>
    public static string? Parse(string? value)
    {
        var trimmed = value?.Trim().ToLowerInvariant();
        return trimmed is not null && All.Contains(trimmed) ? trimmed : null;
    }

    /// <summary>The name a person sees on the screens.</summary>
    public static string DisplayName(string provider) => provider switch
    {
        Anthropic => "Anthropic",
        OpenAi => "OpenAI",
        Gemini => "Google Gemini",
        Perplexity => "Perplexity",
        Groq => "Groq",
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Not an AI provider GeekSocial knows."),
    };

    /// <summary>The section of <c>LlmProviders__{Section}__*</c> that holds the provider's key and model.</summary>
    public static string SettingsSection(string provider) => provider switch
    {
        Anthropic => "Anthropic",
        OpenAi => "OpenAi",
        Gemini => "Gemini",
        Perplexity => "Perplexity",
        Groq => "Groq",
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Not an AI provider GeekSocial knows."),
    };
}
