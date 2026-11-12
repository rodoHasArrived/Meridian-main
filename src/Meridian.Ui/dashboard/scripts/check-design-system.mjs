#!/usr/bin/env node
// Automated compliance checks. Windows/NVDA acceptance is a separate recorded pass.
import { spawnSync } from "node:child_process";
import path from "node:path";
import process from "node:process";
import { fileURLToPath } from "node:url";

const dashboard = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const repository = path.resolve(dashboard, "../../..");
const packageRoot = path.join(repository, "Meridian Design System");
const python = process.env.PYTHON || (process.platform === "win32" ? "python" : "python3");
const checks = [
  ["Generated package freshness", process.execPath, ["scripts/build-design-system.mjs", "--check"]],
  ["Consumption inventory freshness", process.execPath, ["--max-old-space-size=4096", "scripts/design-system-consumption.mjs", "--check"]],
  ["Package governance and contrast regression tests", python, ["-m", "unittest", "discover", "-s", path.join(packageRoot, "tests"), "-p", "test_*.py"]],
  ["Browser runtime visual policy", process.execPath, ["scripts/design-system-policy.mjs"]],
  ["Build, policy, inventory, and acceptance harness regressions", process.execPath, ["--test", "scripts/build-design-system.test.mjs", "scripts/design-system-policy.test.mjs", "scripts/design-system-consumption.test.mjs", "scripts/design-system-browser.test.mjs"]],
  ["Native and reference adapter contracts", process.execPath, ["node_modules/vitest/vitest.mjs", "run", "src/design-system-contract.test.ts", "src/design-system-color-contract.test.ts", "src/print-styles.test.ts", "src/components/ui/selection-contracts.test.tsx", "src/components/ui/reference-grid-form-contracts.test.tsx", "src/components/ui/component-browser-contracts.test.tsx", "src/components/ui/date-reference-contracts.test.tsx", "src/components/ui/date-picker.test.tsx", "src/components/ui/dialog.test.tsx", "src/components/ui/dialog.view-model.test.ts", "src/components/charts/charts.test.tsx"]]
];
for (const [name, command, args] of checks) {
  process.stdout.write(`\n${name}\n`);
  const result = spawnSync(command, args, { cwd: dashboard, stdio: "inherit", env: process.env });
  if (result.error) {
    process.stderr.write(`${result.error.message}\n`);
    process.exit(1);
  }
  if (result.status !== 0) process.exit(result.status ?? 1);
}
process.stdout.write("\nAutomated design-system checks passed. Run design:test:browser and complete the Windows/NVDA receipt before claiming full conformance.\n");
