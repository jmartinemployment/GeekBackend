import { GatewayError } from "./errors.ts";

const CalendarApi = "https://www.googleapis.com/calendar/v3";
const GoogleTimeoutMs = 20_000;

export type FetchFn = (url: string, init: RequestInit) => Promise<Response>;

export interface BusyInterval {
  readonly start: string;
  readonly end: string;
}

export interface AvailabilityQuery {
  readonly accessToken: string;
  readonly calendarId: string;
  readonly timeMin: string;
  readonly timeMax: string;
}

export interface EventDraft {
  readonly accessToken: string;
  readonly calendarId: string;
  readonly eventId?: string;
  readonly summary: string;
  readonly description?: string;
  readonly location?: string;
  readonly start: string;
  readonly end: string;
  readonly timeZone?: string;
  readonly attendeeEmails: readonly string[];
}

export interface CreatedEvent {
  readonly eventId: string;
  readonly htmlLink: string | null;
  readonly start: string;
  readonly end: string;
}

interface GoogleErrorBody {
  readonly error?: { readonly errors?: readonly { readonly reason?: string }[]; readonly status?: string };
}

// One call to Google, once. A refusal becomes a GatewayError with a code the caller can act on; Google's own message is never
// passed along because it can echo request content.
async function callGoogle(fetchFn: FetchFn, url: string, accessToken: string, body: unknown): Promise<unknown> {
  let response: Response;
  try {
    response = await fetchFn(url, {
      method: "POST",
      headers: { authorization: `Bearer ${accessToken}`, "content-type": "application/json" },
      body: JSON.stringify(body),
      signal: AbortSignal.timeout(GoogleTimeoutMs),
    });
  } catch {
    throw new GatewayError(502, "provider_unreachable", "Google Calendar could not be reached.");
  }

  if (response.ok) {
    let parsed: unknown;
    try {
      parsed = await response.json();
    } catch {
      throw new GatewayError(502, "provider_unavailable", "Google Calendar answered with something that is not JSON.");
    }
    if (typeof parsed !== "object" || parsed === null || Array.isArray(parsed)) {
      throw new GatewayError(502, "provider_unavailable", "Google Calendar answered with something that is not a JSON object.");
    }
    return parsed;
  }

  throw await refusal(response);
}

async function refusal(response: Response): Promise<GatewayError> {
  let reason = "";
  try {
    const parsed = (await response.json()) as GoogleErrorBody;
    reason = parsed.error?.errors?.[0]?.reason ?? parsed.error?.status ?? "";
  } catch {
    reason = "";
  }

  switch (response.status) {
    case 400:
      return new GatewayError(400, "provider_rejected", `Google Calendar rejected the request (${reason || "bad request"}).`);
    case 401:
      return new GatewayError(401, "token_rejected", "Google Calendar did not accept the access token; it has expired or been revoked.");
    case 403:
      if (reason === "rateLimitExceeded" || reason === "userRateLimitExceeded" || reason === "quotaExceeded") {
        return new GatewayError(502, "provider_unavailable", "Google Calendar is limiting requests.");
      }
      return new GatewayError(403, "provider_forbidden", `Google Calendar refused this account (${reason || "forbidden"}).`);
    case 404:
      return new GatewayError(404, "calendar_not_found", "That calendar was not found.");
    case 409:
      return new GatewayError(409, "event_exists", "An event with that id already exists.");
    default:
      return new GatewayError(502, "provider_unavailable", `Google Calendar failed with status ${response.status}.`);
  }
}

interface FreeBusyAnswer {
  readonly calendars?: Record<string, { readonly busy?: readonly BusyInterval[]; readonly errors?: readonly { readonly reason?: string }[] }>;
}

export async function queryBusy(fetchFn: FetchFn, query: AvailabilityQuery): Promise<readonly BusyInterval[]> {
  const answer = (await callGoogle(fetchFn, `${CalendarApi}/freeBusy`, query.accessToken, {
    timeMin: query.timeMin,
    timeMax: query.timeMax,
    items: [{ id: query.calendarId }],
  })) as FreeBusyAnswer;

  const calendar = answer.calendars?.[query.calendarId];
  if (!calendar) {
    throw new GatewayError(502, "provider_unavailable", "Google Calendar answered without the calendar that was asked about.");
  }
  if (calendar.errors && calendar.errors.length > 0) {
    const reason = calendar.errors[0]?.reason ?? "";
    if (reason === "notFound") throw new GatewayError(404, "calendar_not_found", "That calendar was not found.");
    throw new GatewayError(403, "provider_forbidden", `Google Calendar would not show that calendar (${reason || "error"}).`);
  }
  if (!Array.isArray(calendar.busy)) {
    throw new GatewayError(502, "provider_unavailable", "Google Calendar answered with no list of busy times.");
  }

  return calendar.busy.map((interval) => ({ start: interval.start, end: interval.end }));
}

interface EventAnswer {
  readonly id?: string;
  readonly htmlLink?: string;
  readonly start?: { readonly dateTime?: string };
  readonly end?: { readonly dateTime?: string };
}

export async function insertEvent(fetchFn: FetchFn, draft: EventDraft): Promise<CreatedEvent> {
  const body: Record<string, unknown> = {
    summary: draft.summary,
    start: { dateTime: draft.start, ...(draft.timeZone ? { timeZone: draft.timeZone } : {}) },
    end: { dateTime: draft.end, ...(draft.timeZone ? { timeZone: draft.timeZone } : {}) },
  };
  if (draft.eventId) body["id"] = draft.eventId;
  if (draft.description) body["description"] = draft.description;
  if (draft.location) body["location"] = draft.location;
  if (draft.attendeeEmails.length > 0) body["attendees"] = draft.attendeeEmails.map((email) => ({ email }));

  const url = `${CalendarApi}/calendars/${encodeURIComponent(draft.calendarId)}/events?sendUpdates=none`;
  const answer = (await callGoogle(fetchFn, url, draft.accessToken, body)) as EventAnswer;

  if (!answer.id || !answer.start?.dateTime || !answer.end?.dateTime) {
    throw new GatewayError(502, "provider_unavailable", "Google Calendar answered without the event it created.");
  }

  return { eventId: answer.id, htmlLink: answer.htmlLink ?? null, start: answer.start.dateTime, end: answer.end.dateTime };
}
