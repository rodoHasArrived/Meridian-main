#!/usr/bin/env node
// Read-only source verification. Browser journeys are an explicit separate command.
import { readFileSync } from "node:fs";
import { spawnSync } from "node:child_process";
import path from "node:path";
import process from "node:process";
import { fileURLToPath } from "node:url";

const defaultDashboardRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const readJson = (file) => JSON.parse(readFileSync(file, "utf8"));
const entries = (value = {}) => Object.entries(value).sort(([a], [b]) => a < b ? -1 : a > b ? 1 : 0);

/** Verify declared and installed direct dependencies against npm's checked-in resolution. */
export function checkProjectDependencies(projectRoot) {
  const issues = [];
  try {
    const manifest = readJson(path.join(projectRoot, "package.json"));
    const lock = readJson(path.join(projectRoot, "package-lock.json"));
    const root = lock.packages?.[""];
    if (!root || ![2, 3].includes(lock.lockfileVersion)) throw new Error("Expected an npm v2/v3 package lock");
    if (root.name !== manifest.name || root.version !== manifest.version) issues.push("Package identity differs from its lockfile");
    for (const field of ["dependencies", "devDependencies", "optionalDependencies"]) {
      if (JSON.stringify(entries(root[field])) !== JSON.stringify(entries(manifest[field]))) {
        issues.push(`package.json ${field} differs from its lockfile`);
      }
    }
    const optional = manifest.optionalDependencies ?? {};
    for (const name of Object.keys({ ...manifest.dependencies, ...manifest.devDependencies, ...optional }).sort()) {
      const installedPath = path.join(projectRoot, "node_modules", name, "package.json");
      const locked = lock.packages[`node_modules/${name}`];
      const expected = locked?.link ? lock.packages[locked.resolved]?.version : locked?.version;
      if (!expected) {
        issues.push(`${name}: no resolved version in the lockfile`);
        continue;
      }
      try {
        const installed = readJson(installedPath);
        if (installed.version !== expected) issues.push(`${name}: installed ${installed.version}, lockfile requires ${expected}`);
      } catch (error) {
        if (Object.hasOwn(optional, name) && error.code === "ENOENT") continue;
        issues.push(`${name}: missing or unreadable installed package (${error.message})`);
      }
    }
  } catch (error) {
    issues.push(error.message);
  }
  return issues;
}

export function createCheckPlan({ dashboardRoot = defaultDashboardRoot, python = process.env.MERIDIAN_PYTHON || (process.platform === "win32" ? "python" : "python3") } = {}) {
  const repositoryRoot = path.resolve(dashboardRoot, "../../..");
  const packageRoot = path.join(repositoryRoot, "Meridian Design System");
  const node = (name, cwd, ...args) => ({ name, cwd, executable: process.execPath, args });
  const pythonCheck = (name, script, ...args) => ({ name, cwd: packageRoot, executable: python, args: [script, ...args] });
  return {
    projects: [dashboardRoot, packageRoot],
    stages: [
      node("Verification tooling tests", dashboardRoot, "--test", "scripts/check-design-system.test.mjs", "scripts/design-system-smoke.test.mjs", "scripts/sync-design-system-assets.test.mjs"),
      node("Canonical asset/font/license copies", dashboardRoot, "scripts/sync-design-system-assets.mjs", "--check"),
      node("Design-system ratchets", dashboardRoot, "scripts/design-system-ratchets.mjs"),
      node("Typography floor", dashboardRoot, "scripts/check-typography-floor.mjs"),
      {
        ...node("Focused workstation contracts", dashboardRoot, "--max-old-space-size=4096", "scripts/run-vitest-stable.mjs", "--maxWorkers=2",
          "src/design-system-contract.test.ts", "src/selected-surface-contrast.test.ts", "src/tailwind-build.test.ts", "src/startup-appearance.test.ts", "src/vite-config.font-licenses.test.ts",
          "src/dev/design-system-workbench-entry.test.ts", "src/dev/design-system-workbench.test.tsx",
          "src/design-system/design-system-ratchets.test.ts", "src/design-system/status-badge.test.tsx", "src/components/ui/button.test.tsx",
          "src/components/ui/theme-toggle.test.tsx", "src/components/data/empty-state.concrete.test.tsx", "src/components/meridian/workspace-nav.test.tsx"),
        env: { MERIDIAN_BROWSER_RESULTS_DIR: path.join(repositoryRoot, "artifacts/design-system/source-check") }
      },
      node("Gallery model tests", packageRoot, "--test", "tests/gallery.test.mjs"),
      pythonCheck("Contrast resolution tests", "-m", "unittest", "tests.test_contrast.ContrastMathTest", "tests.test_contrast.CssColorResolutionTest", "tests.test_contrast.PrimaryActionContrastTest", "tests.test_contrast.LegacyGateCompatibilityTest"),
      pythonCheck("Primary action contrast", "scripts/check_contrast.py", "--actions-only"),
      node("Generated package freshness", packageRoot, "scripts/build_package.mjs", "--check"),
      pythonCheck("Release integrity tests", "-m", "unittest", "discover", "-s", "tests", "-p", "test_release.py"),
      pythonCheck("Export integrity", "scripts/export_release.py", "--check")
    ]
  };
}

export function runVerification({ plan = createCheckPlan(), preflight = checkProjectDependencies, execute = spawnSync, logger = console } = {}) {
  for (const project of plan.projects) {
    const issues = preflight(project);
    if (issues.length) {
      logger.error(`Dependency preflight failed in ${project}:\n${issues.join("\n")}\nUse a lockfile-synchronized npm ci in that project before retrying; this command never installs dependencies.`);
      return 1;
    }
  }
  for (const stage of plan.stages) {
    logger.log(`[design-system] ${stage.name}`);
    let result;
    try {
      result = execute(stage.executable, stage.args, { cwd: stage.cwd, shell: false, stdio: "inherit", env: { ...process.env, ...stage.env } });
    } catch (error) {
      logger.error(`[design-system] ${stage.name} failed: ${error.message}`);
      return 1;
    }
    if (result.error || result.status !== 0) {
      logger.error(`[design-system] ${stage.name} failed: ${result.error?.message ?? result.signal ?? `exit ${result.status}`}`);
      return result.status || 1;
    }
  }
  logger.log("[design-system] Focused source checks passed. Browser validation remains a separate check:design-system:browser command.");
  return 0;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  if (process.argv.length > 2) {
    console.error("Usage: node scripts/check-design-system.mjs");
    process.exitCode = 1;
  } else process.exitCode = runVerification();
}
