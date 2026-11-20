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

public static class VoiceTransferCallerIdModes
{
    public const string OriginalCaller = "original_caller";
    public const string AssignedAiNumber = "assigned_ai_number";
    public static readonly IReadOnlyList<string> All = [OriginalCaller, AssignedAiNumber];
}

public static class VoiceMcpProviders
{
    public const string GoogleCalendar = "google_calendar";
    public const string Outlook = "outlook";
    public const string CalCom = "cal_com";
    public const string HubSpot = "hubspot";
    public static readonly IReadOnlyList<string> All = [GoogleCalendar, Outlook, CalCom, HubSpot];
}

public static class VoiceKnowledgeSourceTypes
{
    public const string GuidedTradeForm = "guided_trade_form";
    public const string PdfUpload = "pdf_upload";
    public const string GeekCrawlerUrl = "geek_crawler_url";
    public static readonly IReadOnlyList<string> All = [GuidedTradeForm, PdfUpload, GeekCrawlerUrl];
}

/// <summary>The size of an embedding: BAAI/bge-small-en-v1.5.</summary>
public static class VoiceEmbedding
{
    public const int Dimensions = 384;
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
    string ContactEmail,
    string ContactPhone)
{
    /// <summary>The business's no to every excluded-industry question, stored with the tenant in one transaction and kept forever.</summary>
    public required VoiceIndustryAnswerSet IndustryAnswers { get; init; }
}

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

public sealed record VoiceTenant(
    Guid Id,
    string BusinessName,
    string IndustryVertical,
    DateTimeOffset ComplianceAttestedAt,
    string CountryCode,
    string StateCode,
    string ContactEmail,
    string ContactPhone,
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

/// <summary>A tenant's receptionist settings. Saving sends the whole record: there are no defaults to fall back on.</summary>
public sealed record VoiceReceptionistConfig(
    string GreetingMessage,
    string SystemPromptTemplate,
    JsonElement BusinessHours,
    JsonElement QualifyingQuestions,
    string TransferMode,
    string? TransferTargetNumber,
    string TransferCallerIdMode,
    IReadOnlyList<string> EmergencyKeywords,
    bool RecordingDisclosureEnabled,
    bool MessageEmailEnabled = false,
    string? MessageDestinationEmail = null,
    bool IncludeTranscript = false,
    bool NotifyOnAbandonedCalls = false,
    bool BookingEnabled = false);

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

// ----- Knowledge -----

public sealed record VoiceKnowledgeChunk(int ChunkIndex, string ChunkText, IReadOnlyList<float> Embedding);

/// <summary>A document and all of its chunks, stored together or not at all.</summary>
public sealed record CreateVoiceKnowledgeDocumentCommand(
    string SourceType,
    string Title,
    string? SourceUrl,
    string RawContent,
    IReadOnlyList<VoiceKnowledgeChunk> Chunks);

public sealed record VoiceKnowledgeDocumentCreated(Guid DocumentId);

public sealed record SearchVoiceKnowledgeCommand(IReadOnlyList<float> QueryEmbedding, int Limit);

/// <summary>Cosine similarity, 1 for the same direction and lower the further apart.</summary>
public sealed record VoiceKnowledgeHit(Guid DocumentId, int ChunkIndex, string ChunkText, double Similarity);

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

public sealed record CreateVoiceAppointmentCommand(
    Guid? CallLogId,
    string CustomerName,
    string CustomerPhone,
    string? CustomerEmail,
    string ServiceAddress,
    string IssueDescription,
    DateTimeOffset ScheduledStart,
    DateTimeOffset ScheduledEnd,
    string? ExternalEventId,
    string McpProvider);

public sealed record VoiceAppointmentCreated(Guid AppointmentId);
