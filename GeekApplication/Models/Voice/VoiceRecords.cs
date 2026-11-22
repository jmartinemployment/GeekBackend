using System.Text.Json;

namespace GeekApplication.Models.Voice;

// Geek-Receptionist v1 (docs/Geek-Receptionist.md): what GeekAPI asks GeekRepository to store and read for a tenant, over
// /internal/v1/*. JSON camelCase on the wire. Encrypted values are ciphertext the caller produced; GeekRepository never decrypts.

/// <summary>Why a store call failed. The controller turns the code into a status; the wording is for people and never decides one.</summary>
public static class VoiceFailureCodes
{
    public const string NotFound = "not_found";
    public const string InvalidRequest = "invalid_request";
    public const string Conflict = "conflict";
    public const string StoreUnavailable = "store_unavailable";
}

public static class VoiceTenantStatuses
{
    public const string PendingPayment = "pending_payment";
    public const string Active = "active";
    public const string Suspended = "suspended";
    public const string Cancelled = "cancelled";
    public static readonly IReadOnlyList<string> All = [PendingPayment, Active, Suspended, Cancelled];
}

public static class VoiceSubscriptionTiers
{
    public const string Starter = "STARTER";
    public const string Standard = "STANDARD";
    public const string Pro = "PRO";
    public const string Premium = "PREMIUM";
    public static readonly IReadOnlyList<string> All = [Starter, Standard, Pro, Premium];

    /// <summary>
    /// The tiers that may write call records into the business's own bucket (plans/client-and-app-plan.md: "AWS S3 / Google Cloud
    /// Storage (Standard &amp; Above)"). Google Drive is on every tier and is not gated here.
    /// </summary>
    public static readonly IReadOnlyList<string> WithObjectStorage = [Standard, Pro, Premium];
}

/// <summary>The PayPal subscription status a tenant's subscription row last recorded. Activation stores ACTIVE; end-of-cycle billing copies PayPal's own status when it is anything else.</summary>
public static class VoiceBillingStatuses
{
    public const string Active = "ACTIVE";
}

public static class VoiceTransferModes
{
    public const string WarmTransfer = "warm_transfer";
    public const string ColdTransfer = "cold_transfer";
    public const string VoicemailOnly = "voicemail_only";
    public static readonly IReadOnlyList<string> All = [WarmTransfer, ColdTransfer, VoicemailOnly];
}

/// <summary>
/// How a caller is told, and asked, before the AI engages (plans/client-and-app-plan.md, "Upfront consent gate (Two-Mode Tenant
/// Setting)").
///
/// <para>
/// <b>Which one a business uses is the business's own choice</b> - Jeff, 2026-11-20: "Client configurable. I don't feel comfortable
/// making that decision for others." The value here is only what a tenant starts with before it has been asked, and it is not a
/// recommendation. It is strict keypress, because a business that has not been asked yet should start at the mode that asks something
/// of a caller rather than the one that asks nothing and deters nothing; a dashboard switch does not help a business that never opens
/// the dashboard.
/// </para>
///
/// <para>
/// <b>Strict keypress</b> costs every caller a key-press and earns it three times over: a key pressed is affirmative consent rather than
/// an assumption drawn from somebody not hanging up, which is the better evidence in an all-party state; an autodialer playing a
/// recording does not press keys, so most of them end there; and because this product bills by the minute, a robocall the AI answers is
/// a minute billed to a business for a call it never wanted. It is stricter than the comparable products ship - Vapi offers stay-on-line
/// and spoken consent, and no keypress at all.
/// </para>
///
/// <para>
/// <b>Passive disclosure</b> asks nothing of the caller. It is what the rest of the industry does, and it deters nothing: staying on the
/// line is exactly what a recording does. The business trades billed robocall minutes for a caller who never has to touch a keypad.
/// </para>
/// </summary>
public static class VoiceConsentModes
{
    public const string PassiveDisclosure = "passive_disclosure";
    public const string StrictKeypress = "strict_keypress";
    public static readonly IReadOnlyList<string> All = [PassiveDisclosure, StrictKeypress];
}

/// <summary>
/// The languages a receptionist may speak, as Twilio's BCP-47 locales. Codes only: every sentence a caller hears in each of them lives
/// in GeekAPI's <c>ReceptionistLanguages</c>, and a test asserts this list and that one hold exactly the same codes, so adding a code
/// here without its reviewed wording fails the build rather than reaching a caller.
///
/// <para>
/// A language is offerable only once its caller-facing wording - including the consent disclosure, which is reviewed under
/// Fla. Stat. 934.03 rather than translated - exists. <c>en-US</c> is also ConversationRelay's documented default for an unset
/// <c>language</c> attribute, so it is both the default and the only entry today.
/// </para>
/// </summary>
public static class VoiceLanguages
{
    public const string EnglishUnitedStates = "en-US";
    public const string Default = EnglishUnitedStates;
    public static readonly IReadOnlyList<string> All = [EnglishUnitedStates];
}

public static class VoiceTransferCallerIdModes
{
    public const string OriginalCaller = "original_caller";
    public const string AssignedAiNumber = "assigned_ai_number";
    public static readonly IReadOnlyList<string> All = [OriginalCaller, AssignedAiNumber];
}

/// <summary>
/// One system a tenant can connect, and what it can do for the receptionist.
///
/// <para>
/// <b>Capabilities are a set, because a vendor is not one thing.</b> HubSpot is a CRM that also has a calendar; Outlook is a calendar
/// that also has contacts. Filing a vendor under a single kind forces a choice between its features, and that is exactly how a CRM
/// came to sit in the list the appointments table reads: <c>bccc1a30</c> (2026-11-19) added Google Drive and carved it out as
/// "everything that is not Drive is a calendar", which swept HubSpot in under a comment asserting the opposite. Jeff, 2026-11-20:
/// <em>"Hubspot while it has a calendar, no it is a CRM, your filing."</em>
/// </para>
///
/// <para>
/// A capability is declared only once it is built and tested end to end for that provider - connect, reconnect, refresh,
/// reauthorize, call a tool, do the job, disconnect. Declaring one we have not proved is the same defect as a name that reads as a
/// live claim: something downstream offers it. HubSpot's meetings API is real and is not <see cref="Booking"/> here, because nothing
/// books through it.
/// </para>
/// </summary>
/// <param name="Code">The stored value, and what a route names.</param>
/// <param name="Name">What an owner is shown.</param>
/// <param name="Booking">An appointment can be read from and written to it.</param>
/// <param name="Contacts">People and their details can be read from and written to it.</param>
/// <param name="Storage">A business's own call records can be written to it.</param>
public sealed record VoiceMcpProvider(string Code, string Name, bool Booking, bool Contacts, bool Storage);

public static class VoiceMcpProviders
{
    public const string GoogleCalendar = "google_calendar";
    public const string Outlook = "outlook";
    public const string CalCom = "cal_com";
    public const string HubSpot = "hubspot";

    /// <summary>Not a calendar: where a business has its own call records written (plans/client-and-app-plan.md, BYOS).</summary>
    public const string GoogleDrive = "google_drive";

    /// <summary>
    /// Every provider, with what each can do. Only <see cref="GoogleCalendar"/> and <see cref="GoogleDrive"/> have an OAuth path
    /// today; the other three are accepted values with nothing behind them yet (plans/connecting-client-systems.md).
    /// </summary>
    public static readonly IReadOnlyList<VoiceMcpProvider> Known =
    [
        new(GoogleCalendar, "Google Calendar", Booking: true, Contacts: false, Storage: false),
        new(Outlook, "Outlook", Booking: true, Contacts: false, Storage: false),
        new(CalCom, "Cal.com", Booking: true, Contacts: false, Storage: false),
        new(HubSpot, "HubSpot", Booking: false, Contacts: true, Storage: false),
        new(GoogleDrive, "Google Drive", Booking: false, Contacts: false, Storage: true),
    ];

    public static readonly IReadOnlyList<string> All = [.. Known.Select(p => p.Code)];

    /// <summary>
    /// The providers an appointment can come from: derived from <see cref="VoiceMcpProvider.Booking"/> rather than listed by hand, so
    /// it cannot disagree with what each provider is said to do.
    /// </summary>
    public static readonly IReadOnlyList<string> Calendars = [.. Known.Where(p => p.Booking).Select(p => p.Code)];

    /// <summary>The providers a person's details can be read from and written to.</summary>
    public static readonly IReadOnlyList<string> Crms = [.. Known.Where(p => p.Contacts).Select(p => p.Code)];

    /// <summary>The providers a business's own call records can be written to.</summary>
    public static readonly IReadOnlyList<string> Stores = [.. Known.Where(p => p.Storage).Select(p => p.Code)];
}

/// <summary>
/// The object stores a business can have its call records written to (plans/client-and-app-plan.md, "AWS S3 / Google Cloud Storage").
/// Both speak the S3 API and are signed the same way; only the endpoint differs.
/// </summary>
public static class VoiceStorageProviders
{
    public const string AwsS3 = "aws_s3";
    public const string GoogleCloudStorage = "google_cloud_storage";
    public static readonly IReadOnlyList<string> All = [AwsS3, GoogleCloudStorage];
}

// ----- Tenants -----

/// <summary>
/// A business that signs up. GeekAPI has already applied the eligibility gate; the store records the tenant and its answers.
/// </summary>
public sealed record CreateVoiceTenantCommand(
    string BusinessName,
    string IndustryVertical,
    DateTimeOffset ComplianceAttestedAt,
    string CountryCode,
    string StateCode,
    string ContactEmailEncrypted,
    string ContactPhoneEncrypted)
{
    /// <summary>The business's no to every excluded-industry question, stored with the tenant in one transaction and kept forever.</summary>
    public required VoiceIndustryAnswerSet IndustryAnswers { get; init; }

    /// <summary>The business's website: an absolute http or https address, or null when it has none. Not sealed, it is public.</summary>
    public string? BusinessWebsite { get; init; }

    /// <summary>The named person's declaration and acceptance of the Terms, stored with the tenant in one transaction and kept forever.</summary>
    public required VoiceSignupAttestation Attestation { get; init; }
}

/// <summary>
/// The declaration a named person made before a tenant existed (plans/attestation-and-terms.md, stages 2 and 3): that they are authorised
/// to answer for the business and that the answers are true, and that they accept the Terms. It is a record of agreement, so it exists
/// only for a person who agreed; a No creates no tenant and so no attestation. The name, title and address are ciphertext GeekAPI sealed
/// (<c>VoiceSecretPurposes</c> <c>SignupAttestorName</c> / <c>SignupBusinessAddress</c> / <c>SignupDeclarantAddress</c> / <c>SignupDeclarantPhone</c> / <c>SignupDeclarantEmail</c>); the two statements are
/// the exact words the person was shown.
/// </summary>
public sealed record VoiceSignupAttestation(
    string AttestorNameEncrypted,
    string BusinessAddressEncrypted,
    string DeclarantAddressEncrypted,
    string DeclarantPhoneEncrypted,
    string DeclarantEmailEncrypted,
    string TermsStatement,
    DateTimeOffset AttestedAt,
    string IpAddress,
    string VisitorCountry);

/// <summary>One excluded-industry question of /signup as it was answered: the industry's code, the exact words shown, and the answer.</summary>
public sealed record VoiceIndustryAnswer(string Industry, string Question, bool Answer);

/// <summary>
/// The excluded-industry answers one visitor gave, with when they were recorded and the visitor's own address and country. Stored in
/// geek_voice.signup_industry_answers, which the database never lets change or lose a row (docs/Geek-Receptionist.md section 5, item 1).
/// </summary>
public sealed record VoiceIndustryAnswerSet(
    IReadOnlyList<VoiceIndustryAnswer> Answers,
    DateTimeOffset AnsweredAt,
    string IpAddress,
    string VisitorCountry);

/// <summary>How many answers of a refused visitor were stored.</summary>
public sealed record VoiceIndustryAnswersRecorded(int Answers);

/// <summary>
/// A business as it is stored. Its contact address and number are ciphertext GeekAPI sealed (<c>VoiceSecretPurposes</c>
/// <c>TenantContactEmail</c> / <c>TenantContactPhone</c>); GeekRepository holds them and never reads them, and GeekAPI opens one only
/// where it needs the clear value. The name says so, because a field called ContactEmail would be read as an address and sent to.
/// </summary>
public sealed record VoiceTenant(
    Guid Id,
    string BusinessName,
    string IndustryVertical,
    DateTimeOffset ComplianceAttestedAt,
    string CountryCode,
    string StateCode,
    string ContactEmailEncrypted,
    string ContactPhoneEncrypted,
    string Status,
    DateTimeOffset CreatedAt);

/// <summary>
/// The tenant has paid and its telephony exists: one subscription row, one telephony row, status active, in one transaction.
/// Only a tenant still pending payment may be activated.
/// </summary>
public sealed record ActivateVoiceTenantCommand(
    string TierCode,
    decimal MonthlyPriceUsd,
    int IncludedMinutes,
    decimal OverageRateUsd,
    string PaypalSubscriptionId,
    string PaypalVaultTokenId,
    DateTimeOffset CurrentPeriodStart,
    DateTimeOffset CurrentPeriodEnd,
    string BillingStatus,
    string TwilioSubaccountSid,
    string TwilioAuthTokenEncrypted,
    string AssignedPhoneNumber,
    string TwilioPhoneSid,
    string? A2pBrandSid,
    string? A2pCampaignSid,
    string A2pStatus,
    bool SmsNotificationsEnabled);

// ----- Telephony, configuration, calendar connections -----

public sealed record VoiceTelephony(
    Guid TenantId,
    string TwilioSubaccountSid,
    string TwilioAuthTokenEncrypted,
    string AssignedPhoneNumber,
    string TwilioPhoneSid,
    string? A2pBrandSid,
    string? A2pCampaignSid,
    string A2pStatus,
    bool SmsNotificationsEnabled);

/// <summary>
/// A tenant's receptionist settings. Saving sends the whole record: there are no defaults to fall back on.
///
/// <para>
/// Two of the sixteen are a person's details rather than a behaviour, and both are ciphertext GeekAPI sealed: the number a caller is
/// transferred to and the address an end-of-call message goes to. The other fourteen stay in the clear - they are read on every call,
/// they are not anybody's personal details, and encrypting a boolean or a greeting buys nothing.
/// </para>
/// </summary>
public sealed record VoiceReceptionistConfig(
    string GreetingMessage,
    string SystemPromptTemplate,
    JsonElement BusinessHours,
    JsonElement QualifyingQuestions,
    string TransferMode,
    string? TransferTargetNumberEncrypted,
    string TransferCallerIdMode,
    IReadOnlyList<string> EmergencyKeywords,
    bool RecordingDisclosureEnabled,
    bool MessageEmailEnabled = false,
    string? MessageDestinationEmailEncrypted = null,
    bool IncludeTranscript = false,
    bool NotifyOnAbandonedCalls = false,
    bool BookingEnabled = false,
    /// <summary>
    /// That the business asked for appointments, which is not the same as its receptionist being able to book one.
    /// <see cref="BookingEnabled"/> is the capability and is refused without a calendar that works; this is the answer, given on the
    /// signup form before the business has a login to connect a calendar with. The first sign-in reads it, asks for the calendar, and
    /// switches the capability on - honouring an answer the business gave rather than inferring one from a connection existing.
    /// </summary>
    bool BookingRequested = false,
    string ConsentMode = VoiceConsentModes.StrictKeypress,
    /// <summary>
    /// The business's own crawled, indexed website, which its receptionist answers questions from. Null when it has none, which gives the
    /// receptionist no lookup tool rather than one that finds nothing. A scope, not a store: the Library holds the vectors.
    /// </summary>
    Guid? KnowledgeRunId = null,
    /// <summary>
    /// The language the receptionist speaks to a caller in, as Twilio's BCP-47 locale. Its only legal values are the codes in
    /// <c>ReceptionistLanguages</c>, which holds every sentence a caller hears in that language - including the consent disclosure,
    /// which is reviewed wording rather than a translatable string. Defaults to ConversationRelay's own default, so a config written
    /// before this column existed is unchanged rather than unset.
    /// </summary>
    string Language = VoiceLanguages.Default);

public sealed record VoiceMcpConnection(
    Guid TenantId,
    string Provider,
    string OauthAccessTokenEncrypted,
    string OauthRefreshTokenEncrypted,
    DateTimeOffset? TokenExpiresAt,
    string? ExternalCalendarId,
    bool IsActive);

/// <summary>
/// A tenant's calendar or CRM connection as GeekAPI saves it after the OAuth callback. Both tokens are ciphertext GeekAPI
/// produced and GeekRepository never decrypts. The provider comes from the route, so the body cannot name a different one.
/// Saving again for the same tenant and provider replaces the whole connection and makes it active.
/// </summary>
public sealed record SaveVoiceMcpConnectionCommand(
    string OauthAccessTokenEncrypted,
    string OauthRefreshTokenEncrypted,
    DateTimeOffset? TokenExpiresAt,
    string? ExternalCalendarId);

// ----- Bring-your-own-storage -----

/// <summary>
/// Where a business has its call records written, and the keys to write them with. Both keys are ciphertext GeekAPI produced and
/// GeekRepository never decrypts. One connection per tenant: a business writes its records to one place.
/// </summary>
public sealed record VoiceStorageConnection(
    Guid TenantId,
    string Provider,
    string AccessKeyIdEncrypted,
    string SecretAccessKeyEncrypted,
    string Bucket,
    string KeyPrefix,
    string Region,
    bool IsActive);

/// <summary>
/// A business's bucket as the owner gave it. Saving again replaces the whole connection and makes it active, as the calendar's does.
/// The credentials are expected to carry nothing but the right to put an object, which is what the setup instructions grant.
/// </summary>
public sealed record SaveVoiceStorageConnectionCommand(
    string Provider,
    string AccessKeyIdEncrypted,
    string SecretAccessKeyEncrypted,
    string Bucket,
    string KeyPrefix,
    string Region);

// ----- Withdrawing a connection -----

/// <summary>Whether there was a connection to remove. Removing one that is already gone is not a failure: an owner withdrawing permission twice has still withdrawn it.</summary>
public sealed record VoiceConnectionRemoved(bool Removed);

// ----- Calls and appointments -----

/// <summary>
/// What billing needs of a call as Twilio's status callback reports it, and nothing of what was said: no caller number, transcript,
/// summary or recording link (docs/Geek-Receptionist.md section 5, item 6). Sent again with the same Twilio call SID it updates the
/// same row: an end time or a longer duration that arrives later is kept, and one that is already there is not lost.
/// </summary>
public sealed record UpsertVoiceCallCommand(
    string TwilioCallSid,
    string CallStatus,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    int DurationSeconds,
    int BillableMinutes,
    string? Disposition);

public sealed record VoiceCallStored(Guid CallId);

/// <summary>
/// An appointment as this platform records it: metadata only. Who it is with and what it is about are call content, are never stored
/// here, and live in the owner's own calendar, which the provider's event id points at.
/// </summary>
public sealed record CreateVoiceAppointmentCommand(
    Guid? CallLogId,
    DateTimeOffset ScheduledStart,
    DateTimeOffset ScheduledEnd,
    string? ExternalEventId,
    string McpProvider);

public sealed record VoiceAppointmentCreated(Guid AppointmentId);
