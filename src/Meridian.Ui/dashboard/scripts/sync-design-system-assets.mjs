#!/usr/bin/env node
/** Project the canonical design-system assets into the native Vite application.
 * No runtime prototype bundle, remote font service, or second React is loaded.
 */
import { readFileSync, readdirSync, existsSync, mkdirSync, writeFileSync } from "node:fs";
import { dirname, join, resolve, relative } from "node:path";
import { fileURLToPath } from "node:url";

const scriptPath = fileURLToPath(import.meta.url);
const defaultDashboardRoot = resolve(dirname(scriptPath), "..");

export function synchronizeDesignSystemAssets({ dashboardRoot = defaultDashboardRoot, packageRoot = resolve(dashboardRoot, "../../../Meridian Design System"), write = false } = {}) {
  const version = JSON.parse(readFileSync(join(packageRoot, "release.json"), "utf8")).version;
  const bridgePath = join(dashboardRoot, "src/design-system/assets.ts");
  const bridge = readFileSync(bridgePath, "utf8");
  if (!bridge.includes(`DESIGN_SYSTEM_VERSION = "${version}"`)) {
    throw new Error(`Dashboard asset bridge must declare canonical design-system version ${version}`);
  }
  const mappings = [];
  for (const group of ["brand", "icons", "fonts"]) {
    const sourceDirectory = join(packageRoot, "assets", group);
    for (const entry of readdirSync(sourceDirectory, { withFileTypes: true }).sort((a, b) => a.name < b.name ? -1 : 1)) {
      if (!entry.isFile()) throw new Error(`Only regular canonical assets are supported: ${join(sourceDirectory, entry.name)}`);
      const destination = group === "fonts" && entry.name.startsWith("OFL-")
        ? join(dashboardRoot, "public/licenses", entry.name)
        : join(dashboardRoot, "src/assets", group, entry.name);
      mappings.push([join(sourceDirectory, entry.name), destination]);
    }
  }
  mappings.push([join(packageRoot, "tokens/fonts.css"), join(dashboardRoot, "src/design-system/fonts.css")]);
  const drift = [];
  for (const [source, destination] of mappings) {
    const canonical = readFileSync(source);
    if (existsSync(destination) && readFileSync(destination).equals(canonical)) continue;
    drift.push(relative(dashboardRoot, destination));
    if (write) {
      mkdirSync(dirname(destination), { recursive: true });
      writeFileSync(destination, canonical);
    }
  }
  if (drift.length && !write) throw new Error(`Design-system ${version} asset drift:\n${drift.join("\n")}\nRun npm run design-system:sync.`);
  return { version, files: mappings.length, updated: write ? drift : [] };
}

if (process.argv[1] && resolve(process.argv[1]) === scriptPath) {
  const args = process.argv.slice(2);
  if (args.some(arg => !["--write", "--check"].includes(arg)) || args.includes("--write") && args.includes("--check")) {
    throw new Error("Usage: node scripts/sync-design-system-assets.mjs [--check | --write]");
  }
  const result = synchronizeDesignSystemAssets({ write: args.includes("--write") });
  console.log(`Design-system ${result.version}: ${result.files} production asset/font/license files ${args.includes("--write") ? `synchronized (${result.updated.length} updated)` : "match canonical source"}.`);
}
