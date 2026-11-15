import { createServer } from "node:http";
import { readConfig } from "./config.ts";
import { createMcpCaller, GoogleCalendarMcpUrl } from "./mcp.ts";
import { createHandler } from "./server.ts";

const config = readConfig(process.env);

const server = createServer(
  createHandler({
    config,
    mcp: createMcpCaller(GoogleCalendarMcpUrl, { warn: (message) => console.warn(JSON.stringify({ level: "warn", message })) }),
    log: (line) => console.log(JSON.stringify(line)),
  }),
);

server.listen(config.port, "0.0.0.0", () => {
  console.log(JSON.stringify({ event: "listening", port: config.port }));
});
