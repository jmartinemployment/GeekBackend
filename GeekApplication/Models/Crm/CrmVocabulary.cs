namespace GeekApplication.Models.Crm;

// Geek-CRM's fixed vocabularies (docs/GEEK-CRM-DATA-MODEL.md). Each list is declared in
// GeekApplication/Schemas/GeekCrm.xsd, repeated as a CHECK constraint in
// GeekRepository/Migrations/Sql/0050_geek_crm_schema.sql, and GeekBackend.Tests compares all three,
// so a value one of them knows and another refuses fails the build instead of a write.

public static class ParticipantKinds
{
    public const string Person = "person";
    public const string Organization = "organization";
    public static readonly IReadOnlyList<string> All = [Person, Organization];
}

public static class LifecycleStages
{
    public const string Lead = "lead";
    public const string Prospect = "prospect";
    public const string Customer = "customer";
    public const string FormerCustomer = "former_customer";
    public const string Other = "other";
    public static readonly IReadOnlyList<string> All = [Lead, Prospect, Customer, FormerCustomer, Other];
}

public static class ParticipantSources
{
    public const string PhoneCall = "phone_call";
    public const string Text = "text";
    public const string Website = "website";
    public const string Referral = "referral";
    public const string Import = "import";
    public const string Event = "event";
    public const string Outreach = "outreach";
    public const string Other = "other";
    public static readonly IReadOnlyList<string> All = [PhoneCall, Text, Website, Referral, Import, Event, Outreach, Other];
}

public static class ContactPointKinds
{
    public const string Phone = "phone";
    public const string Email = "email";
    public const string Address = "address";
    public const string Website = "website";
    public const string Social = "social";
    public static readonly IReadOnlyList<string> All = [Phone, Email, Address, Website, Social];
}

public static class ContactPointLabels
{
    public const string Mobile = "mobile";
    public const string Work = "work";
    public const string Home = "home";
    public const string Main = "main";
    public const string Billing = "billing";
    public const string Other = "other";
    public static readonly IReadOnlyList<string> All = [Mobile, Work, Home, Main, Billing, Other];
}

public static class ParticipantLinkRoles
{
    public const string Owner = "owner";
    public const string Employee = "employee";
    public const string DecisionMaker = "decision_maker";
    public const string BillingContact = "billing_contact";
    public const string ReferredBy = "referred_by";
    public const string Partner = "partner";
    public const string Other = "other";
    public static readonly IReadOnlyList<string> All = [Owner, Employee, DecisionMaker, BillingContact, ReferredBy, Partner, Other];
}

public static class OrganizationTypes
{
    public static readonly IReadOnlyList<string> All =
        ["sole_proprietor", "llc", "partnership", "c_corporation", "s_corporation", "nonprofit", "government", "other"];
}

public static class RevenueBands
{
    public static readonly IReadOnlyList<string> All = ["under_250k", "250k_1m", "1m_5m", "5m_25m", "over_25m"];
}

public static class MaritalStatuses
{
    public static readonly IReadOnlyList<string> All = ["single", "married", "domestic_partner", "divorced", "widowed"];
}

public static class EducationLevels
{
    public static readonly IReadOnlyList<string> All =
        ["high_school", "some_college", "associate", "bachelor", "master", "doctorate", "other"];
}

public static class StageOutcomes
{
    public const string Open = "open";
    public const string Won = "won";
    public const string Lost = "lost";
    public static readonly IReadOnlyList<string> All = [Open, Won, Lost];
}

public static class DealParticipantRoles
{
    public const string PrimaryContact = "primary_contact";
    public static readonly IReadOnlyList<string> All = [PrimaryContact, "decision_maker", "influencer", "billing", "other"];
}

public static class ActivityChannels
{
    public const string Call = "call";
    public const string Sms = "sms";
    public const string Voicemail = "voicemail";
    public const string Email = "email";
    public const string Meeting = "meeting";
    public const string Note = "note";
    public const string Task = "task";
    public static readonly IReadOnlyList<string> All = [Call, Sms, Voicemail, Email, Meeting, Note, Task];
}

public static class ActivityDirections
{
    public const string Inbound = "inbound";
    public const string Outbound = "outbound";
    public static readonly IReadOnlyList<string> All = [Inbound, Outbound];
}

public static class ActivityOutcomes
{
    public const string Answered = "answered";
    public const string Missed = "missed";
    public const string TextedBack = "texted_back";
    public const string VoicemailLeft = "voicemail_left";
    public const string Booked = "booked";
    public const string NoShow = "no_show";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public static readonly IReadOnlyList<string> All = [Answered, Missed, TextedBack, VoicemailLeft, Booked, NoShow, Completed, Failed];
}

public static class PropertyObjectTypes
{
    public const string Person = "person";
    public const string Organization = "organization";
    public const string Deal = "deal";
    public static readonly IReadOnlyList<string> All = [Person, Organization, Deal];
}

public static class PropertyDataTypes
{
    public const string Text = "text";
    public const string Number = "number";
    public const string Date = "date";
    public const string Bool = "bool";
    public const string Enum = "enum";
    public const string MultiEnum = "multi_enum";
    public static readonly IReadOnlyList<string> All = [Text, Number, Date, Bool, Enum, MultiEnum];
}

/// <summary>Stable failure codes every <c>ICrmStore</c> result carries; mapped to HTTP status by both sides.</summary>
public static class CrmFailureCodes
{
    public const string NotFound = "not_found";
    public const string InvalidRequest = "invalid_request";
    public const string Conflict = "conflict";
    public const string StoreUnavailable = "store_unavailable";
}

/// <summary>Well-known ids seeded by 0050_geek_crm_schema.sql.</summary>
public static class CrmSeed
{
    /// <summary>Geek @ Your Spot, tenant #1.</summary>
    public static readonly Guid GeekAtYourSpotTenantId = Guid.Parse("6e0c2a3a-7f4e-4b8e-9d51-5a1c0f6b2a01");
}

public static class ProductKinds
{
    public const string GeekSaas = "geek_saas";
    public const string Implementation = "implementation";
    public const string PartnerSubscription = "partner_subscription";
    public const string SupportPlan = "support_plan";
    public const string Service = "service";
    public static readonly IReadOnlyList<string> All = [GeekSaas, Implementation, PartnerSubscription, SupportPlan, Service];
}

public static class ProductCategories
{
    public const string Accounting = "accounting";
    public const string BusinessOperations = "business_operations";
    public const string Marketing = "marketing";
    public const string Sales = "sales";
    public const string Bespoke = "bespoke";
    public static readonly IReadOnlyList<string> All = [Accounting, BusinessOperations, Marketing, Sales, Bespoke];
}

public static class BillingPeriods
{
    public const string OneTime = "one_time";
    public const string Monthly = "monthly";
    public const string Annual = "annual";
    public static readonly IReadOnlyList<string> All = [OneTime, Monthly, Annual];
}

public static class HoldingStatuses
{
    public const string Proposed = "proposed";
    public const string Active = "active";
    public const string Paused = "paused";
    public const string Cancelled = "cancelled";
    public const string Ended = "ended";
    public static readonly IReadOnlyList<string> All = [Proposed, Active, Paused, Cancelled, Ended];
}

public static class PartnerArrangements
{
    public const string CustomerDirect = "customer_direct";
    public const string Referral = "referral";
    public const string Resold = "resold";
    public static readonly IReadOnlyList<string> All = [CustomerDirect, Referral, Resold];
}

public static class EstimateSizes
{
    public const string Small = "small";
    public const string Medium = "medium";
    public const string Large = "large";
    public static readonly IReadOnlyList<string> All = [Small, Medium, Large];
}

public static class Currencies
{
    public const string Usd = "USD";
    public static readonly IReadOnlyList<string> All = [Usd];
}
