import path from "node:path";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";
import type { ProxyOptions } from "vite";

export const defaultMeridianApiBaseUrl = "http://localhost:8080";

export function resolveMeridianApiBaseUrl(env: NodeJS.ProcessEnv = process.env): string {
  const configured = env.MERIDIAN_API_BASE_URL ?? env.VITE_MERIDIAN_API_BASE_URL;
  return (configured?.trim() || defaultMeridianApiBaseUrl).replace(/\/+$/, "");
}

export function createMeridianApiProxy(target = resolveMeridianApiBaseUrl()): Record<string, ProxyOptions> {
  return {
    "/api": {
      target,
      changeOrigin: true,
      secure: false
    }
  };
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
