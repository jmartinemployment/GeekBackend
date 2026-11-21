// Every system this gateway will speak MCP to, and everything about one that differs from the others: where its MCP server is, what
// it is called when a failure has to name it, and the only tools and arguments a receptionist may send it.
//
// This was four exported constants describing Google Calendar and a `SupportedProvider` string compared with `!==`. That was honest
// while one system could be connected. It stops being honest at the second, and the shape it leaves behind is the one that goes
// wrong quietly: a second provider added to the comparison but not to the tool list would be let through and then checked against
// Google's tools. Here a provider that is not in this file cannot be called at all, and one that is brings its own allow-list with
// it (GeekBackend/plans/... Stage 3; the Geek-Receptionist copy is plans/connecting-client-systems.md).
//
// Adding one is an entry here and an entry in GeekAPI's McpProviders. Neither alone connects anything: GeekAPI holds the OAuth and
// this holds the MCP endpoint, and a provider missing from either is refused rather than half-supported.

export interface Provider {
  /** The stored value, and what GeekAPI sends in the body. Matches VoiceMcpProviders in GeekBackend. */
  readonly code: string;
  /** What a failure calls this system to an operator reading a log line. */
  readonly displayName: string;
  /** The vendor's own MCP server. The gateway opens one session per request against it and closes it. */
  readonly mcpUrl: string;
  /** The only tools offered, and for each the only arguments that may be sent. Anything else is refused before the vendor is called. */
  readonly toolArguments: Readonly<Record<string, readonly string[]>>;
  /** Added to a tool's arguments after the caller's, so the caller cannot override them. */
  readonly forcedArguments: Readonly<Record<string, Readonly<Record<string, unknown>>>>;
}

// The only tools a receptionist needs to read a calendar and book on it, and for each the only arguments it may send. Deleting,
// changing or answering invitations on a tenant's events is not offered until a feature needs it. create_event takes no attendees,
// attachments, meeting links or recurrence: those would make the owner's calendar send mail to, or share files with, whoever the
// caller names.
export const GoogleCalendar: Provider = {
  code: "google_calendar",
  displayName: "Google Calendar",
  mcpUrl: "https://calendarmcp.googleapis.com/mcp/v1",
  toolArguments: {
    list_calendars: ["pageSize", "pageToken"],
    list_events: ["calendarId", "startTime", "endTime", "timeZone", "orderBy", "pageSize", "pageToken"],
    get_event: ["calendarId", "eventId"],
    suggest_time: ["attendeeEmails", "durationMinutes", "startTime", "endTime", "timeZone", "preferences"],
    create_event: ["calendarId", "summary", "startTime", "endTime", "timeZone", "description", "location"],
  },
  // A booking sends no invitations.
  forcedArguments: {
    create_event: { notificationLevel: "NONE" },
  },
};

/** Every provider this gateway can reach. One entry, until a second vendor's MCP server has been connected and tested end to end. */
export const Providers: readonly Provider[] = [GoogleCalendar];

/** The provider this code names, or null for one this gateway cannot reach. */
export function providerFor(code: string): Provider | null {
  return Providers.find((provider) => provider.code === code) ?? null;
}

/** The tools this provider offers, which is exactly the keys of its argument allow-list: the two cannot disagree. */
export function allowedTools(provider: Provider): readonly string[] {
  return Object.keys(provider.toolArguments);
}

/** Every provider's code, for a message that has to say what is on offer. */
export function providerCodes(): readonly string[] {
  return Providers.map((provider) => provider.code);
}
