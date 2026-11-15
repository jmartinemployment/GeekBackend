import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { StreamableHTTPClientTransport, StreamableHTTPError } from "@modelcontextprotocol/sdk/client/streamableHttp.js";
import { UnauthorizedError } from "@modelcontextprotocol/sdk/client/auth.js";
import { McpError, ErrorCode } from "@modelcontextprotocol/sdk/types.js";
import { GatewayError } from "./errors.ts";

export const GoogleCalendarMcpUrl = "https://calendarmcp.googleapis.com/mcp/v1";
const McpTimeoutMs = 20_000;

// The only tools a receptionist needs to read a calendar and book on it. Deleting, changing or answering invitations on a
// tenant's events is not offered until a feature needs it.
export const AllowedTools: readonly string[] = ["list_calendars", "list_events", "get_event", "suggest_time", "create_event"];

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
export function createMcpCaller(url: string): McpCaller {
  async function withSession<T>(accessToken: string | null, use: (client: Client) => Promise<T>): Promise<T> {
    const transport = openTransport(url, accessToken);
    const client = new Client({ name: "geek-mcp-gateway", version: "1.0.0" });
    try {
      await client.connect(transport);
      return await use(client);
    } catch (error) {
      throw translate(error);
    } finally {
      await client.close().catch(() => undefined);
    }
  }

  return {
    listTools: (accessToken) =>
      withSession(accessToken, async (client) => {
        const listed = await client.listTools(undefined, { timeout: McpTimeoutMs });
        return listed.tools.map((tool) => ({ name: tool.name, description: tool.description ?? null, inputSchema: tool.inputSchema }));
      }),

    callTool: (accessToken, tool, args) =>
      withSession(accessToken, async (client) => {
        const result = await client.callTool({ name: tool, arguments: args }, undefined, { timeout: McpTimeoutMs });
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
