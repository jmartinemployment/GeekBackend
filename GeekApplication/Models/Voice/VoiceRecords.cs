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
/// A business that signs up. GeekAPI has already applied the eligibility gate; the store records the answer, so
/// <see cref="AddendumSignedAt"/> and <see cref="AddendumIpAddress"/> are both present or both absent.
/// </summary>
public sealed record CreateVoiceTenantCommand(
    string BusinessName,
    string IndustryVertical,
    string BusinessActivityCode,
    bool RequiresAddendum,
    DateTimeOffset? AddendumSignedAt,
    string? AddendumIpAddress,
    DateTimeOffset ComplianceAttestedAt,
    string CountryCode,
    string StateCode,
    string? County,
    string ContactEmail,
    string ContactPhone);

public sealed record VoiceTenant(
    Guid Id,
    string BusinessName,
    string IndustryVertical,
    string? BusinessActivityCode,
    bool RequiresAddendum,
    DateTimeOffset? AddendumSignedAt,
    string? AddendumIpAddress,
    DateTimeOffset ComplianceAttestedAt,
    string CountryCode,
    string StateCode,
    string? County,
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
    string? RetellAgentId,
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
    string? RetellAgentId,
    string? A2pBrandSid,
    string? A2pCampaignSid,
    string A2pStatus,
    bool SmsNotificationsEnabled);

/// <summary>A tenant's receptionist settings. Saving sends the whole record: there are no defaults to fall back on.</summary>
public sealed record VoiceReceptionistConfig(
    string GreetingMessage,
    string VoiceId,
    string SystemPromptTemplate,
    JsonElement BusinessHours,
    JsonElement QualifyingQuestions,
    string TransferMode,
    string? TransferTargetNumber,
    string TransferCallerIdMode,
    IReadOnlyList<string> EmergencyKeywords,
    bool RecordingDisclosureEnabled);

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
/// A call as Retell reports it. Sent again with the same Retell call id it updates the same row: an end time, a longer
/// duration, a summary, a recording link or a Twilio call SID that was missing arrives later, and one that is already there is
/// kept. The Twilio call SID is Retell's to give "when available", so it is optional; Retell's call id is always present.
/// </summary>
public sealed record UpsertVoiceCallCommand(
    string RetellCallId,
    string? TwilioCallSid,
    string CallerNumber,
    string CallStatus,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    int DurationSeconds,
    int BillableMinutes,
    JsonElement Transcript,
    string? CallSummary,
    string? Disposition,
    string? RecordingUrl);

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
