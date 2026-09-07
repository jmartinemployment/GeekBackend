namespace GeekAPI.Services.Rag;

/// <summary>
/// Phase 0 seed entities for UI multi-select until Mongo <c>entities</c> exists.
/// Free-text entities remain allowed on generate.
/// </summary>
public static class RagEntitySeedList
{
    public static readonly IReadOnlyList<string> Names =
    [
        "HubSpot",
        "Salesforce",
        "Marketo",
        "Pardot",
        "Mailchimp",
        "Klaviyo",
        "ActiveCampaign",
        "Zoho CRM",
        "Pipedrive",
        "Intercom",
        "Zendesk",
        "Freshdesk",
        "Notion",
        "Asana",
        "Monday.com",
        "ClickUp",
        "Jira",
        "Confluence",
        "Slack",
        "Microsoft Teams",
    ];
}
