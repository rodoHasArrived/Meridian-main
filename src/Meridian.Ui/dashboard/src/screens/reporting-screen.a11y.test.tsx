import { beforeEach, describe, expect, it, vi } from "vitest";
import { axe } from "jest-axe";
import { ReportingScreen } from "@/screens/reporting-screen";
import { renderWithRouter } from "@/test/render";
import type { AccountingWorkspaceResponse } from "@/types";

const accounting: AccountingWorkspaceResponse = {
  metrics: [],
  reconciliationQueue: [],
  breakQueue: [],
  cashFlow: {
    totalCash: 0,
    totalLedgerCash: 0,
    netVariance: 0,
    totalFinancing: 0,
    runsWithCashSignals: 0,
    runsWithCashVariance: 0,
    tone: "success",
    summary: "Cash is balanced."
  },
  reporting: {
    profileCount: 1,
    recommendedProfiles: ["excel"],
    profiles: [
      {
        id: "excel",
        name: "Excel",
        targetTool: "Excel",
        format: "Xlsx",
        description: "Board-ready workbook export.",
        loaderScript: false,
        dataDictionary: true
      }
    ],
    summary: "1 export profile available."
  }
};

describe("ReportingScreen accessibility", () => {
  const fetchMock = vi.fn();

  beforeEach(() => {
    fetchMock.mockReset();
    fetchMock.mockResolvedValue({
      ok: true,
      text: async () => JSON.stringify({})
    });
    vi.stubGlobal("fetch", fetchMock);
  });

  it("has no basic accessibility violations in the loading state", async () => {
    const { container } = renderWithRouter(<ReportingScreen data={null} />, { initialEntries: ["/reporting"] });

    const results = await axe(container);
    expect(results.violations).toHaveLength(0);
  });

  it("has no basic accessibility violations with a minimal reporting workspace payload", async () => {
    const { container } = renderWithRouter(<ReportingScreen data={accounting} />, { initialEntries: ["/reporting"] });

    const results = await axe(container);
    expect(results.violations).toHaveLength(0);
  });

  it.each([
    ["report-builder", "/reporting/report-builder"],
    ["scheduled", "/reporting/scheduled"],
    ["run-status", "/reporting/run-status"],
    ["report-packs", "/reporting/report-packs"],
    ["exports", "/reporting/exports"],
    ["governance", "/reporting/governance"]
  ])("has no basic accessibility violations on the %s route", async (_view, pathname) => {
    const { container } = renderWithRouter(<ReportingScreen data={accounting} />, {
      initialEntries: [pathname]
    });

    const results = await axe(container);
    expect(results.violations.map((violation) => ({
      id: violation.id,
      targets: violation.nodes.map((node) => node.target)
    }))).toEqual([]);
  });
});
