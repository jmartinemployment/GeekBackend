import { createHash, timingSafeEqual } from "node:crypto";
import type { IncomingMessage, RequestListener, ServerResponse } from "node:http";
import type { GatewayConfig } from "./config.ts";
import { GatewayError } from "./errors.ts";
import { insertEvent, queryBusy, type FetchFn } from "./google-calendar.ts";
import { parseAvailability, parseEvent } from "./validate.ts";

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
  readonly fetch: FetchFn;
  readonly log: (line: LogLine) => void;
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

      if (method === "POST" && path === "/v1/calendar/availability") {
        const query = parseAvailability(await readJson(request));
        const busy = await queryBusy(deps.fetch, query);
        send(response, 200, { calendarId: query.calendarId, timeMin: query.timeMin, timeMax: query.timeMax, busy });
        return;
      }

      if (method === "POST" && path === "/v1/calendar/events") {
        const draft = parseEvent(await readJson(request));
        const event = await insertEvent(deps.fetch, draft);
        status = 201;
        send(response, 201, event);
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
