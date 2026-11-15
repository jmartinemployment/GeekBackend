import { GatewayError } from "./errors.ts";
import { AllowedTools } from "./mcp.ts";

export const SupportedProvider = "google_calendar";
const MaxArgumentBytes = 32 * 1024;

function invalid(message: string): GatewayError {
  return new GatewayError(400, "invalid_request", message);
}

function asObject(body: unknown): Record<string, unknown> {
  if (typeof body !== "object" || body === null || Array.isArray(body)) throw invalid("A JSON object is required.");
  return body as Record<string, unknown>;
}

function checkProvider(body: Record<string, unknown>): void {
  const provider = body["provider"];
  if (typeof provider !== "string" || provider.trim() === "") throw invalid("provider is required.");
  if (provider !== SupportedProvider) {
    throw new GatewayError(400, "provider_not_supported", `Only ${SupportedProvider} is supported so far.`);
  }
}

export function parseToolList(body: unknown): void {
  checkProvider(asObject(body));
}

export interface ToolCall {
  readonly accessToken: string;
  readonly tool: string;
  readonly args: Record<string, unknown>;
}

export function parseToolCall(body: unknown): ToolCall {
  const input = asObject(body);
  checkProvider(input);

  const accessToken = input["accessToken"];
  if (typeof accessToken !== "string" || accessToken.trim() === "") throw invalid("accessToken is required.");
  if (accessToken.length > 4096) throw invalid("accessToken is longer than 4096 characters.");

  const tool = input["tool"];
  if (typeof tool !== "string" || tool.trim() === "") throw invalid("tool is required.");
  if (!AllowedTools.includes(tool)) {
    throw new GatewayError(400, "tool_not_allowed", `The tool ${tool} is not offered; the offered tools are ${AllowedTools.join(", ")}.`);
  }

  const rawArguments = input["arguments"] ?? {};
  if (typeof rawArguments !== "object" || rawArguments === null || Array.isArray(rawArguments)) throw invalid("arguments must be a JSON object.");
  if (JSON.stringify(rawArguments).length > MaxArgumentBytes) throw invalid(`arguments may not be larger than ${MaxArgumentBytes} characters.`);

  return { accessToken: accessToken.trim(), tool, args: rawArguments as Record<string, unknown> };
}
