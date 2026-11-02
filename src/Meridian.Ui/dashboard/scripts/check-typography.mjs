#!/usr/bin/env node
// Typography guardrail (roadmap Phase 0): sub-12px text utilities may not grow.
//
// The baseline in scripts/typography-allowlist.json records the current count of
// text-[9px] / text-[10px] / text-[11px] per source file. The check fails when a file
// exceeds its recorded count or a new file introduces one. Counts may only shrink:
// run with --update after removing violations to lower the baseline. Never raise it by hand.

import { readFileSync, readdirSync, statSync, writeFileSync } from "node:fs";
import { dirname, join, relative } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const root = join(here, "..");
const srcRoot = join(root, "src");
const allowlistPath = join(here, "typography-allowlist.json");
const SUB_12PX = /text-\[(?:9|10|11)px\]/g;
const SOURCE_EXT = /\.(?:ts|tsx|css)$/;

function walk(dir, out = []) {
  for (const name of readdirSync(dir)) {
    const full = join(dir, name);
    if (statSync(full).isDirectory()) {
      walk(full, out);
    } else if (SOURCE_EXT.test(name) && !/\.test\.(?:ts|tsx)$/.test(name)) {
      out.push(full);
    }
  }
  return out;
}

function countByFile() {
  const counts = {};
  for (const file of walk(srcRoot)) {
    const matches = readFileSync(file, "utf8").match(SUB_12PX);
    if (matches && matches.length > 0) {
      counts[relative(root, file).split("\\").join("/")] = matches.length;
    }
  }
  return counts;
}

const current = countByFile();
const total = Object.values(current).reduce((sum, n) => sum + n, 0);

if (process.argv.includes("--update")) {
  const baseline = JSON.parse(readFileSync(allowlistPath, "utf8"));
  const next = {};
  for (const [file, count] of Object.entries(current)) {
    // Only ever lower a recorded count. A file not in the baseline has no allowance, so
    // --update never grandfathers new violations; a file that grew keeps its old count and fails below.
    const recorded = baseline.files[file];
    if (recorded === undefined) continue;
    next[file] = Math.min(count, recorded);
  }
  writeFileSync(allowlistPath, `${JSON.stringify({ version: 1, files: next }, null, 2)}\n`);
  console.log(`typography baseline updated: ${Object.keys(next).length} file(s), ${total} sub-12px occurrence(s)`);
  process.exit(0);
}

const baseline = JSON.parse(readFileSync(allowlistPath, "utf8")).files;
const violations = [];
for (const [file, count] of Object.entries(current)) {
  const allowed = baseline[file] ?? 0;
  if (count > allowed) {
    violations.push(`${file}: ${count} sub-12px occurrence(s), baseline allows ${allowed}`);
  }
}

if (violations.length > 0) {
  console.error("Sub-12px text (text-[9px|10px|11px]) increased. Use the type-scale tokens instead:");
  for (const line of violations) console.error(`  ${line}`);
  process.exit(1);
}

const baselineTotal = Object.values(baseline).reduce((sum, n) => sum + n, 0);
console.log(`typography check passed: ${total} sub-12px occurrence(s), baseline ${baselineTotal}`);
