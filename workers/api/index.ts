import { Container, type StopParams } from "@cloudflare/containers";
import { env } from "cloudflare:workers";

export class LumenCueApi extends Container<Env> {
  defaultPort = 8080;
  requiredPorts = [8080];
  sleepAfter = "10m";
  enableInternet = true;
  envVars = {
    PORT: "8080",
    ASPNETCORE_URLS: "http://0.0.0.0:8080",
    ASPNETCORE_ENVIRONMENT: "Production",
    DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE: "false",
    NEON_CONNECTION_STRING: env.NEON_CONNECTION_STRING,
    APIBIBLE_API_KEY: env.APIBIBLE_API_KEY,
    ELEVENLABS_API_KEY: env.ELEVENLABS_API_KEY,
  };

  override onStart(): void {
    console.log("LumenCue container started; Kestrel should be on 0.0.0.0:8080");
  }

  override onStop(params: StopParams): void {
    console.log(`LumenCue container stopped: exit=${params.exitCode} reason=${params.reason}`);
  }
}

function isRetryableContainerError(error: unknown): boolean {
  const message = error instanceof Error ? error.message : String(error);
  return (
    message.includes("not running") ||
    message.includes("internal error connecting") ||
    message.includes("no container instance")
  );
}

function containerEnv(workerEnv: Env) {
  return {
    PORT: "8080",
    ASPNETCORE_URLS: "http://0.0.0.0:8080",
    ASPNETCORE_ENVIRONMENT: "Production",
    DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE: "false",
    NEON_CONNECTION_STRING: workerEnv.NEON_CONNECTION_STRING,
    APIBIBLE_API_KEY: workerEnv.APIBIBLE_API_KEY,
    ELEVENLABS_API_KEY: workerEnv.ELEVENLABS_API_KEY,
  };
}

export default {
  async fetch(request: Request, workerEnv: Env): Promise<Response> {
    const url = new URL(request.url);
    if (url.pathname === "/worker-env") {
      return Response.json({
        elevenLabsBound: Boolean(workerEnv.ELEVENLABS_API_KEY),
        apiBibleBound: Boolean(workerEnv.APIBIBLE_API_KEY),
        neonBound: Boolean(workerEnv.NEON_CONNECTION_STRING),
      });
    }

    const stub = workerEnv.LUMEN_CUE_API.getByName("singleton");
    const startOptions = { envVars: containerEnv(workerEnv) };
    try {
      await stub.startAndWaitForPorts({ startOptions });
      return await stub.fetch(request);
    } catch (error) {
      if (!isRetryableContainerError(error)) throw error;
      try {
        await stub.startAndWaitForPorts({ startOptions });
        return await stub.fetch(request);
      } catch (retryError) {
        const message = retryError instanceof Error ? retryError.message : String(retryError);
        return new Response(`Container starting: ${message}`, { status: 503 });
      }
    }
  },
} satisfies ExportedHandler<Env>;
