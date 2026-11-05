import { describe, expect, it } from "vitest";
import { compareWithBaseline, isAdHocBadgeClass, RATCHETS } from "../../scripts/design-system-ratchets.mjs";

describe("design-system ratchets", () => {
  // Phase 2 turned these rules on with a baseline of existing offenders: no file may gain an
  // occurrence, no new file may introduce one, and progress must be locked in by lowering the
  // baseline (`npm run ratchets:update`).
  it("lets no screen add hand-styled status chips or direct Intl formatting", () => {
    const { regressions, improvements } = compareWithBaseline();

    expect(regressions, "Use StatusBadge (@/design-system/status) and @/lib/format instead.").toEqual([]);
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
  });
});
