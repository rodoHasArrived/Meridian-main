import path from "node:path";
import { readFileSync } from "node:fs";
import type { IncomingMessage, ServerResponse } from "node:http";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";
import type { Plugin, ProxyOptions } from "vite";
import { resolveDevFixture } from "./src/lib/dev-fixtures";
import { COVERED_CALL_API_ENDPOINTS, QUANT_API_ENDPOINTS, WORKSTATION_API_ENDPOINTS } from "./src/lib/workstation-endpoints";

export const defaultMeridianApiBaseUrl = "http://localhost:8080";
export const meridianDevFixtureHeader = "x-meridian-dev-fixture";
export const meridianApiAvailabilityCacheMs = 2_000;
export const meridianApiAvailabilityTimeoutMs = 200;
export const meridianScreenshotCaptureEnv = "MERIDIAN_SCREENSHOT_CAPTURE";
export const meridianDevSessionHeader = "x-meridian-dev-session";
export type MeridianDevMode = "fixture-only" | "backend-connected";

export interface MeridianApiAvailabilityProbe {
  isAvailable: () => Promise<boolean>;
}

export function resolveMeridianApiBaseUrl(env: NodeJS.ProcessEnv = process.env): string {
  const configured = env.MERIDIAN_API_BASE_URL ?? env.VITE_MERIDIAN_API_BASE_URL;
  return (configured?.trim() || defaultMeridianApiBaseUrl).replace(/\/+$/, "");
}

export function resolveMeridianDevMode(env: NodeJS.ProcessEnv = process.env): MeridianDevMode | undefined {
  const serverMode = env.MERIDIAN_DEV_MODE?.trim() || undefined;
  const clientMode = env.VITE_MERIDIAN_DEV_MODE?.trim() || undefined;
  for (const mode of [serverMode, clientMode]) {
    if (mode !== undefined && mode !== "fixture-only" && mode !== "backend-connected") {
      throw new Error(`Invalid Meridian development mode "${mode}". Use fixture-only or backend-connected.`);
    }
  }
  if (serverMode && clientMode && serverMode !== clientMode) {
    throw new Error("MERIDIAN_DEV_MODE and VITE_MERIDIAN_DEV_MODE must select the same development mode.");
  }
  return (serverMode ?? clientMode) as MeridianDevMode | undefined;
}

export function resolveViteHmrConfig(env: NodeJS.ProcessEnv = process.env): false | undefined {
  const value = env[meridianScreenshotCaptureEnv]?.trim().toLowerCase();
  return value === "1" || value === "true" || value === "yes" ? false : undefined;
}

export function createMeridianDevSessionPlugin(env: NodeJS.ProcessEnv = process.env): Plugin {
  const session = env.MERIDIAN_DEV_SESSION?.trim();
  return {
    name: "meridian-dev-session",
    apply: "serve",
    configureServer(server) {
      if (session) {
        server.middlewares.use((_req, res, next) => {
          res.setHeader(meridianDevSessionHeader, session);
          next();
        });
      }
    }
  };
}

export function createMeridianApiAvailabilityProbe(
  target = resolveMeridianApiBaseUrl(),
  {
    cacheMs = meridianApiAvailabilityCacheMs,
    timeoutMs = meridianApiAvailabilityTimeoutMs,
    fetchImpl = globalThis.fetch,
    now = () => Date.now()
  }: {
    cacheMs?: number;
    timeoutMs?: number;
    fetchImpl?: typeof fetch;
    now?: () => number;
  } = {}
): MeridianApiAvailabilityProbe {
  let cached: { checkedAt: number; available: boolean } | null = null;
  let pending: Promise<boolean> | null = null;

  return {
    async isAvailable() {
      const currentTime = now();
      if (cached && currentTime - cached.checkedAt < cacheMs) {
        return cached.available;
      }

      if (pending) {
        return pending;
      }

      pending = probeMeridianApiTarget(target, timeoutMs, fetchImpl)
        .then((available) => {
          cached = { checkedAt: now(), available };
          return available;
        })
        .finally(() => {
          pending = null;
        });

      return pending;
    }
  };
}

export function createMeridianApiFallbackBypass(
  target = resolveMeridianApiBaseUrl(),
  availabilityProbe: MeridianApiAvailabilityProbe = createMeridianApiAvailabilityProbe(target),
  mode = resolveMeridianDevMode()
): NonNullable<ProxyOptions["bypass"]> {
  return async (req, res) => {
    if (!res || mode === "backend-connected") {
      return undefined;
    }

    const fixture = isDevelopmentFixtureRequest(req) ? resolveDevFixture<unknown>(req.url ?? "") : undefined;
    if (mode === "fixture-only") {
      if (fixture === undefined) {
        writeUnsupportedFixtureResponse(req, res);
      } else {
        writeDevelopmentFixtureResponse(req, res, fixture);
      }
      return req.url ?? "";
    }

    if (fixture === undefined || await availabilityProbe.isAvailable()) {
      return undefined;
    }

    writeDevelopmentFixtureResponse(req, res, fixture);
    return req.url ?? "";
  };
}

export function createMeridianApiProxy(
  target = resolveMeridianApiBaseUrl(),
  mode = resolveMeridianDevMode()
): Record<string, ProxyOptions> {
  return {
    "/api": {
      target,
      changeOrigin: true,
      secure: false,
      bypass: createMeridianApiFallbackBypass(target, undefined, mode)
    }
  };
}

async function probeMeridianApiTarget(target: string, timeoutMs: number, fetchImpl: typeof fetch): Promise<boolean> {
  if (!fetchImpl) {
    return false;
  }

  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), timeoutMs);

  try {
    const response = await fetchImpl(new URL("/healthz", `${target}/`).toString(), {
      method: "GET",
      signal: controller.signal,
      headers: {
        Accept: "application/json,text/plain,*/*"
      }
    });
    return response.ok;
  } catch {
    return false;
  } finally {
    clearTimeout(timeout);
  }
}

function isDevelopmentFixtureRequest(req: IncomingMessage): boolean {
  if (req.method === "GET" || req.method === "HEAD") {
    return req.url?.startsWith("/api/") === true;
  }

  const cleanPath = req.url?.split("?")[0];
  return req.method === "POST" && (
    cleanPath === QUANT_API_ENDPOINTS.parameters ||
    cleanPath === COVERED_CALL_API_ENDPOINTS.chainPreview ||
    cleanPath === WORKSTATION_API_ENDPOINTS.evidenceVaultSearch ||
    isEvidenceWorkbenchDemoPost(cleanPath)
  );
}

function isEvidenceWorkbenchDemoPost(cleanPath?: string): boolean {
  return cleanPath?.startsWith(`${WORKSTATION_API_ENDPOINTS.evidenceSubjects}/`) === true &&
    (cleanPath.endsWith("/validate") || cleanPath.endsWith("/export-manifest"));
}

function writeDevelopmentFixtureResponse(req: IncomingMessage, res: ServerResponse, fixture: unknown) {
  const body = req.method === "HEAD" ? "" : JSON.stringify(fixture);

  res.statusCode = 200;
  res.setHeader("Content-Type", "application/json; charset=utf-8");
  res.setHeader("Cache-Control", "no-store");
  res.setHeader(meridianDevFixtureHeader, "true");
  res.end(body);
}

function writeUnsupportedFixtureResponse(req: IncomingMessage, res: ServerResponse) {
  res.statusCode = 501;
  res.setHeader("Content-Type", "application/json; charset=utf-8");
  res.setHeader("Cache-Control", "no-store");
  res.end(req.method === "HEAD" ? "" : JSON.stringify({
    title: "Development fixture unavailable",
    detail: `No fixture supports ${req.method ?? "GET"} ${req.url ?? "/api"}. Start backend-connected mode to use the Meridian host.`,
    mode: "fixture-only"
  }));
}

const apiBaseUrl = resolveMeridianApiBaseUrl();
const devMode = resolveMeridianDevMode();
const appRoot = path.resolve(__dirname);

// Read at config time, in Node, so package.json never enters the client module graph. A
// default `import packageJson from "../package.json"` in application code inlines the whole
// manifest into the emitted chunk -- every dependency name and pinned version, and every npm
// script body -- to render one version string (#2684). Injecting the single field keeps the
// manifest out of the bundle by construction rather than relying on JSON tree-shaking, and
// stops a dependency bump or script rename from rewriting the committed bundle.
const { version: appVersion } = JSON.parse(
  readFileSync(path.resolve(__dirname, "package.json"), "utf8")
) as { version: string };

export default defineConfig({
  root: appRoot,
  base: "/workstation/",
  plugins: [react(), createMeridianDevSessionPlugin()],
  define: {
    __APP_VERSION__: JSON.stringify(appVersion),
    "import.meta.env.VITE_MERIDIAN_DEV_MODE": JSON.stringify(devMode ?? "")
  },
  server: {
    hmr: resolveViteHmrConfig(),
    proxy: createMeridianApiProxy(apiBaseUrl, devMode)
  },
  preview: {
    proxy: createMeridianApiProxy(apiBaseUrl, devMode)
  },
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "src")
    }
  },
  build: {
    outDir: "../wwwroot/workstation",
    emptyOutDir: true
  },
  test: {
    globals: true,
    environment: "jsdom",
    setupFiles: "./src/test/setup.ts",
    css: true,
    pool: "forks",
    maxWorkers: 2,
    testTimeout: 15000,
    teardownTimeout: 5000
  }
});
