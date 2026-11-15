export interface GatewayConfig {
  readonly port: number;
  readonly apiKey: string;
}

export function readConfig(env: NodeJS.ProcessEnv): GatewayConfig {
  const apiKey = env["MCP_GATEWAY_API_KEY"]?.trim();
  if (!apiKey) {
    throw new Error("MCP_GATEWAY_API_KEY is not set; the gateway will not start without it.");
  }

  const rawPort = env["PORT"]?.trim() ?? "3000";
  const port = Number(rawPort);
  if (!Number.isInteger(port) || port < 1 || port > 65535) {
    throw new Error(`PORT must be a whole number from 1 to 65535; it is "${rawPort}".`);
  }

  return { port, apiKey };
}
