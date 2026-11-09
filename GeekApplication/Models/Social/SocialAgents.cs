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
