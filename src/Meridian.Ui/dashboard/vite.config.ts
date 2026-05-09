import path from "node:path";
import type { IncomingMessage, ServerResponse } from "node:http";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";
import type { ProxyOptions } from "vite";
import { resolveDevFixture } from "./src/lib/dev-fixtures";

export const defaultMeridianApiBaseUrl = "http://localhost:8080";
export const meridianDevFixtureHeader = "x-meridian-dev-fixture";
export const meridianApiAvailabilityCacheMs = 2_000;
export const meridianApiAvailabilityTimeoutMs = 200;

export interface MeridianApiAvailabilityProbe {
  isAvailable: () => Promise<boolean>;
}

export function resolveMeridianApiBaseUrl(env: NodeJS.ProcessEnv = process.env): string {
  const configured = env.MERIDIAN_API_BASE_URL ?? env.VITE_MERIDIAN_API_BASE_URL;
  return (configured?.trim() || defaultMeridianApiBaseUrl).replace(/\/+$/, "");
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
  availabilityProbe: MeridianApiAvailabilityProbe = createMeridianApiAvailabilityProbe(target)
): NonNullable<ProxyOptions["bypass"]> {
  return async (req, res) => {
    if (!res || !isDevelopmentFixtureRequest(req)) {
      return undefined;
    }

    const fixture = resolveDevFixture<unknown>(req.url ?? "");
    if (fixture === undefined || await availabilityProbe.isAvailable()) {
      return undefined;
    }

    writeDevelopmentFixtureResponse(req, res, fixture);
    return req.url ?? "";
  };
}

export function createMeridianApiProxy(target = resolveMeridianApiBaseUrl()): Record<string, ProxyOptions> {
  return {
    "/api": {
      target,
      changeOrigin: true,
      secure: false,
      bypass: createMeridianApiFallbackBypass(target)
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
  return (req.method === "GET" || req.method === "HEAD") && req.url?.startsWith("/api/") === true;
}

function writeDevelopmentFixtureResponse(req: IncomingMessage, res: ServerResponse, fixture: unknown) {
  const body = req.method === "HEAD" ? "" : JSON.stringify(fixture);

  res.statusCode = 200;
  res.setHeader("Content-Type", "application/json; charset=utf-8");
  res.setHeader("Cache-Control", "no-store");
  res.setHeader(meridianDevFixtureHeader, "true");
  res.end(body);
}

const apiBaseUrl = resolveMeridianApiBaseUrl();

export default defineConfig({
  base: "/workstation/",
  plugins: [react()],
  server: {
    proxy: createMeridianApiProxy(apiBaseUrl)
  },
  preview: {
    proxy: createMeridianApiProxy(apiBaseUrl)
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
    testTimeout: 15000
  }
});
