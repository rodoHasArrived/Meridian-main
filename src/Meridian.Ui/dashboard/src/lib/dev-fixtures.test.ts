import { describe, expect, it } from "vitest";
import { resolveDevFixture } from "@/lib/dev-fixtures";
import { workstationFinancialRecordExplorerEndpoint } from "@/lib/workstation-endpoints";
import type { FinancialRecordExplorerDto } from "@/types";

describe("dev fixtures", () => {
  it("serves the portfolio financial record explorer for no-host previews", () => {
    const fixture = resolveDevFixture<FinancialRecordExplorerDto>(
      workstationFinancialRecordExplorerEndpoint("portfolio")
    );

    expect(fixture).toBeDefined();
    expect(fixture?.explorerId).toBe("portfolio");
    expect(fixture?.sourceState).toMatch(/demo data/i);
    expect(fixture?.rows.length).toBeGreaterThan(0);
    expect(fixture?.selectedRecord?.recordId).toBe(fixture?.rows[0]?.recordId);
    expect(fixture?.proofActions.some((action) => action.isEnabled)).toBe(true);
  });
});
