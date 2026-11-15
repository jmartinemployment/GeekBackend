import { GatewayError } from "./errors.ts";
import { AllowedTools, ForcedArguments, ToolArguments } from "./mcp.ts";

export const SupportedProvider = "google_calendar";
const MaxArgumentBytes = 32 * 1024;
const MaxSuggestAttendees = 5;
// Printable ASCII only. Anything else (a newline, a space, a control character) cannot be in an Authorization header and would
// otherwise surface later as a failure to reach Google.
const HeaderSafeToken = /^[\x21-\x7e]+$/;

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
  if (!HeaderSafeToken.test(accessToken.trim())) throw invalid("accessToken contains characters that cannot be sent in a header.");

  const tool = input["tool"];
  if (typeof tool !== "string" || tool.trim() === "") throw invalid("tool is required.");
  if (!AllowedTools.includes(tool)) {
    throw new GatewayError(400, "tool_not_allowed", `The tool ${tool} is not offered; the offered tools are ${AllowedTools.join(", ")}.`);
  }

  const rawArguments = input["arguments"] ?? {};
  if (typeof rawArguments !== "object" || rawArguments === null || Array.isArray(rawArguments)) throw invalid("arguments must be a JSON object.");
  if (JSON.stringify(rawArguments).length > MaxArgumentBytes) throw invalid(`arguments may not be larger than ${MaxArgumentBytes} characters.`);

  const args = rawArguments as Record<string, unknown>;
  const permitted = ToolArguments[tool] ?? [];
  for (const key of Object.keys(args)) {
    if (!permitted.includes(key)) {
      throw new GatewayError(
        400,
        "argument_not_allowed",
        `The argument ${key} is not offered for ${tool}; the offered arguments are ${permitted.join(", ")}. Invitations, attendees, attachments and recurrence are never sent.`,
      );
    }
  }

  if (tool === "suggest_time" && args["attendeeEmails"] !== undefined) {
    const emails = args["attendeeEmails"];
    if (!Array.isArray(emails) || emails.length > MaxSuggestAttendees || emails.some((email) => typeof email !== "string")) {
      throw invalid(`attendeeEmails must be a list of at most ${MaxSuggestAttendees} email addresses.`);
    }
  }

  return { accessToken: accessToken.trim(), tool, args: { ...args, ...(ForcedArguments[tool] ?? {}) } };
}
