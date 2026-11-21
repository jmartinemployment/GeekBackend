import { createServer } from "node:http";
import { readConfig } from "./config.ts";
import { createMcpCaller, type McpCaller } from "./mcp.ts";
import { Providers, type Provider } from "./providers.ts";
import { createHandler } from "./server.ts";

const config = readConfig(process.env);

const warn = (message: string): void => console.warn(JSON.stringify({ level: "warn", message }));

// One caller per provider, built once at startup. A caller holds no session and no token - it only knows where to open one and what
// to call that system in a failure - so building them up front costs nothing and means an unknown provider cannot reach this far.
const callers = new Map<string, McpCaller>(Providers.map((provider) => [provider.code, createMcpCaller(provider, { warn })]));

function callerFor(provider: Provider): McpCaller {
  const caller = callers.get(provider.code);
  if (caller === undefined) throw new Error(`No MCP caller was built for ${provider.code}.`);
  return caller;
}

const server = createServer(
  createHandler({
    config,
    mcp: callerFor,
    log: (line) => console.log(JSON.stringify(line)),
  }),
);

server.listen(config.port, "0.0.0.0", () => {
  console.log(JSON.stringify({ event: "listening", port: config.port }));
});
