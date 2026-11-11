import { describe, expect, it } from "vitest";
import { compareWithBaseline, isAdHocBadgeClass, RATCHETS } from "../../scripts/design-system-ratchets.mjs";

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
});
