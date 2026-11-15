import assert from "node:assert/strict";
import { createServer, type Server } from "node:http";
import type { AddressInfo } from "node:net";
import { after, before, describe, it } from "node:test";
import { readConfig } from "../src/config.ts";
import type { FetchFn } from "../src/google-calendar.ts";
import { createHandler, type LogLine } from "../src/server.ts";

const ApiKey = "gateway-key-for-tests";
const Token = "ya29.SECRET-ACCESS-TOKEN";

interface Sent {
  url: string;
  authorization: string | null;
  body: unknown;
}

function json(status: number, payload: unknown): Response {
  return new Response(JSON.stringify(payload), { status, headers: { "content-type": "application/json" } });
}

describe("gateway", () => {
  let server: Server;
  let base: string;
  let sent: Sent[];
  let logs: LogLine[];
  let google: (call: Sent) => Response | Promise<Response>;

  const fakeFetch: FetchFn = async (url, init) => {
    const call: Sent = {
      url,
      authorization: new Headers(init.headers).get("authorization"),
      body: JSON.parse(String(init.body)),
    };
    sent.push(call);
    return google(call);
  };

  before(async () => {
    sent = [];
    logs = [];
    google = () => json(200, {});
    server = createServer(createHandler({ config: { port: 0, apiKey: ApiKey }, fetch: fakeFetch, log: (line) => logs.push(line) }));
    await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve));
    base = `http://127.0.0.1:${(server.address() as AddressInfo).port}`;
  });

  after(() => new Promise<void>((resolve) => server.close(() => resolve())));

  function reset(answer: (call: Sent) => Response | Promise<Response>): void {
    sent = [];
    logs = [];
    google = answer;
  }

  async function post(path: string, body: unknown, key: string | null = ApiKey): Promise<{ status: number; body: any }> {
    const headers: Record<string, string> = { "content-type": "application/json" };
    if (key !== null) headers["x-internal-api-key"] = key;
    const response = await fetch(base + path, { method: "POST", headers, body: typeof body === "string" ? body : JSON.stringify(body) });
    return { status: response.status, body: await response.json() };
  }

  const availability = {
    provider: "google_calendar",
    accessToken: Token,
    calendarId: "primary",
    timeMin: "2026-11-20T08:00:00-05:00",
    timeMax: "2026-11-20T18:00:00-05:00",
  };

  const event = {
    provider: "google_calendar",
    accessToken: Token,
    calendarId: "primary",
    summary: "Water heater repair, Dana",
    start: "2026-11-20T14:00:00-05:00",
    end: "2026-11-20T15:00:00-05:00",
    timeZone: "America/New_York",
  };

  it("answers health without a key", async () => {
    const response = await fetch(`${base}/health`);
    assert.equal(response.status, 200);
  });

  it("refuses every other route without the key or with a wrong one, before reading anything", async () => {
    reset(() => json(200, {}));
    for (const key of [null, "wrong", ""]) {
      const result = await post("/v1/calendar/availability", availability, key);
      assert.equal(result.status, 401);
      assert.equal(result.body.code, "unauthorized");
    }
    assert.equal((await post("/v1/anything", {}, null)).status, 401);
    assert.equal(sent.length, 0);
  });

  it("returns the busy times Google reports and asks Google once with the bearer token", async () => {
    reset(() => json(200, { calendars: { primary: { busy: [{ start: "2026-11-20T15:00:00Z", end: "2026-11-20T16:00:00Z" }] } } }));
    const result = await post("/v1/calendar/availability", availability);

    assert.equal(result.status, 200);
    assert.deepEqual(result.body.busy, [{ start: "2026-11-20T15:00:00Z", end: "2026-11-20T16:00:00Z" }]);
    assert.equal(sent.length, 1);
    assert.equal(sent[0]!.url, "https://www.googleapis.com/calendar/v3/freeBusy");
    assert.equal(sent[0]!.authorization, `Bearer ${Token}`);
    assert.deepEqual(sent[0]!.body, { timeMin: availability.timeMin, timeMax: availability.timeMax, items: [{ id: "primary" }] });
  });

  it("returns an empty list for a free calendar, not a failure", async () => {
    reset(() => json(200, { calendars: { primary: { busy: [] } } }));
    const result = await post("/v1/calendar/availability", availability);
    assert.equal(result.status, 200);
    assert.deepEqual(result.body.busy, []);
  });

  it("refuses an answer that lacks the calendar or its busy list instead of calling it free", async () => {
    reset(() => json(200, { calendars: {} }));
    assert.equal((await post("/v1/calendar/availability", availability)).status, 502);
    reset(() => json(200, { calendars: { primary: {} } }));
    assert.equal((await post("/v1/calendar/availability", availability)).status, 502);
  });

  it("maps a calendar-level error from freeBusy", async () => {
    reset(() => json(200, { calendars: { primary: { busy: [], errors: [{ reason: "notFound" }] } } }));
    const missing = await post("/v1/calendar/availability", availability);
    assert.equal(missing.status, 404);
    assert.equal(missing.body.code, "calendar_not_found");

    reset(() => json(200, { calendars: { primary: { busy: [], errors: [{ reason: "forbidden" }] } } }));
    assert.equal((await post("/v1/calendar/availability", availability)).status, 403);
  });

  it("creates an event and returns its id, link and times", async () => {
    reset(() =>
      json(200, { id: "abc123", htmlLink: "https://calendar.google.com/event?eid=abc", start: { dateTime: event.start }, end: { dateTime: event.end } }),
    );
    const result = await post("/v1/calendar/events", { ...event, eventId: "call0001abc", location: "12 Palm Way", attendeeEmails: ["dana@example.com"] });

    assert.equal(result.status, 201);
    assert.equal(result.body.eventId, "abc123");
    assert.equal(sent.length, 1);
    assert.equal(sent[0]!.url, "https://www.googleapis.com/calendar/v3/calendars/primary/events?sendUpdates=none");
    const body = sent[0]!.body as any;
    assert.equal(body.summary, event.summary);
    assert.equal(body.id, "call0001abc");
    assert.deepEqual(body.start, { dateTime: event.start, timeZone: "America/New_York" });
    assert.deepEqual(body.attendees, [{ email: "dana@example.com" }]);
    assert.equal(body.location, "12 Palm Way");
  });

  it("encodes a calendar id that is an email address", async () => {
    reset(() => json(200, { id: "x", start: { dateTime: event.start }, end: { dateTime: event.end } }));
    await post("/v1/calendar/events", { ...event, calendarId: "owner@example.com" });
    assert.ok(sent[0]!.url.includes("/calendars/owner%40example.com/events"));
  });

  it("refuses a created answer without the event instead of calling it booked", async () => {
    reset(() => json(200, {}));
    const result = await post("/v1/calendar/events", event);
    assert.equal(result.status, 502);
    assert.equal(result.body.code, "provider_unavailable");
  });

  for (const [googleStatus, status, code] of [
    [400, 400, "provider_rejected"],
    [401, 401, "token_rejected"],
    [403, 403, "provider_forbidden"],
    [404, 404, "calendar_not_found"],
    [409, 409, "event_exists"],
    [429, 502, "provider_unavailable"],
    [500, 502, "provider_unavailable"],
    [503, 502, "provider_unavailable"],
  ] as const) {
    it(`maps Google ${googleStatus} to ${status} ${code}, asks once and never retries`, async () => {
      reset(() => json(googleStatus, { error: { message: "internal detail", errors: [{ reason: "someReason" }] } }));
      const result = await post("/v1/calendar/events", event);
      assert.equal(result.status, status);
      assert.equal(result.body.code, code);
      assert.equal(sent.length, 1);
      assert.ok(!JSON.stringify(result.body).includes("internal detail"));
    });
  }

  it("treats a Google rate limit on 403 as unavailable, not forbidden", async () => {
    reset(() => json(403, { error: { errors: [{ reason: "rateLimitExceeded" }] } }));
    const result = await post("/v1/calendar/events", event);
    assert.equal(result.status, 502);
  });

  it("answers 502 provider_unreachable when Google cannot be reached", async () => {
    reset(() => {
      throw new TypeError("fetch failed");
    });
    const result = await post("/v1/calendar/events", event);
    assert.equal(result.status, 502);
    assert.equal(result.body.code, "provider_unreachable");
  });

  it("refuses an unsupported provider before calling anyone", async () => {
    reset(() => json(200, {}));
    for (const provider of ["outlook", "cal_com", "hubspot", "", "Google_Calendar"]) {
      const result = await post("/v1/calendar/events", { ...event, provider });
      assert.equal(result.status, 400);
    }
    assert.equal((await post("/v1/calendar/events", { ...event, provider: "outlook" })).body.code, "provider_not_supported");
    assert.equal(sent.length, 0);
  });

  it("refuses bad input with a 400 and calls no one", async () => {
    reset(() => json(200, {}));
    const bad: [string, unknown][] = [
      ["/v1/calendar/availability", { ...availability, accessToken: "" }],
      ["/v1/calendar/availability", { ...availability, calendarId: undefined }],
      ["/v1/calendar/availability", { ...availability, timeMin: "2026-11-20" }],
      ["/v1/calendar/availability", { ...availability, timeMin: "2026-11-20T18:00:00-05:00", timeMax: "2026-11-20T08:00:00-05:00" }],
      ["/v1/calendar/availability", { ...availability, timeMax: "2027-03-20T18:00:00-05:00" }],
      ["/v1/calendar/events", { ...event, summary: "  " }],
      ["/v1/calendar/events", { ...event, end: event.start }],
      ["/v1/calendar/events", { ...event, end: "2026-11-22T15:00:00-05:00" }],
      ["/v1/calendar/events", { ...event, eventId: "UPPER" }],
      ["/v1/calendar/events", { ...event, attendeeEmails: ["nope"] }],
      ["/v1/calendar/events", { ...event, attendeeEmails: "dana@example.com" }],
      ["/v1/calendar/events", []],
      ["/v1/calendar/events", "not json"],
    ];
    for (const [path, body] of bad) {
      const result = await post(path, body);
      assert.equal(result.status, 400, JSON.stringify(body));
    }
    assert.equal(sent.length, 0);
  });

  it("refuses a body over 64 KB", async () => {
    reset(() => json(200, {}));
    const result = await post("/v1/calendar/events", { ...event, description: "x".repeat(70_000) });
    assert.equal(result.status, 413);
    assert.equal(sent.length, 0);
  });

  it("answers 404 for an unknown route and 404 for the wrong method when the key is right", async () => {
    assert.equal((await post("/v1/nope", {})).status, 404);
    const response = await fetch(`${base}/v1/calendar/events`, { method: "GET", headers: { "x-internal-api-key": ApiKey } });
    assert.equal(response.status, 404);
  });

  it("never logs or returns the access token, the key or the event text", async () => {
    reset(() => json(401, { error: { errors: [{ reason: "authError" }] } }));
    const result = await post("/v1/calendar/events", event);
    const everything = JSON.stringify(logs) + JSON.stringify(result.body);
    assert.ok(!everything.includes(Token));
    assert.ok(!everything.includes(ApiKey));
    assert.ok(!everything.includes(event.summary));
    assert.equal(logs.length, 1);
    assert.equal(logs[0]!.status, 401);
    assert.equal(logs[0]!.code, "token_rejected");
  });

  it("treats an answer that is not a JSON object as unavailable, never as success", async () => {
    for (const text of ["{not json", "null", "[]", "\"text\""]) {
      reset(() => new Response(text, { status: 200 }));
      const result = await post("/v1/calendar/events", event);
      assert.equal(result.status, 502, text);
      assert.equal(result.body.code, "provider_unavailable");
    }
  });
});

describe("config", () => {
  it("will not start without the key", () => {
    assert.throws(() => readConfig({}), /MCP_GATEWAY_API_KEY/);
    assert.throws(() => readConfig({ MCP_GATEWAY_API_KEY: "  " }), /MCP_GATEWAY_API_KEY/);
  });

  it("defaults the port to 3000 and refuses a bad one", () => {
    assert.equal(readConfig({ MCP_GATEWAY_API_KEY: "k" }).port, 3000);
    assert.equal(readConfig({ MCP_GATEWAY_API_KEY: "k", PORT: "8080" }).port, 8080);
    assert.throws(() => readConfig({ MCP_GATEWAY_API_KEY: "k", PORT: "abc" }), /PORT/);
    assert.throws(() => readConfig({ MCP_GATEWAY_API_KEY: "k", PORT: "70000" }), /PORT/);
  });
});
