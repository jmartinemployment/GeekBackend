import assert from "node:assert/strict";
import { createServer, type Server } from "node:http";
import type { AddressInfo } from "node:net";
import { after, before, describe, it } from "node:test";
import { readConfig } from "../src/config.ts";
import { GatewayError } from "../src/errors.ts";
import type { McpCaller, ToolInfo, ToolOutcome } from "../src/mcp.ts";
import { createHandler, type LogLine } from "../src/server.ts";

const ApiKey = "gateway-key-for-tests";
const Token = "ya29.SECRET-ACCESS-TOKEN";

interface Seen {
  kind: "list" | "call";
  accessToken: string | null;
  tool?: string;
  args?: Record<string, unknown>;
}

describe("gateway", () => {
  let server: Server;
  let base: string;
  let seen: Seen[];
  let logs: LogLine[];
  let onCall: (tool: string, args: Record<string, unknown>) => ToolOutcome | Promise<ToolOutcome>;
  let onList: () => readonly ToolInfo[] | Promise<readonly ToolInfo[]>;

  const mcp: McpCaller = {
    async listTools(accessToken) {
      seen.push({ kind: "list", accessToken });
      return onList();
    },
    async callTool(accessToken, tool, args) {
      seen.push({ kind: "call", accessToken, tool, args });
      return onCall(tool, args);
    },
  };

  before(async () => {
    seen = [];
    logs = [];
    onCall = () => ({ isError: false, content: [], structuredContent: null });
    onList = () => [];
    server = createServer(createHandler({ config: { port: 0, apiKey: ApiKey }, mcp: () => mcp, log: (line) => logs.push(line) }));
    await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve));
    base = `http://127.0.0.1:${(server.address() as AddressInfo).port}`;
  });

  after(() => new Promise<void>((resolve) => server.close(() => resolve())));

  function reset(): void {
    seen = [];
    logs = [];
    onCall = () => ({ isError: false, content: [], structuredContent: null });
    onList = () => [];
  }

  async function post(path: string, body: unknown, key: string | null = ApiKey): Promise<{ status: number; body: any }> {
    const headers: Record<string, string> = { "content-type": "application/json" };
    if (key !== null) headers["x-internal-api-key"] = key;
    const response = await fetch(base + path, { method: "POST", headers, body: typeof body === "string" ? body : JSON.stringify(body) });
    return { status: response.status, body: await response.json() };
  }

  const bookEvent = {
    provider: "google_calendar",
    accessToken: Token,
    tool: "create_event",
    arguments: { summary: "Water heater repair, Dana", startTime: "2026-11-20T14:00:00", endTime: "2026-11-20T15:00:00", timeZone: "America/New_York" },
  };

  it("answers health without a key", async () => {
    assert.equal((await fetch(`${base}/health`)).status, 200);
  });

  it("refuses every other route without the key or with a wrong one, before reading anything", async () => {
    reset();
    for (const key of [null, "wrong", ""]) {
      const result = await post("/v1/mcp/call", bookEvent, key);
      assert.equal(result.status, 401);
      assert.equal(result.body.code, "unauthorized");
    }
    assert.equal((await post("/v1/anything", {}, null)).status, 401);
    assert.equal(seen.length, 0);
  });

  it("lists only the offered tools, without a token", async () => {
    reset();
    onList = () => ["list_events", "create_event", "delete_event", "update_event", "respond_to_event", "search_events", "list_calendars", "get_event", "suggest_time"].map((name) => ({ name, description: null, inputSchema: {} }));
    const result = await post("/v1/mcp/tools", { provider: "google_calendar" });

    assert.equal(result.status, 200);
    assert.deepEqual(result.body.tools.map((t: ToolInfo) => t.name).sort(), ["create_event", "get_event", "list_calendars", "list_events", "suggest_time"]);
    assert.equal(seen[0]!.accessToken, null);
  });

  it("calls the named tool once, with the token and the arguments exactly as sent, and returns what it returned", async () => {
    reset();
    onCall = () => ({ isError: false, content: [{ type: "text", text: "created" }], structuredContent: { id: "evt1" } });
    const result = await post("/v1/mcp/call", bookEvent);

    assert.equal(result.status, 200);
    assert.deepEqual(result.body, { tool: "create_event", content: [{ type: "text", text: "created" }], structuredContent: { id: "evt1" } });
    assert.equal(seen.length, 1);
    assert.equal(seen[0]!.accessToken, Token);
    assert.equal(seen[0]!.tool, "create_event");
    assert.deepEqual(seen[0]!.args, { ...bookEvent.arguments, notificationLevel: "NONE" });
  });

  it("never lets a booking send invitations: attendees and notification settings are refused, and no mail is the forced default", async () => {
    reset();
    for (const extra of [
      { attendees: [{ email: "someone@example.com" }] },
      { attendeeEmails: ["someone@example.com"] },
      { notificationLevel: "ALL" },
      { addGoogleMeetUrl: true },
      { attachments: [{ fileUrl: "https://example.com/x" }] },
      { recurrenceData: ["RRULE:FREQ=DAILY"] },
      { guestPermissions: { guestsCanModify: true } },
    ]) {
      const result = await post("/v1/mcp/call", { ...bookEvent, arguments: { ...bookEvent.arguments, ...extra } });
      assert.equal(result.status, 400, JSON.stringify(extra));
      assert.equal(result.body.code, "argument_not_allowed");
    }
    assert.equal(seen.length, 0);

    await post("/v1/mcp/call", bookEvent);
    assert.equal(seen[0]!.args!["notificationLevel"], "NONE");
  });

  it("offers each tool only the arguments it needs", async () => {
    reset();
    const refused: [string, Record<string, unknown>][] = [
      ["list_events", { fullText: "Dana" }],
      ["list_events", { eventType: ["DEFAULT"] }],
      ["list_calendars", { calendarId: "x" }],
      ["get_event", { notificationLevel: "NONE" }],
      ["suggest_time", { attendeeEmails: ["a@b.co"], extra: 1 }],
    ];
    for (const [tool, args] of refused) {
      const result = await post("/v1/mcp/call", { ...bookEvent, tool, arguments: args });
      assert.equal(result.status, 400, tool);
      assert.equal(result.body.code, "argument_not_allowed");
    }
    assert.equal(seen.length, 0);

    for (const [tool, args] of [
      ["list_events", { calendarId: "primary", startTime: "2026-11-20T08:00:00", endTime: "2026-11-20T18:00:00", timeZone: "America/New_York", pageSize: 10 }],
      ["get_event", { calendarId: "primary", eventId: "abc" }],
      ["list_calendars", {}],
    ] as const) {
      assert.equal((await post("/v1/mcp/call", { ...bookEvent, tool, arguments: args })).status, 200, tool);
    }
  });

  it("limits suggest_time to five attendee emails, each text", async () => {
    reset();
    const base = { ...bookEvent, tool: "suggest_time" };
    const many = Array.from({ length: 6 }, (_, i) => `p${i}@example.com`);
    assert.equal((await post("/v1/mcp/call", { ...base, arguments: { attendeeEmails: many } })).status, 400);
    assert.equal((await post("/v1/mcp/call", { ...base, arguments: { attendeeEmails: "a@b.co" } })).status, 400);
    assert.equal((await post("/v1/mcp/call", { ...base, arguments: { attendeeEmails: [1] } })).status, 400);
    assert.equal(seen.length, 0);
    assert.equal((await post("/v1/mcp/call", { ...base, arguments: { attendeeEmails: many.slice(0, 5) } })).status, 200);
  });

  it("refuses an access token that cannot be put in a header, as a 400 and not a failure to reach Google", async () => {
    reset();
    for (const accessToken of ["ya29.abc\ndef", "ya29 abc", "ya29.\u00e9", "ya29.\u0000x", "ya29.\r\nX-Evil: 1"]) {
      const result = await post("/v1/mcp/call", { ...bookEvent, accessToken });
      assert.equal(result.status, 400, JSON.stringify(accessToken));
      assert.equal(result.body.code, "invalid_request");
    }
    assert.equal(seen.length, 0);
  });

  it("sends empty arguments when none are given", async () => {
    reset();
    await post("/v1/mcp/call", { provider: "google_calendar", accessToken: Token, tool: "list_calendars" });
    assert.deepEqual(seen[0]!.args, {});
  });

  it("answers a tool's own error as 422 tool_error with its content, never as success", async () => {
    reset();
    onCall = () => ({ isError: true, content: [{ type: "text", text: "Event time is in the past." }], structuredContent: null });
    const result = await post("/v1/mcp/call", bookEvent);

    assert.equal(result.status, 422);
    assert.equal(result.body.code, "tool_error");
    assert.deepEqual(result.body.content, [{ type: "text", text: "Event time is in the past." }]);
    assert.equal(seen.length, 1);
  });

  it("answers a tool error that says the credential is bad as 401 token_rejected", async () => {
    reset();
    onCall = () => ({
      isError: true,
      content: [{ type: "text", text: "Request is missing required authentication credential. Expected OAuth 2 access token." }],
      structuredContent: null,
    });
    const result = await post("/v1/mcp/call", bookEvent);

    assert.equal(result.status, 401);
    assert.equal(result.body.code, "token_rejected");
  });

  for (const [thrown, status, code] of [
    [new GatewayError(401, "token_rejected", "x"), 401, "token_rejected"],
    [new GatewayError(403, "provider_forbidden", "x"), 403, "provider_forbidden"],
    [new GatewayError(502, "provider_unavailable", "x"), 502, "provider_unavailable"],
    [new GatewayError(502, "provider_unreachable", "x"), 502, "provider_unreachable"],
  ] as const) {
    it(`passes a ${status} ${code} from the MCP call through, asking once`, async () => {
      reset();
      onCall = () => {
        throw thrown;
      };
      const result = await post("/v1/mcp/call", bookEvent);
      assert.equal(result.status, status);
      assert.equal(result.body.code, code);
      assert.equal(seen.length, 1);
    });
  }

  it("refuses an unsupported provider before calling anyone", async () => {
    reset();
    for (const provider of ["outlook", "cal_com", "hubspot", "Google_Calendar"]) {
      const result = await post("/v1/mcp/call", { ...bookEvent, provider });
      assert.equal(result.status, 400);
      assert.equal(result.body.code, "provider_not_supported");
    }
    assert.equal((await post("/v1/mcp/call", { ...bookEvent, provider: "" })).status, 400);
    assert.equal((await post("/v1/mcp/tools", { provider: "outlook" })).status, 400);
    assert.equal(seen.length, 0);
  });

  it("refuses a tool that is not offered, so a tenant's events cannot be deleted or changed", async () => {
    reset();
    for (const tool of ["delete_event", "update_event", "respond_to_event", "search_events", "nope"]) {
      const result = await post("/v1/mcp/call", { ...bookEvent, tool });
      assert.equal(result.status, 400, tool);
      assert.equal(result.body.code, "tool_not_allowed");
    }
    assert.equal(seen.length, 0);
  });

  it("refuses bad input with a 400 and calls no one", async () => {
    reset();
    const bad: unknown[] = [
      { ...bookEvent, accessToken: "" },
      { ...bookEvent, accessToken: undefined },
      { ...bookEvent, accessToken: "x".repeat(5000) },
      { ...bookEvent, tool: "" },
      { ...bookEvent, tool: undefined },
      { ...bookEvent, arguments: "text" },
      { ...bookEvent, arguments: [] },
      { ...bookEvent, arguments: { summary: "x".repeat(40_000) } },
      [],
      "not json",
    ];
    for (const body of bad) {
      const result = await post("/v1/mcp/call", body);
      assert.equal(result.status, 400, JSON.stringify(body)?.slice(0, 80));
    }
    assert.equal(seen.length, 0);
  });

  it("refuses a body over 64 KB", async () => {
    reset();
    const result = await post("/v1/mcp/call", { ...bookEvent, arguments: { summary: "x".repeat(70_000) } });
    assert.equal(result.status, 413);
    assert.equal(seen.length, 0);
  });

  it("answers 404 for an unknown route and for the wrong method when the key is right", async () => {
    assert.equal((await post("/v1/nope", {})).status, 404);
    assert.equal((await fetch(`${base}/v1/mcp/call`, { method: "GET", headers: { "x-internal-api-key": ApiKey } })).status, 404);
  });

  it("never logs or returns the access token, the key or the event text", async () => {
    reset();
    onCall = () => {
      throw new GatewayError(401, "token_rejected", "Google Calendar did not accept the access token.");
    };
    const result = await post("/v1/mcp/call", bookEvent);
    const everything = JSON.stringify(logs) + JSON.stringify(result.body);
    assert.ok(!everything.includes(Token));
    assert.ok(!everything.includes(ApiKey));
    assert.ok(!everything.includes("Water heater"));
    assert.equal(logs.length, 1);
    assert.equal(logs[0]!.status, 401);
  });

  it("logs how long a tool's error text was, and never the text", async () => {
    reset();
    onCall = () => ({ isError: true, content: [{ type: "text", text: "Event time is in the past." }], structuredContent: null });
    await post("/v1/mcp/call", bookEvent);
    assert.equal(logs[0]!.toolErrorChars, "Event time is in the past.".length);
    assert.ok(!JSON.stringify(logs).includes("Event time"));
  });

  it("gives up on a body that never finishes arriving: the connection is closed within the deadline and the log says 408", async () => {
    reset();
    const slowLogs: LogLine[] = [];
    const slow = createServer(createHandler({ config: { port: 0, apiKey: ApiKey }, mcp: () => mcp, log: (line) => slowLogs.push(line), bodyTimeoutMs: 200 }));
    await new Promise<void>((resolve) => slow.listen(0, "127.0.0.1", resolve));
    const port = (slow.address() as AddressInfo).port;
    const net = await import("node:net");
    const started = Date.now();
    const answer = await new Promise<string>((resolve, reject) => {
      const socket = net.connect(port, "127.0.0.1", () => {
        socket.write(`POST /v1/mcp/call HTTP/1.1\r\nHost: x\r\nx-internal-api-key: ${ApiKey}\r\ncontent-type: application/json\r\ncontent-length: 500\r\n\r\n{"provider"`);
      });
      let received = "";
      socket.on("data", (data) => (received += data.toString()));
      socket.on("close", () => resolve(received));
      socket.on("error", reject);
      setTimeout(() => socket.destroy(), 3000);
    });
    const waited = Date.now() - started;
    await new Promise<void>((resolve) => slow.close(() => resolve()));
    assert.ok(waited < 2000, `the server held the connection for ${waited} ms`);
    assert.equal(slowLogs.length, 1);
    assert.equal(slowLogs[0]!.status, 408);
    assert.equal(slowLogs[0]!.code, "body_timeout");
    assert.equal(seen.length, 0);
    void answer;
  });

  it("reports an unexpected failure as 500 and names it in the log", async () => {
    reset();
    onCall = () => {
      throw new RangeError("boom");
    };
    const result = await post("/v1/mcp/call", bookEvent);
    assert.equal(result.status, 500);
    assert.equal(result.body.code, "internal_error");
    assert.match(logs[0]!.error ?? "", /RangeError: boom/);
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
