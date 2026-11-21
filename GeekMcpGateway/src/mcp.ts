import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import type { RequestOptions } from "@modelcontextprotocol/sdk/shared/protocol.js";
import { StreamableHTTPClientTransport, StreamableHTTPError } from "@modelcontextprotocol/sdk/client/streamableHttp.js";
import { UnauthorizedError } from "@modelcontextprotocol/sdk/client/auth.js";
import { McpError, ErrorCode } from "@modelcontextprotocol/sdk/types.js";
import { GatewayError } from "./errors.ts";
import type { Provider } from "./providers.ts";

const DefaultTimeoutMs = 20_000;

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
export function createMcpCaller(provider: Provider, options: McpCallerOptions = {}): McpCaller {
  const timeoutMs = options.timeoutMs ?? DefaultTimeoutMs;
  const warn = options.warn ?? (() => undefined);
  const url = provider.mcpUrl;
  const name = provider.displayName;

  async function withSession<T>(accessToken: string | null, use: (client: Client, request: RequestOptions) => Promise<T>): Promise<T> {
    const request: RequestOptions = { timeout: timeoutMs, signal: AbortSignal.timeout(timeoutMs) };
    const transport = openTransport(url, accessToken);
    const client = new Client({ name: "geek-mcp-gateway", version: "1.0.0" });
    try {
      await client.connect(transport, request);
      return await use(client, request);
    } catch (error) {
      throw translate(error, name);
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

function translate(error: unknown, name: string): GatewayError {
  if (error instanceof GatewayError) return error;
  if (error instanceof Error && (error.name === "AbortError" || error.name === "TimeoutError")) {
    return new GatewayError(502, "provider_unreachable", `${name} did not answer in time.`);
  }
  if (error instanceof UnauthorizedError) {
    return new GatewayError(401, "token_rejected", `${name} did not accept the access token; it has expired or been revoked.`);
  }
  if (error instanceof StreamableHTTPError) {
    const status = error.code;
    if (status === 401) return new GatewayError(401, "token_rejected", `${name} did not accept the access token; it has expired or been revoked.`);
    if (status === 403) return new GatewayError(403, "provider_forbidden", `${name} refused this account.`);
    if (status === 400) return new GatewayError(400, "provider_rejected", `${name} rejected the request.`);
    return new GatewayError(502, "provider_unavailable", `${name} failed with status ${status ?? "unknown"}.`);
  }
  if (error instanceof McpError) {
    if (error.code === ErrorCode.InvalidParams || error.code === ErrorCode.MethodNotFound) {
      return new GatewayError(400, "provider_rejected", `${name} rejected the tool call.`);
    }
    if (error.code === ErrorCode.RequestTimeout) {
      return new GatewayError(502, "provider_unreachable", `${name} did not answer in time.`);
    }
    return new GatewayError(502, "provider_unavailable", `${name} answered with a protocol error.`);
  }
  return new GatewayError(502, "provider_unreachable", `${name} could not be reached.`);
}
