import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { afterEach, describe, expect, it, vi } from "vitest";
import { compareWithBaseline, isAdHocBadgeClass, RATCHETS, readBaseline, writeReducedBaseline, type RatchetCounts } from "../../scripts/design-system-ratchets.mjs";

const temporaryDirectories: string[] = [];
afterEach(() => {
  vi.restoreAllMocks();
  for (const directory of temporaryDirectories.splice(0)) fs.rmSync(directory, { recursive: true, force: true });
});

function counts(files: Record<string, number> = {}): RatchetCounts {
  return Object.fromEntries(Object.keys(RATCHETS).map((name) => [name, name === "rawTables" ? files : {}]));
}

function baselineFixture(value: unknown = counts()) {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), "meridian-ratchet-"));
  temporaryDirectories.push(directory);
  const file = path.join(directory, "baseline.json");
  fs.writeFileSync(file, JSON.stringify(value));
  return { file, directory, original: fs.readFileSync(file) };
}

describe("design-system ratchets", () => {
  // Phase 2 turned these rules on with a baseline of existing offenders: no file may gain an
  // occurrence, no new file may introduce one, and progress must be locked in by lowering the
  // baseline (`npm run ratchets:update`).
  it("lets no screen add hand-styled status chips, direct Intl or hand-rolled number formatting, local money formatters or raw tables", () => {
    const { regressions, improvements } = compareWithBaseline();

    expect(regressions, "Use StatusBadge (@/design-system/status), @/lib/format and DenseDataTable instead.").toEqual([]);
    expect(improvements, "Lower the baseline with `npm run ratchets:update`.").toEqual([]);
  });

  it("recognizes a hand-styled status chip but not ordinary uppercase text", () => {
    expect(isAdHocBadgeClass("rounded-sm border px-2 py-1 font-mono text-[10px] uppercase tracking-[0.14em]")).toBe(true);
    expect(isAdHocBadgeClass("text-[10px] uppercase tracking-[0.14em] text-muted-foreground")).toBe(false);
    expect(isAdHocBadgeClass("rounded-sm border px-2 py-1 text-sm")).toBe(false);
  });

  it("counts direct Intl formatting calls", () => {
    const source = "new Intl.NumberFormat('en-US').format(1); value.toLocaleString(); d.toLocaleDateString();";
    expect(RATCHETS.directIntlFormatting.count(source)).toBe(3);
    expect(RATCHETS.directIntlFormatting.count("const options: Intl.DateTimeFormatOptions = { timeZone: \"UTC\" };")).toBe(0);
  });

  it("counts hand-rolled toFixed formatting", () => {
    expect(RATCHETS.handRolledNumberFormatting.count("`${(share * 100).toFixed(1)}%` + value.toFixed(2)")).toBe(2);
    expect(RATCHETS.handRolledNumberFormatting.count("formatPercent(share * 100, { maximumFractionDigits: 1 })")).toBe(0);
  });

  it("counts money and P&L formatters defined in a screen but not shared imports or other helpers", () => {
    const source = [
      "function formatPnl(value: number) {}",
      "export function formatSignedCurrency(value: number) {}",
      "const fmtMoney = (value: number) => value;",
      "import { formatMoney, formatPnl } from \"@/lib/format\";",
      "function formatDateTime(value: string) {}",
      "function formatCountLabel(count: number) {}"
    ].join("\n");
    expect(RATCHETS.localMoneyFormatters.count(source)).toBe(3);
  });

  it("counts raw tables but not table-system tables", () => {
    const source = '<table className="w-full text-sm"><table className="dense-data-table" aria-label="Ledger"><table>';
    expect(RATCHETS.rawTables.count(source)).toBe(2);
  });

  it("records only reductions, sorting retained paths and preserving every category", () => {
    const baseline = baselineFixture(counts({ "src/screens/z.tsx": 4, "src/screens/a.tsx": 2, "src/screens/removed.tsx": 1 }));
    expect(writeReducedBaseline(counts({ "src/screens/z.tsx": 3, "src/screens/a.tsx": 2 }), baseline.file)).toBe(true);
    const saved = readBaseline(baseline.file);
    expect(Object.keys(saved)).toEqual(Object.keys(RATCHETS));
    expect(Object.keys(saved.rawTables)).toEqual(["src/screens/a.tsx", "src/screens/z.tsx"]);
    expect(saved.rawTables["src/screens/z.tsx"]).toBe(3);
    expect(saved.rawTables["src/screens/removed.tsx"]).toBeUndefined();
    expect(fs.readdirSync(baseline.directory)).toEqual(["baseline.json"]);
  });

  it("leaves an unchanged baseline byte-for-byte intact", () => {
    const current = counts({ "src/screens/a.tsx": 2 });
    const baseline = baselineFixture(current);
    expect(writeReducedBaseline(current, baseline.file)).toBe(false);
    expect(fs.readFileSync(baseline.file)).toEqual(baseline.original);
  });

  it.each([
    counts({ "src/screens/a.tsx": 3 }),
    counts({ "src/screens/new.tsx": 1 })
  ])("rejects per-file growth even when total occurrences decrease", (current) => {
    const baseline = baselineFixture(counts({ "src/screens/a.tsx": 2, "src/screens/removed.tsx": 10 }));
    expect(() => writeReducedBaseline(current, baseline.file)).toThrow("Refusing to increase");
    expect(fs.readFileSync(baseline.file)).toEqual(baseline.original);
    expect(fs.readdirSync(baseline.directory)).toEqual(["baseline.json"]);
  });

  it.each([
    {}, { ...counts(), unknown: {} }, { ...counts(), rawTables: [] },
    counts({ "src/screens/a.tsx": -1 }), counts({ "src/screens/a.tsx": 0 }), counts({ "src/screens/a.tsx": 1.5 }),
    counts({ "src/screens/../a.tsx": 1 }), counts({ "src/screens/a.test.tsx": 1 }), counts({ "src\\screens\\a.tsx": 1 })
  ])("rejects malformed baseline structure without overwriting it", (value) => {
    const baseline = baselineFixture(value);
    expect(() => writeReducedBaseline(counts(), baseline.file)).toThrow("Cannot use ratchet baseline");
    expect(fs.readFileSync(baseline.file)).toEqual(baseline.original);
  });

  it("fails closed for missing baselines and invalid JSON", () => {
    const baseline = baselineFixture();
    fs.writeFileSync(baseline.file, "{");
    expect(() => writeReducedBaseline(counts(), baseline.file)).toThrow("Cannot use ratchet baseline");
    expect(fs.readFileSync(baseline.file, "utf8")).toBe("{");
    fs.unlinkSync(baseline.file);
    expect(() => writeReducedBaseline(counts(), baseline.file)).toThrow("Cannot use ratchet baseline");
    expect(fs.existsSync(baseline.file)).toBe(false);
  });

  it("retains the original and removes temporary files when atomic replacement fails", () => {
    const baseline = baselineFixture(counts({ "src/screens/a.tsx": 2 }));
    vi.spyOn(fs, "renameSync").mockImplementation(() => { throw new Error("Replacement unavailable"); });
    expect(() => writeReducedBaseline(counts(), baseline.file)).toThrow("Replacement unavailable");
    expect(fs.readFileSync(baseline.file)).toEqual(baseline.original);
    expect(fs.readdirSync(baseline.directory)).toEqual(["baseline.json"]);
  });
});
