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
//   - rawTables: hand-built <table> elements instead of the table system (DenseDataTable, or
//     DenseDataTableHeaderCell inside a `dense-data-table` for grouped rows).
//   - handRolledNumberFormatting: `.toFixed(` calls, which build a displayed number without
//     thousands separators, a pinned locale or the shared sign convention.
//   - localMoneyFormatters: money and P&L formatters defined in a screen (formatMoney,
//     formatCurrency, formatPnl, formatSigned*, fmtMoney, ...) instead of imported from
//     `@/lib/format`; each one can round, sign or abbreviate differently from its neighbours.
//
// `design-system-ratchets.test.ts` fails when a file exceeds its baseline, when a file not in
// the baseline gains an occurrence, or when a file drops below its baseline (lock the progress
// in by rerunning with --write). Usage:
//   node scripts/design-system-ratchets.mjs            # report
//   node scripts/design-system-ratchets.mjs --write    # record reductions only; never allow growth
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const DASHBOARD_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
export const BASELINE_PATH = path.join(DASHBOARD_ROOT, "scripts", "design-system-ratchets.baseline.json");
const SCREENS_DIR = path.join(DASHBOARD_ROOT, "src", "screens");

const STRING_LITERAL = /"([^"\n]*)"|'([^'\n]*)'|`([^`]*)`/g;
// `\b` keeps type references such as `Intl.DateTimeFormatOptions` out of the count.
const DIRECT_INTL = /Intl\.(?:NumberFormat|DateTimeFormat)\b|\.toLocaleString\(|\.toLocaleDateString\(|\.toLocaleTimeString\(/g;
const TO_FIXED = /\.toFixed\(/g;
// A function or const declaration whose name marks it as a money or P&L formatter.
const LOCAL_MONEY_FORMATTER = /\b(?:function|const)\s+(?:format|fmt)(?:Money|Currency|Pnl|PnL|Usd|Dollars?|Notional|Signed|Compact)\w*/g;
// A <table opening tag that is not styled as the table system's `dense-data-table`.
const RAW_TABLE = /<table\b(?![^>]*\bclassName="[^"]*\bdense-data-table\b)/g;

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
  },
  rawTables: {
    description: "hand-built <table> elements in screens/ (use DenseDataTable from ui-kit-primitives)",
    count(source) {
      return [...source.matchAll(RAW_TABLE)].length;
    }
  },
  handRolledNumberFormatting: {
    description: ".toFixed() number formatting in screens/ (use formatNumber, formatPercent, formatMoney or formatPnl from @/lib/format)",
    count(source) {
      return [...source.matchAll(TO_FIXED)].length;
    }
  },
  localMoneyFormatters: {
    description: "money and P&L formatters defined in screens/ (import formatMoney, formatPnl or formatCurrency from @/lib/format)",
    count(source) {
      return [...source.matchAll(LOCAL_MONEY_FORMATTER)].length;
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

function validateCounts(counts, label) {
  const object = (value) => value !== null && typeof value === "object" && !Array.isArray(value);
  if (!object(counts) || Object.keys(counts).length !== Object.keys(RATCHETS).length
      || Object.keys(counts).some((name) => !Object.hasOwn(RATCHETS, name))) {
    throw new Error(`${label}: expected exactly the known ratchet categories`);
  }
  for (const name of Object.keys(RATCHETS)) {
    if (!object(counts[name])) throw new Error(`${label}: ${name} must be a file/count object`);
    for (const [file, count] of Object.entries(counts[name])) {
      if (!file.startsWith("src/screens/") || file.includes("\\") || file.includes("\0")
          || file.split("/").some((part) => !part || part === "." || part === "..")
          || !/\.(ts|tsx)$/.test(file) || /\.test\.(ts|tsx)$/.test(file)) {
        throw new Error(`${label}: unsafe or non-screen source path ${file}`);
      }
      if (!Number.isSafeInteger(count) || count <= 0) {
        throw new Error(`${label}: ${name}/${file} must have a positive integer count`);
      }
    }
  }
  return counts;
}

export function readBaseline(baselinePath = BASELINE_PATH) {
  try {
    return validateCounts(JSON.parse(fs.readFileSync(baselinePath, "utf8")), "Baseline");
  } catch (error) {
    throw new Error(`Cannot use ratchet baseline ${baselinePath}: ${error.message}`, { cause: error });
  }
}

/** Atomically record improvements. A rejected update never writes to the baseline. */
export function writeReducedBaseline(current = scanRatchets(), baselinePath = BASELINE_PATH) {
  const baseline = readBaseline(baselinePath);
  validateCounts(current, "Current counts");
  const { regressions, improvements } = compareWithBaseline(current, baseline);
  if (regressions.length) {
    throw new Error(`Refusing to increase the ratchet baseline:\n${regressions.join("\n")}`);
  }
  if (!improvements.length) return false;

  const sorted = Object.fromEntries(Object.keys(RATCHETS).map((name) => [
    name, Object.fromEntries(Object.entries(current[name]).sort(([a], [b]) => a < b ? -1 : a > b ? 1 : 0))
  ]));
  const temporaryDirectory = fs.mkdtempSync(path.join(path.dirname(baselinePath), ".ratchet-update-"));
  try {
    const temporaryFile = path.join(temporaryDirectory, "baseline.json");
    fs.writeFileSync(temporaryFile, `${JSON.stringify(sorted, null, 2)}\n`, { flag: "wx" });
    fs.renameSync(temporaryFile, baselinePath);
  } finally {
    fs.rmSync(temporaryDirectory, { recursive: true, force: true });
  }
  return true;
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
  try {
    if (process.argv.slice(2).some((arg) => arg !== "--write")) throw new Error("Usage: design-system-ratchets.mjs [--write]");
    const scan = scanRatchets();
    if (process.argv.includes("--write")) {
      const changed = writeReducedBaseline(scan);
      console.log(`${changed ? "Reduced" : "Unchanged"} ${path.relative(DASHBOARD_ROOT, BASELINE_PATH)}`, JSON.stringify(totals(scan)));
    } else {
      const { regressions, improvements } = compareWithBaseline(scan);
      console.log(JSON.stringify(totals(scan)));
      for (const line of regressions) console.log(`REGRESSION ${line}`);
      for (const line of improvements) console.log(`IMPROVED   ${line} (rerun with --write to lock it in)`);
      process.exitCode = regressions.length > 0 ? 1 : 0;
    }
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
  }
}
