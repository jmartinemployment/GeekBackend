using System.Text.Json;
using GeekApplication.Models.Voice;

namespace GeekAPI.Services.Voice.Onboarding;

/// <summary>
/// What a business answered about its receptionist, on one step of the signup form or one save from its dashboard. Every field is
/// optional because a form asks a few things at a time: a field that is null was not asked, and the business's current answer stands.
///
/// <para>
/// A nullable answer is cleared by sending it empty, not by sending null - <see cref="TransferTargetNumber"/> and
/// <see cref="MessageDestinationEmail"/> as <c>""</c>, <see cref="EmergencyKeywords"/> as <c>[]</c>. Null and "none" are different
/// answers, and a form that could not say "none" would have none chosen for it.
/// </para>
///
/// <para>
/// <c>KnowledgeRunId</c> is deliberately not here. It is a crawl, not an answer: the knowledge flow sets it when a business's website
/// has been indexed, and a form field that let anyone write any run id would point a receptionist at another business's pages.
/// </para>
/// </summary>
public sealed record ReceptionistConfigAnswers(
    string? GreetingMessage = null,
    string? SystemPromptTemplate = null,
    JsonElement? BusinessHours = null,
    JsonElement? QualifyingQuestions = null,
    string? TransferMode = null,
    string? TransferTargetNumber = null,
    string? TransferCallerIdMode = null,
    IReadOnlyList<string>? EmergencyKeywords = null,
    bool? RecordingDisclosureEnabled = null,
    bool? MessageEmailEnabled = null,
    string? MessageDestinationEmail = null,
    bool? IncludeTranscript = null,
    bool? NotifyOnAbandonedCalls = null,
    bool? BookingEnabled = null,
    string? ConsentMode = null)
{
    /// <summary>
    /// The settings this answer actually names, by setting name and never by value. It is what a log line can carry: which of its
    /// settings a business changed is operable, and a greeting, a script, a transfer number and a destination address are not.
    /// </summary>
    public IReadOnlyList<string> AnsweredFields
    {
        get
        {
            var answered = new List<string>(15);
            if (GreetingMessage is not null) answered.Add(nameof(GreetingMessage));
            if (SystemPromptTemplate is not null) answered.Add(nameof(SystemPromptTemplate));
            if (BusinessHours is not null) answered.Add(nameof(BusinessHours));
            if (QualifyingQuestions is not null) answered.Add(nameof(QualifyingQuestions));
            if (TransferMode is not null) answered.Add(nameof(TransferMode));
            if (TransferTargetNumber is not null) answered.Add(nameof(TransferTargetNumber));
            if (TransferCallerIdMode is not null) answered.Add(nameof(TransferCallerIdMode));
            if (EmergencyKeywords is not null) answered.Add(nameof(EmergencyKeywords));
            if (RecordingDisclosureEnabled is not null) answered.Add(nameof(RecordingDisclosureEnabled));
            if (MessageEmailEnabled is not null) answered.Add(nameof(MessageEmailEnabled));
            if (MessageDestinationEmail is not null) answered.Add(nameof(MessageDestinationEmail));
            if (IncludeTranscript is not null) answered.Add(nameof(IncludeTranscript));
            if (NotifyOnAbandonedCalls is not null) answered.Add(nameof(NotifyOnAbandonedCalls));
            if (BookingEnabled is not null) answered.Add(nameof(BookingEnabled));
            if (ConsentMode is not null) answered.Add(nameof(ConsentMode));
            return answered;
        }
    }

    /// <summary>False when nothing was answered, which is a form posting an empty step and is refused rather than saved as a no-op.</summary>
    public bool AnswersAnything => AnsweredFields.Count > 0;

    /// <summary>The business's current settings with these answers written over them, and nothing else changed.</summary>
    public VoiceReceptionistConfig AppliedTo(VoiceReceptionistConfig config) => config with
    {
        GreetingMessage = GreetingMessage ?? config.GreetingMessage,
        SystemPromptTemplate = SystemPromptTemplate ?? config.SystemPromptTemplate,
        BusinessHours = BusinessHours ?? config.BusinessHours,
        QualifyingQuestions = QualifyingQuestions ?? config.QualifyingQuestions,
        TransferMode = TransferMode ?? config.TransferMode,
        TransferTargetNumber = TransferTargetNumber is null ? config.TransferTargetNumber : Chosen(TransferTargetNumber),
        TransferCallerIdMode = TransferCallerIdMode ?? config.TransferCallerIdMode,
        EmergencyKeywords = EmergencyKeywords ?? config.EmergencyKeywords,
        RecordingDisclosureEnabled = RecordingDisclosureEnabled ?? config.RecordingDisclosureEnabled,
        MessageEmailEnabled = MessageEmailEnabled ?? config.MessageEmailEnabled,
        MessageDestinationEmail = MessageDestinationEmail is null ? config.MessageDestinationEmail : Chosen(MessageDestinationEmail),
        IncludeTranscript = IncludeTranscript ?? config.IncludeTranscript,
        NotifyOnAbandonedCalls = NotifyOnAbandonedCalls ?? config.NotifyOnAbandonedCalls,
        BookingEnabled = BookingEnabled ?? config.BookingEnabled,
        ConsentMode = ConsentMode ?? config.ConsentMode,
    };

    /// <summary>An answer sent empty is the business choosing "none", stored as null rather than as an empty string.</summary>
    private static string? Chosen(string answer) => string.IsNullOrWhiteSpace(answer) ? null : answer.Trim();
}
