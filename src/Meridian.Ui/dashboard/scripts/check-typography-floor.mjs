// Typography floor ratchet.
//
// The workstation type scale starts at 12px. Sub-12px text (Tailwind arbitrary-size utilities
// or CSS `font-size` below 12px) is tolerated only where it already exists. This script
// counts those occurrences per file and compares them with a checked-in baseline:
//
//   - a file gaining occurrences, or a new file containing any, fails the check;
//   - a file losing occurrences passes, and the script asks for the baseline to be lowered
//     with `--update-baseline` so the allowlist only ever shrinks.
//
// Usage:
//   node scripts/check-typography-floor.mjs                   # check (used by `npm run lint`)
//   node scripts/check-typography-floor.mjs --update-baseline # rewrite the baseline

import { readFile, readdir, writeFile } from "node:fs/promises";
import { dirname, join, relative, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const dashboardRoot = resolve(here, "..");
const srcRoot = join(dashboardRoot, "src");
const baselinePath = join(here, "typography-floor-baseline.json");

// Tailwind arbitrary sub-12px font sizes. The class prefix is assembled at runtime so that
// Tailwind's content scanner, which reads this repo's files, does not emit utilities from it.
const TAILWIND_SUB12 = new RegExp("text-" + "\\[(?:[0-9]|10|11)(?:\\.\\d+)?px\\]", "g");
// CSS font-size declarations below 12px, e.g. `font-size: 11px;` or `font-size: 10.5px`.
const CSS_SUB12 = /font-size\s*:\s*(?:[0-9]|10|11)(?:\.\d+)?px/g;

const SOURCE_EXTENSIONS = new Set([".ts", ".tsx", ".css"]);
const UPDATE = process.argv.includes("--update-baseline");

async function listSourceFiles(dir) {
  const entries = await readdir(dir, { withFileTypes: true });
  const files = [];
  for (const entry of entries) {
    const full = join(dir, entry.name);
    if (entry.isDirectory()) {
      files.push(...(await listSourceFiles(full)));
    } else if (SOURCE_EXTENSIONS.has(entry.name.slice(entry.name.lastIndexOf(".")))) {
      files.push(full);
    }
  }
  return files;
}

function countMatches(text, pattern) {
  return (text.match(pattern) ?? []).length;
}

async function measure() {
  const counts = {};
  for (const file of await listSourceFiles(srcRoot)) {
    const text = await readFile(file, "utf8");
    const total = countMatches(text, TAILWIND_SUB12) + countMatches(text, CSS_SUB12);
    if (total > 0) {
      // Forward slashes keep the baseline stable across operating systems.
      const key = relative(dashboardRoot, file).split(sep).join("/");
      counts[key] = total;
    }
  }
  return counts;
}

async function readBaseline() {
  try {
    return JSON.parse(await readFile(baselinePath, "utf8")).files ?? {};
  } catch (error) {
    if (error.code === "ENOENT") {
      return {};
    }
    throw error;
  }
}

const current = await measure();

if (UPDATE) {
  const sorted = Object.fromEntries(Object.entries(current).sort(([a], [b]) => a.localeCompare(b)));
  const payload = {
    description:
      "Sub-12px text occurrences per file. Ratchet only: counts may fall, never rise. Regenerate with `node scripts/check-typography-floor.mjs --update-baseline`.",
    files: sorted
  };
  await writeFile(baselinePath, `${JSON.stringify(payload, null, 2)}\n`);
  const total = Object.values(sorted).reduce((sum, n) => sum + n, 0);
  console.log(`Wrote typography floor baseline: ${Object.keys(sorted).length} files, ${total} occurrences.`);
  process.exit(0);
}

const baseline = await readBaseline();
const regressions = [];
const reducible = [];

for (const [file, count] of Object.entries(current)) {
  const allowed = baseline[file] ?? 0;
  if (count > allowed) {
    regressions.push(`${file}: ${count} sub-12px occurrences (allowed ${allowed})`);
  }
}

for (const [file, allowed] of Object.entries(baseline)) {
  const count = current[file] ?? 0;
  if (count < allowed) {
    reducible.push(`${file}: ${count} (baseline ${allowed})`);
  }
}

if (reducible.length > 0) {
  console.log(`Typography floor: ${reducible.length} file(s) dropped below baseline. Run with --update-baseline to lock in the reduction:`);
  for (const line of reducible) {
    console.log(`  - ${line}`);
  }
}

if (regressions.length > 0) {
  console.error("Typography floor check failed. New sub-12px text is not allowed; use the 12px minimum from the type scale:");
  for (const line of regressions) {
    console.error(`  - ${line}`);
  }
  process.exit(1);
}

const total = Object.values(current).reduce((sum, n) => sum + n, 0);
console.log(`Typography floor OK: ${total} allowlisted sub-12px occurrences, none new.`);
