import assert from "node:assert/strict";
import { createServer, type IncomingMessage, type Server, type ServerResponse } from "node:http";
import type { AddressInfo } from "node:net";
import { after, before, describe, it } from "node:test";
import { Server as McpServer } from "@modelcontextprotocol/sdk/server/index.js";
import { StreamableHTTPServerTransport } from "@modelcontextprotocol/sdk/server/streamableHttp.js";
import { CallToolRequestSchema, ErrorCode, ListToolsRequestSchema, McpError } from "@modelcontextprotocol/sdk/types.js";
import { GatewayError } from "../src/errors.ts";
import { createMcpCaller } from "../src/mcp.ts";
import { GoogleCalendar, type Provider } from "../src/providers.ts";

// The caller under test needs somewhere to connect and a name to put in a failure; the stub server below stands in for the vendor.
const at = (mcpUrl: string): Provider => ({ ...GoogleCalendar, mcpUrl });

// A real MCP server over HTTP, standing in for Google's, so the gateway's MCP client is exercised end to end:
// initialize, the bearer header, tools/list, tools/call, tool errors, HTTP refusals and an unreachable server.
describe("MCP client", () => {
  let http: Server;
  let url: string;
  const requests: { authorization: string | undefined; tool?: string; args?: unknown }[] = [];
  let httpStatus: number | null = null;
  let behaviour: "normal" | "never_answers" | "invalid_params" | "no_tools_capability" = "normal";

  async function handle(request: IncomingMessage, response: ServerResponse): Promise<void> {
    if (httpStatus !== null) {
      response.writeHead(httpStatus, { "content-type": "application/json" });
      response.end("{}");
      return;
    }
    if (behaviour === "never_answers") {
      await new Promise<void>((resolve) => response.on("close", () => resolve()));
      return;
    }
    const server = new McpServer({ name: "fake-google", version: "1" }, { capabilities: behaviour === "no_tools_capability" ? {} : { tools: {} } });
    if (behaviour === "no_tools_capability") {
      const transport = new StreamableHTTPServerTransport({ sessionIdGenerator: undefined });
      response.on("close", () => {
        void transport.close();
        void server.close();
      });
      await server.connect(transport);
      await transport.handleRequest(request, response);
      return;
    }
    server.setRequestHandler(ListToolsRequestSchema, async () => ({
      tools: [{ name: "list_calendars", description: "lists", inputSchema: { type: "object", properties: {} } }],
    }));
    server.setRequestHandler(CallToolRequestSchema, async (call) => {
      requests.push({ authorization: request.headers.authorization, tool: call.params.name, args: call.params.arguments });
      if (call.params.name === "boom") return { isError: true, content: [{ type: "text", text: "nope" }] };
      if (behaviour === "invalid_params") throw new McpError(ErrorCode.InvalidParams, "bad arguments");
      return { content: [{ type: "text", text: "ok" }], structuredContent: { echoed: call.params.arguments ?? null } };
    });
    const transport = new StreamableHTTPServerTransport({ sessionIdGenerator: undefined });
    response.on("close", () => {
      void transport.close();
      void server.close();
    });
    await server.connect(transport);
    await transport.handleRequest(request, response);
  }

  before(async () => {
    http = createServer((request, response) => {
      void handle(request, response);
    });
    await new Promise<void>((resolve) => http.listen(0, "127.0.0.1", resolve));
    url = `http://127.0.0.1:${(http.address() as AddressInfo).port}/mcp`;
  });

  after(() => new Promise<void>((resolve) => http.close(() => resolve())));

  it("lists the server's tools", async () => {
    httpStatus = null;
    behaviour = "normal";
    const tools = await createMcpCaller(at(url)).listTools(null);
    assert.deepEqual(tools.map((t) => t.name), ["list_calendars"]);
  });

  it("calls a tool with the bearer token and the arguments, and returns the content", async () => {
    httpStatus = null;
    requests.length = 0;
    const outcome = await createMcpCaller(at(url)).callTool("tok-123", "list_calendars", { pageSize: 5 });

    assert.equal(outcome.isError, false);
    assert.deepEqual(outcome.structuredContent, { echoed: { pageSize: 5 } });
    assert.equal(requests.length, 1);
    assert.equal(requests[0]!.authorization, "Bearer tok-123");
    assert.deepEqual(requests[0]!.args, { pageSize: 5 });
  });

  it("reports a tool's own error as isError", async () => {
    httpStatus = null;
    const outcome = await createMcpCaller(at(url)).callTool("tok", "boom", {});
    assert.equal(outcome.isError, true);
  });

  for (const [status, expectedStatus, code] of [
    [401, 401, "token_rejected"],
    [403, 403, "provider_forbidden"],
    [400, 400, "provider_rejected"],
    [429, 502, "provider_unavailable"],
    [500, 502, "provider_unavailable"],
    [503, 502, "provider_unavailable"],
  ] as const) {
    it(`turns an HTTP ${status} from the server into ${expectedStatus} ${code}`, async () => {
      httpStatus = status;
      await assert.rejects(createMcpCaller(at(url)).callTool("tok", "list_calendars", {}), (error: unknown) => {
        assert.ok(error instanceof GatewayError);
        assert.equal(error.status, expectedStatus);
        assert.equal(error.code, code);
        return true;
      });
    });
  }

  it("gives up on a server that never answers once the one deadline has passed, as 502 provider_unreachable", async () => {
    httpStatus = null;
    behaviour = "never_answers";
    const started = Date.now();
    await assert.rejects(createMcpCaller(at(url), { timeoutMs: 300 }).callTool("tok", "list_calendars", {}), (error: unknown) => {
      assert.ok(error instanceof GatewayError);
      assert.equal(error.status, 502);
      assert.equal(error.code, "provider_unreachable");
      return true;
    });
    assert.ok(Date.now() - started < 2500, "the deadline covers the initialize step too");
    behaviour = "normal";
  });

  it("turns a protocol error for bad arguments into 400 provider_rejected", async () => {
    httpStatus = null;
    behaviour = "invalid_params";
    await assert.rejects(createMcpCaller(at(url)).callTool("tok", "list_calendars", {}), (error: unknown) => {
      assert.ok(error instanceof GatewayError);
      assert.equal(error.status, 400);
      assert.equal(error.code, "provider_rejected");
      return true;
    });
    behaviour = "normal";
  });

  it("turns a server without the tools capability into 400 provider_rejected, never an empty list", async () => {
    httpStatus = null;
    behaviour = "no_tools_capability";
    await assert.rejects(createMcpCaller(at(url)).listTools(null), (error: unknown) => {
      assert.ok(error instanceof GatewayError);
      assert.equal(error.code, "provider_rejected");
      return true;
    });
    behaviour = "normal";
  });

  it("turns an unreachable server into 502 provider_unreachable", async () => {
    httpStatus = null;
    await assert.rejects(createMcpCaller(at("http://127.0.0.1:1/mcp")).callTool("tok", "list_calendars", {}), (error: unknown) => {
      assert.ok(error instanceof GatewayError);
      assert.equal(error.status, 502);
      assert.equal(error.code, "provider_unreachable");
      return true;
    });
  });
});
