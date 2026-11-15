import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import type { RequestOptions } from "@modelcontextprotocol/sdk/shared/protocol.js";
import { StreamableHTTPClientTransport, StreamableHTTPError } from "@modelcontextprotocol/sdk/client/streamableHttp.js";
import { UnauthorizedError } from "@modelcontextprotocol/sdk/client/auth.js";
import { McpError, ErrorCode } from "@modelcontextprotocol/sdk/types.js";
import { GatewayError } from "./errors.ts";

export const GoogleCalendarMcpUrl = "https://calendarmcp.googleapis.com/mcp/v1";
const DefaultTimeoutMs = 20_000;

// The only tools a receptionist needs to read a calendar and book on it, and for each the only arguments it may send. Deleting,
// changing or answering invitations on a tenant's events is not offered until a feature needs it. create_event takes no attendees,
// attachments, meeting links or recurrence: those would make the owner's calendar send mail to, or share files with, whoever the
// caller names. Anything not listed here is refused before Google is called.
export const ToolArguments: Readonly<Record<string, readonly string[]>> = {
  list_calendars: ["pageSize", "pageToken"],
  list_events: ["calendarId", "startTime", "endTime", "timeZone", "orderBy", "pageSize", "pageToken"],
  get_event: ["calendarId", "eventId"],
  suggest_time: ["attendeeEmails", "durationMinutes", "startTime", "endTime", "timeZone", "preferences"],
  create_event: ["calendarId", "summary", "startTime", "endTime", "timeZone", "description", "location"],
};

export const AllowedTools: readonly string[] = Object.keys(ToolArguments);

// Added to a tool's arguments by the gateway, after the caller's, so the caller cannot override them. A booking sends no invitations.
export const ForcedArguments: Readonly<Record<string, Readonly<Record<string, unknown>>>> = {
  create_event: { notificationLevel: "NONE" },
};

export interface ToolInfo {
  readonly name: string;
  readonly description: string | null;
  readonly inputSchema: unknown;
}

export interface ToolOutcome {
  readonly isError: boolean;
  readonly content: unknown;
  readonly structuredContent: unknown;
}

export interface McpCaller {
  listTools(accessToken: string | null): Promise<readonly ToolInfo[]>;
  callTool(accessToken: string, tool: string, args: Record<string, unknown>): Promise<ToolOutcome>;
}

// One MCP session per request, opened, used once and closed; nothing is kept between requests, and nothing is retried (the SDK's
// stream reconnection is switched off).
export interface McpCallerOptions {
  readonly timeoutMs?: number;
  readonly warn?: (message: string) => void;
}

// One deadline covers the whole call, the initialize step and the notification included, so a slow Google cannot take longer than
// timeoutMs however many steps the session needs.
export function createMcpCaller(url: string, options: McpCallerOptions = {}): McpCaller {
  const timeoutMs = options.timeoutMs ?? DefaultTimeoutMs;
  const warn = options.warn ?? (() => undefined);

  async function withSession<T>(accessToken: string | null, use: (client: Client, request: RequestOptions) => Promise<T>): Promise<T> {
    const request: RequestOptions = { timeout: timeoutMs, signal: AbortSignal.timeout(timeoutMs) };
    const transport = openTransport(url, accessToken);
    const client = new Client({ name: "geek-mcp-gateway", version: "1.0.0" });
    try {
      await client.connect(transport, request);
      return await use(client, request);
    } catch (error) {
      throw translate(error);
    } finally {
      await client.close().catch((closeError: unknown) => {
        warn(`closing the MCP session failed: ${closeError instanceof Error ? closeError.name : "unknown error"}`);
      });
    }
  }

  return {
    listTools: (accessToken) =>
      withSession(accessToken, async (client, request) => {
        const listed = await client.listTools(undefined, request);
        return listed.tools.map((tool) => ({ name: tool.name, description: tool.description ?? null, inputSchema: tool.inputSchema }));
      }),

    callTool: (accessToken, tool, args) =>
      withSession(accessToken, async (client, request) => {
        const result = await client.callTool({ name: tool, arguments: args }, undefined, request);
        return { isError: result["isError"] === true, content: result["content"] ?? null, structuredContent: result["structuredContent"] ?? null };
      }),
  };
}

function openTransport(url: string, accessToken: string | null): StreamableHTTPClientTransport {
  return new StreamableHTTPClientTransport(new URL(url), {
    requestInit: accessToken === null ? {} : { headers: { authorization: `Bearer ${accessToken}` } },
    reconnectionOptions: { maxRetries: 0, initialReconnectionDelay: 1, maxReconnectionDelay: 1, reconnectionDelayGrowFactor: 1 },
  });
}

function translate(error: unknown): GatewayError {
  if (error instanceof GatewayError) return error;
  if (error instanceof Error && (error.name === "AbortError" || error.name === "TimeoutError")) {
    return new GatewayError(502, "provider_unreachable", "Google Calendar did not answer in time.");
  }
  if (error instanceof UnauthorizedError) {
    return new GatewayError(401, "token_rejected", "Google Calendar did not accept the access token; it has expired or been revoked.");
  }
  if (error instanceof StreamableHTTPError) {
    const status = error.code;
    if (status === 401) return new GatewayError(401, "token_rejected", "Google Calendar did not accept the access token; it has expired or been revoked.");
    if (status === 403) return new GatewayError(403, "provider_forbidden", "Google Calendar refused this account.");
    if (status === 400) return new GatewayError(400, "provider_rejected", "Google Calendar rejected the request.");
    return new GatewayError(502, "provider_unavailable", `Google Calendar failed with status ${status ?? "unknown"}.`);
  }
  if (error instanceof McpError) {
    if (error.code === ErrorCode.InvalidParams || error.code === ErrorCode.MethodNotFound) {
      return new GatewayError(400, "provider_rejected", "Google Calendar rejected the tool call.");
    }
    if (error.code === ErrorCode.RequestTimeout) {
      return new GatewayError(502, "provider_unreachable", "Google Calendar did not answer in time.");
    }
    return new GatewayError(502, "provider_unavailable", "Google Calendar answered with a protocol error.");
  }
  return new GatewayError(502, "provider_unreachable", "Google Calendar could not be reached.");
}
