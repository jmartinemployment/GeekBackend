import { createHash, timingSafeEqual } from "node:crypto";
import type { IncomingMessage, RequestListener, ServerResponse } from "node:http";
import type { GatewayConfig } from "./config.ts";
import { GatewayError } from "./errors.ts";
import { AllowedTools, type McpCaller } from "./mcp.ts";
import { parseToolCall, parseToolList } from "./validate.ts";

const MaxBodyBytes = 64 * 1024;
const ApiKeyHeader = "x-internal-api-key";

export interface LogLine {
  readonly method: string;
  readonly path: string;
  readonly status: number;
  readonly code: string | null;
  readonly ms: number;
  readonly error?: string;
}

export interface GatewayDependencies {
  readonly config: GatewayConfig;
  readonly mcp: McpCaller;
  readonly log: (line: LogLine) => void;
}

const ExpiredCredential = /authentication credential|invalid[^.]*(token|credential)|unauthenticated|token[^.]*expired/i;

function toolErrorText(content: unknown): string {
  if (!Array.isArray(content)) return "";
  return content.map((part) => (typeof part === "object" && part !== null && "text" in part ? String((part as { text: unknown }).text) : "")).join(" ");
}

function digest(value: string): Buffer {
  return createHash("sha256").update(value).digest();
}

function keyMatches(presented: string | undefined, expected: string): boolean {
  if (presented === undefined) return false;
  return timingSafeEqual(digest(presented), digest(expected));
}

async function readJson(request: IncomingMessage): Promise<unknown> {
  const chunks: Buffer[] = [];
  let size = 0;
  for await (const chunk of request) {
    const piece = chunk as Buffer;
    size += piece.length;
    if (size > MaxBodyBytes) throw new GatewayError(413, "body_too_large", `The body may not be larger than ${MaxBodyBytes} bytes.`);
    chunks.push(piece);
  }
  try {
    return JSON.parse(Buffer.concat(chunks).toString("utf8"));
  } catch {
    throw new GatewayError(400, "invalid_request", "The body is not valid JSON.");
  }
}

function send(response: ServerResponse, status: number, payload: unknown): void {
  const text = JSON.stringify(payload);
  response.writeHead(status, { "content-type": "application/json", "content-length": Buffer.byteLength(text) });
  response.end(text);
}

// Stateless by design: every call carries the tenant's access token, which is used for that one Google call and is never stored,
// logged or returned. Nothing here retries; a failure is a status and a code.
export function createHandler(deps: GatewayDependencies): RequestListener {
  return async (request, response) => {
    const started = performance.now();
    const method = request.method ?? "";
    const path = (request.url ?? "").split("?")[0] ?? "";
    let status = 200;
    let code: string | null = null;
    let unexpected: string | undefined;

    try {
      if (method === "GET" && path === "/health") {
        send(response, 200, { status: "ok" });
        return;
      }

      const presented = request.headers[ApiKeyHeader];
      if (!keyMatches(typeof presented === "string" ? presented : undefined, deps.config.apiKey)) {
        throw new GatewayError(401, "unauthorized", "A valid X-Internal-Api-Key is required.");
      }

      if (method === "POST" && path === "/v1/mcp/tools") {
        parseToolList(await readJson(request));
        const tools = (await deps.mcp.listTools(null)).filter((tool) => AllowedTools.includes(tool.name));
        send(response, 200, { tools });
        return;
      }

      if (method === "POST" && path === "/v1/mcp/call") {
        const call = parseToolCall(await readJson(request));
        const outcome = await deps.mcp.callTool(call.accessToken, call.tool, call.args);
        if (outcome.isError) {
          const text = toolErrorText(outcome.content);
          if (ExpiredCredential.test(text)) throw new GatewayError(401, "token_rejected", "Google Calendar did not accept the access token; it has expired or been revoked.");
          status = 422;
          code = "tool_error";
          send(response, 422, { error: "The tool reported an error.", code: "tool_error", tool: call.tool, content: outcome.content });
          return;
        }
        send(response, 200, { tool: call.tool, content: outcome.content, structuredContent: outcome.structuredContent });
        return;
      }

      throw new GatewayError(404, "not_found", "No such route.");
    } catch (error) {
      if (error instanceof GatewayError) {
        status = error.status;
        code = error.code;
        send(response, error.status, { error: error.message, code: error.code });
      } else {
        status = 500;
        code = "internal_error";
        unexpected = error instanceof Error ? `${error.name}: ${error.message}` : String(error);
        send(response, 500, { error: "The gateway failed.", code: "internal_error" });
      }
    } finally {
      deps.log({ method, path, status, code, ms: Math.round(performance.now() - started), ...(unexpected !== undefined ? { error: unexpected } : {}) });
    }
  };
}
