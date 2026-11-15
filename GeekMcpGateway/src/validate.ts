import { GatewayError } from "./errors.ts";
import type { AvailabilityQuery, EventDraft } from "./google-calendar.ts";

export const SupportedProvider = "google_calendar";
const MaxAvailabilityDays = 62;
const MaxEventHours = 24;
const GoogleEventId = /^[a-v0-9]{5,100}$/;
const Email = /^[^@\s]+@[^@\s]+\.[^@\s]+$/;
const OffsetDateTime = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d+)?)?(Z|[+-]\d{2}:\d{2})$/;

function invalid(message: string): GatewayError {
  return new GatewayError(400, "invalid_request", message);
}

function asObject(body: unknown): Record<string, unknown> {
  if (typeof body !== "object" || body === null || Array.isArray(body)) throw invalid("A JSON object is required.");
  return body as Record<string, unknown>;
}

function requiredText(body: Record<string, unknown>, name: string, maxLength: number): string {
  const value = body[name];
  if (typeof value !== "string" || value.trim() === "") throw invalid(`${name} is required.`);
  if (value.length > maxLength) throw invalid(`${name} is longer than ${maxLength} characters.`);
  return value.trim();
}

function optionalText(body: Record<string, unknown>, name: string, maxLength: number): string | undefined {
  const value = body[name];
  if (value === undefined || value === null) return undefined;
  if (typeof value !== "string") throw invalid(`${name} must be text.`);
  if (value.length > maxLength) throw invalid(`${name} is longer than ${maxLength} characters.`);
  return value.trim() === "" ? undefined : value.trim();
}

function requiredInstant(body: Record<string, unknown>, name: string): { text: string; ms: number } {
  const text = requiredText(body, name, 40);
  const ms = OffsetDateTime.test(text) ? Date.parse(text) : Number.NaN;
  if (Number.isNaN(ms)) throw invalid(`${name} must be a date and time with an offset, like 2026-11-20T14:00:00-05:00.`);
  return { text, ms };
}

function checkProvider(body: Record<string, unknown>): void {
  const provider = requiredText(body, "provider", 40);
  if (provider !== SupportedProvider) {
    throw new GatewayError(400, "provider_not_supported", `Only ${SupportedProvider} is supported so far.`);
  }
}

export function parseAvailability(body: unknown): AvailabilityQuery {
  const input = asObject(body);
  checkProvider(input);
  const from = requiredInstant(input, "timeMin");
  const to = requiredInstant(input, "timeMax");
  if (to.ms <= from.ms) throw invalid("timeMax must be after timeMin.");
  if (to.ms - from.ms > MaxAvailabilityDays * 86_400_000) throw invalid(`The range may not be longer than ${MaxAvailabilityDays} days.`);

  return {
    accessToken: requiredText(input, "accessToken", 4096),
    calendarId: requiredText(input, "calendarId", 320),
    timeMin: from.text,
    timeMax: to.text,
  };
}

export function parseEvent(body: unknown): EventDraft {
  const input = asObject(body);
  checkProvider(input);
  const start = requiredInstant(input, "start");
  const end = requiredInstant(input, "end");
  if (end.ms <= start.ms) throw invalid("end must be after start.");
  if (end.ms - start.ms > MaxEventHours * 3_600_000) throw invalid(`An event may not be longer than ${MaxEventHours} hours.`);

  const eventId = optionalText(input, "eventId", 100);
  if (eventId !== undefined && !GoogleEventId.test(eventId)) {
    throw invalid("eventId must be 5 to 100 characters, each a lowercase letter from a to v or a digit.");
  }

  const rawAttendees = input["attendeeEmails"];
  if (rawAttendees !== undefined && !Array.isArray(rawAttendees)) throw invalid("attendeeEmails must be a list.");
  const attendeeEmails = ((rawAttendees ?? []) as unknown[]).map((entry) => {
    if (typeof entry !== "string" || !Email.test(entry.trim())) throw invalid("Every attendee email must be a valid address.");
    return entry.trim();
  });
  if (attendeeEmails.length > 20) throw invalid("At most 20 attendees are allowed.");

  const timeZone = optionalText(input, "timeZone", 64);
  const description = optionalText(input, "description", 8000);
  const location = optionalText(input, "location", 1024);

  return {
    accessToken: requiredText(input, "accessToken", 4096),
    calendarId: requiredText(input, "calendarId", 320),
    summary: requiredText(input, "summary", 1024),
    start: start.text,
    end: end.text,
    attendeeEmails,
    ...(eventId !== undefined ? { eventId } : {}),
    ...(timeZone !== undefined ? { timeZone } : {}),
    ...(description !== undefined ? { description } : {}),
    ...(location !== undefined ? { location } : {}),
  };
}
