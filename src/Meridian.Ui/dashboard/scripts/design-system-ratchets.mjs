#!/usr/bin/env node
/* global process, console */
// Design-system ratchets for the browser workstation.
//
// Each ratchet counts one legacy pattern per file under src/screens/ (tests excluded) and
// compares it with a checked-in baseline that may only shrink:
//   - adHocStatusBadges: hand-styled status chips (a class string with uppercase, tracking,
//     a border or radius, and horizontal padding) instead of the design-system StatusBadge.
//   - directIntlFormatting: Intl.NumberFormat / Intl.DateTimeFormat / toLocale*String calls
//     instead of the shared `@/lib/format` module.
//
// `design-system-ratchets.test.ts` fails when a file exceeds its baseline, when a file not in
// the baseline gains an occurrence, or when a file drops below its baseline (lock the progress
// in by rerunning with --write). Usage:
//   node scripts/design-system-ratchets.mjs            # report
//   node scripts/design-system-ratchets.mjs --write    # rewrite the baseline from the tree
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const DASHBOARD_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
export const BASELINE_PATH = path.join(DASHBOARD_ROOT, "scripts", "design-system-ratchets.baseline.json");
const SCREENS_DIR = path.join(DASHBOARD_ROOT, "src", "screens");

const STRING_LITERAL = /"([^"\n]*)"|'([^'\n]*)'|`([^`]*)`/g;
const DIRECT_INTL = /Intl\.NumberFormat|Intl\.DateTimeFormat|\.toLocaleString\(|\.toLocaleDateString\(|\.toLocaleTimeString\(/g;

/** A string literal styled like a status chip: tracked uppercase text in a bordered/rounded box. */
export function isAdHocBadgeClass(value) {
  return /\buppercase\b/.test(value)
    && /\btracking-/.test(value)
    && /\b(border|rounded)\b|\b(border|rounded)-/.test(value)
    && /\bpx-/.test(value);
}

export const RATCHETS = {
  adHocStatusBadges: {
    description: "hand-styled status chips in screens/ (use StatusBadge from @/design-system/status)",
    count(source) {
      let total = 0;
      for (const match of source.matchAll(STRING_LITERAL)) {
        const value = match[1] ?? match[2] ?? match[3] ?? "";
        if (isAdHocBadgeClass(value)) {
          total += 1;
        }
      }
      return total;
    }
  },
  directIntlFormatting: {
    description: "direct Intl / toLocale*String formatting in screens/ (use @/lib/format)",
    count(source) {
      return [...source.matchAll(DIRECT_INTL)].length;
    }
  }
};

function screenFiles(dir = SCREENS_DIR) {
  const files = [];
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      files.push(...screenFiles(full));
    } else if (/\.(ts|tsx)$/.test(entry.name) && !/\.test\.(ts|tsx)$/.test(entry.name)) {
      files.push(full);
    }
  }
  return files.sort();
}

/** Current per-file counts for every ratchet, keyed by dashboard-relative POSIX path. */
export function scanRatchets() {
  const result = Object.fromEntries(Object.keys(RATCHETS).map((name) => [name, {}]));
  for (const file of screenFiles()) {
    const source = fs.readFileSync(file, "utf8");
    const relative = path.relative(DASHBOARD_ROOT, file).split(path.sep).join("/");
    for (const [name, ratchet] of Object.entries(RATCHETS)) {
      const count = ratchet.count(source);
      if (count > 0) {
        result[name][relative] = count;
      }
    }
  }
  return result;
}

export function readBaseline() {
  return JSON.parse(fs.readFileSync(BASELINE_PATH, "utf8"));
}

/** Differences between the tree and the baseline: regressions and unrecorded improvements. */
export function compareWithBaseline(current = scanRatchets(), baseline = readBaseline()) {
  const regressions = [];
  const improvements = [];
  for (const name of Object.keys(RATCHETS)) {
    const now = current[name] ?? {};
    const allowed = baseline[name] ?? {};
    for (const [file, count] of Object.entries(now)) {
      const limit = allowed[file] ?? 0;
      if (count > limit) {
        regressions.push(`${name}: ${file} has ${count} (baseline ${limit})`);
      }
    }
    for (const [file, limit] of Object.entries(allowed)) {
      const count = now[file] ?? 0;
      if (count < limit) {
        improvements.push(`${name}: ${file} has ${count} (baseline ${limit})`);
      }
    }
  }
  return { regressions, improvements };
}

function totals(scan) {
  return Object.fromEntries(Object.entries(scan).map(([name, files]) => [
    name,
    { files: Object.keys(files).length, occurrences: Object.values(files).reduce((sum, value) => sum + value, 0) }
  ]));
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const scan = scanRatchets();
  if (process.argv.includes("--write")) {
    fs.writeFileSync(BASELINE_PATH, `${JSON.stringify(scan, null, 2)}\n`);
    console.log(`Wrote ${path.relative(DASHBOARD_ROOT, BASELINE_PATH)}`, JSON.stringify(totals(scan)));
  } else {
    const { regressions, improvements } = compareWithBaseline(scan);
    console.log(JSON.stringify(totals(scan)));
    for (const line of regressions) console.log(`REGRESSION ${line}`);
    for (const line of improvements) console.log(`IMPROVED   ${line} (rerun with --write to lock it in)`);
    process.exitCode = regressions.length > 0 ? 1 : 0;
  }
}
