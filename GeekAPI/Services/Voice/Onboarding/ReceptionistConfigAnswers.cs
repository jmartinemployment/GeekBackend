using System.Text.Json;
using GeekApplication.Models.Voice;
using GeekApplication.Results;

namespace GeekAPI.Services.Voice.Onboarding;

/// <summary>
/// What a business told the form about itself, from which its receptionist's prompt is assembled
/// (<see cref="ReceptionistDefaults.PromptFrom"/>). The prompt is a document and not a setting, so the form asks for its parts rather
/// than handing the business a text box holding somebody else's sales script to edit.
///
/// <para>
/// Every part but <see cref="WhatTheBusinessDoes"/> may be left unanswered, and an unanswered part is left out of the prompt. The
/// business's name, state and industry are not asked for here: they are on the tenant row from the business step.
/// </para>
/// </summary>
/// <param name="ReceptionistName">What it calls itself. Blank is the question's other answer - just "the AI receptionist".</param>
/// <param name="WhatTheBusinessDoes">What it sells or does. Required: a receptionist that cannot say this can answer nothing.</param>
/// <param name="WhoItServes">The customers or the area it serves.</param>
/// <param name="HowItPrices">What it charges, or that a person will quote it.</param>
/// <param name="WhatToDoWithACaller">What the business wants the receptionist to do on a call.</param>
public sealed record ReceptionistKnowledgeAnswers(
    string? ReceptionistName = null,
    string? WhatTheBusinessDoes = null,
    string? WhoItServes = null,
    string? HowItPrices = null,
    string? WhatToDoWithACaller = null);

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
/// <b><see cref="BookingEnabled"/> and <see cref="BookingRequested"/> are not two names for one setting.</b> The first is whether a
/// call may write to the calendar, and it is refused without a calendar that works. The second is the business saying it wants
/// appointments, which is what the signup form can ask: no business has a calendar during signup, because connecting one needs the
/// owner's token and the owner's login is the last step. The form answers the second; the dashboard turns the first on once there is a
/// calendar to turn it on against. A form that sent the first would be refused for an honest answer.
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
    bool? BookingRequested = null,
    string? ConsentMode = null,
    ReceptionistKnowledgeAnswers? Knowledge = null)
{
    /// <summary>
    /// Both answers for one setting. <see cref="Knowledge"/> is assembled into <see cref="SystemPromptTemplate"/>, so sending the two
    /// together asks for one prompt to be written twice and is refused rather than silently resolved in some order.
    /// </summary>
    public bool AnswersThePromptTwice => Knowledge is not null && SystemPromptTemplate is not null;

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
            if (BookingRequested is not null) answered.Add(nameof(BookingRequested));
            if (ConsentMode is not null) answered.Add(nameof(ConsentMode));
            // Named as the setting it becomes, because that is the setting this answer changes.
            if (Knowledge is not null && SystemPromptTemplate is null) answered.Add(nameof(SystemPromptTemplate));
            return answered;
        }
    }

    /// <summary>False when nothing was answered, which is a form posting an empty step and is refused rather than saved as a no-op.</summary>
    public bool AnswersAnything => AnsweredFields.Count > 0;

    /// <summary>
    /// These answers with the two personal details among them sealed, ready to be written over a config. Two of the fifteen are a
    /// person's details rather than a behaviour - the number a caller is handed to and the address a message is sent to - and both are
    /// stored as ciphertext, so they are checked and sealed here, before anything is merged or sent. A number that is not a number and
    /// an address that is not an address are refused rather than stored, and a key that cannot seal is reported: there is no path from
    /// this form to a personal detail written in the clear.
    /// </summary>
    public Result<SealedReceptionistConfigAnswers> Seal(VoiceCredentialProtector protector)
    {
        string? transferCipher = null;
        if (!string.IsNullOrWhiteSpace(TransferTargetNumber))
        {
            // The business's own number, typed the way people write one, as the signup's contact phone already allows. An E.164
            // number is taken as it stands, so a number this product stored before is still a valid answer.
            var dialable = UsPhone.ToE164(TransferTargetNumber) ?? TransferTargetNumber.Trim();
            var sealedNumber = VoicePersonalDetails.SealPhone(
                protector,
                dialable,
                VoiceSecretPurposes.ConfigTransferTargetNumber,
                "Enter the US phone number a caller should be transferred to, ten digits, such as (561) 555-0101.");
            if (!sealedNumber.IsSuccess)
                return Result<SealedReceptionistConfigAnswers>.Failure(sealedNumber.Error!, sealedNumber.ErrorCode!);
            transferCipher = sealedNumber.Value;
        }

        string? emailCipher = null;
        if (!string.IsNullOrWhiteSpace(MessageDestinationEmail))
        {
            var sealedEmail = VoicePersonalDetails.SealEmail(
                protector,
                MessageDestinationEmail.Trim(),
                VoiceSecretPurposes.ConfigMessageDestinationEmail,
                "Enter the email address a message about a call should be sent to.");
            if (!sealedEmail.IsSuccess)
                return Result<SealedReceptionistConfigAnswers>.Failure(sealedEmail.Error!, sealedEmail.ErrorCode!);
            emailCipher = sealedEmail.Value;
        }

        return Result<SealedReceptionistConfigAnswers>.Success(new SealedReceptionistConfigAnswers(this, transferCipher, emailCipher));
    }
}

/// <summary>
/// The answers with their two personal details already sealed, which is the only form that may be written to a config. The names say
/// ciphertext because that is what they hold; the record they came from holds what the business typed.
/// </summary>
/// <param name="Answered">The answers as given, which is what says whether a question was asked at all.</param>
/// <param name="TransferTargetNumberEncrypted">The sealed number, or null when the question was not asked or was answered "none".</param>
/// <param name="MessageDestinationEmailEncrypted">The sealed address, on the same terms.</param>
public sealed record SealedReceptionistConfigAnswers(
    ReceptionistConfigAnswers Answered,
    string? TransferTargetNumberEncrypted,
    string? MessageDestinationEmailEncrypted)
{
    /// <summary>The business's current settings with these answers written over them, and nothing else changed.</summary>
    public VoiceReceptionistConfig AppliedTo(VoiceReceptionistConfig config) => config with
    {
        GreetingMessage = Answered.GreetingMessage ?? config.GreetingMessage,
        SystemPromptTemplate = Answered.SystemPromptTemplate ?? config.SystemPromptTemplate,
        BusinessHours = Answered.BusinessHours ?? config.BusinessHours,
        QualifyingQuestions = Answered.QualifyingQuestions ?? config.QualifyingQuestions,
        TransferMode = Answered.TransferMode ?? config.TransferMode,
        // Not asked leaves what the business said last time; answered "none" clears it; answered leaves the sealed value.
        TransferTargetNumberEncrypted = Answered.TransferTargetNumber is null
            ? config.TransferTargetNumberEncrypted
            : TransferTargetNumberEncrypted,
        TransferCallerIdMode = Answered.TransferCallerIdMode ?? config.TransferCallerIdMode,
        EmergencyKeywords = Answered.EmergencyKeywords ?? config.EmergencyKeywords,
        RecordingDisclosureEnabled = Answered.RecordingDisclosureEnabled ?? config.RecordingDisclosureEnabled,
        MessageEmailEnabled = Answered.MessageEmailEnabled ?? config.MessageEmailEnabled,
        MessageDestinationEmailEncrypted = Answered.MessageDestinationEmail is null
            ? config.MessageDestinationEmailEncrypted
            : MessageDestinationEmailEncrypted,
        IncludeTranscript = Answered.IncludeTranscript ?? config.IncludeTranscript,
        NotifyOnAbandonedCalls = Answered.NotifyOnAbandonedCalls ?? config.NotifyOnAbandonedCalls,
        BookingEnabled = Answered.BookingEnabled ?? config.BookingEnabled,
        BookingRequested = Answered.BookingRequested ?? config.BookingRequested,
        ConsentMode = Answered.ConsentMode ?? config.ConsentMode,
    };
}
