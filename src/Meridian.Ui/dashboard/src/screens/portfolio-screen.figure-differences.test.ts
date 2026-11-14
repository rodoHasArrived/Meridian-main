import { describe, expect, it } from "vitest";
import { buildPortfolioFigureDifferenceNote } from "@/screens/portfolio-screen.figure-differences";
import { buildPortfolioFallbackMetrics } from "@/screens/portfolio-screen.view-model";
import type { FinancialRecordExplorerDto } from "@/types";

describe("buildPortfolioFigureDifferenceNote", () => {
  const headerProvenance = { source: "Portfolio workspace", asOf: "2026-06-30T18:05:00Z", scope: "All linked accounts" };
  const headerMetrics = buildPortfolioFallbackMetrics({ openPositionCount: 1, totalExposure: 18_900, totalUnrealizedPnl: 90 });

  function explorer(overrides: Partial<FinancialRecordExplorerDto> = {}): FinancialRecordExplorerDto {
    return {
      explorerId: "portfolio",
      title: "Portfolio Explorer",
      description: "Explore retained account and aggregate position records.",
      sourceState: "Demo portfolio data and Mean reversion evidence are ready for review.",
      isBlocked: false,
      blockedReason: "",
      scopeItems: [
        { label: "Source", value: "Demo portfolio data", tone: "Default" },
        { label: "Run", value: "Mean reversion", tone: "Info" },
        { label: "As of", value: "Jan 2, 2026, 14:00 UTC", tone: "Default" }
      ],
      savedViews: [],
      summaryItems: [
        { label: "Positions", value: "2", detail: "Retained position rows.", tone: "Success" },
        { label: "Market value", value: "$25,587", detail: "Aggregate market value.", tone: "Default" },
        { label: "Unrealized PnL", value: "+$1,239", detail: "Retained unrealized gain.", tone: "Success" }
      ],
      filters: [],
      columns: [],
      rows: [],
      selectedRecord: null,
      proofActions: [],
      recordGraph: { nodes: [], edges: [] },
      ...overrides
    };
  }

  it("explains header-strip and explorer figures that disagree, naming each source", () => {
    expect(buildPortfolioFigureDifferenceNote({ headerMetrics, headerProvenance, explorer: explorer() })).toEqual({
      title: "Why these differ",
      summary: "Live holdings and Retained run records read different records, so their figures are not expected to match.",
      unexplained: false,
      rows: [
        { key: "exposure", label: "Total exposure", first: "$18,900", second: "$25,587" },
        { key: "unrealized-pnl", label: "Unrealized P&L", first: "+$90", second: "+$1,239" },
        { key: "positions", label: "Open positions", first: "1", second: "2" }
      ],
      sources: [
        { name: "Live holdings", text: "Source: Portfolio workspace · As of Jun 30, 2026, 18:05 UTC · Scope: All linked accounts" },
        { name: "Retained run records", text: "Source: Retained run records (demo portfolio data) · As of Jan 2, 2026, 14:00 UTC · Scope: Mean reversion" }
      ]
    });
  });

  const hostScope = [
    { label: "Source", value: "Portfolio workspace", tone: "Default" },
    { label: "Run", value: "run-1", tone: "Info" }
  ] as FinancialRecordExplorerDto["scopeItems"];

  it("does not report the header's gross exposure against the explorer's net exposure as a conflict", () => {
    // Same records with a short position: gross and net legitimately differ.
    const note = buildPortfolioFigureDifferenceNote({
      headerMetrics,
      headerProvenance,
      explorer: explorer({
        scopeItems: hostScope,
        summaryItems: [
          { label: "Positions", value: "1", detail: "", tone: "Success" },
          { label: "Equity", value: "$375,000", detail: "", tone: "Default" },
          { label: "Net Exposure", value: "$12,400", detail: "", tone: "Default" }
        ]
      })
    });

    expect(note).toBeNull();
  });

  it("names the gross and net bases when exposure is listed beside a like-for-like difference", () => {
    const note = buildPortfolioFigureDifferenceNote({
      headerMetrics,
      headerProvenance,
      explorer: explorer({
        scopeItems: hostScope,
        summaryItems: [
          { label: "Positions", value: "3", detail: "", tone: "Success" },
          { label: "Net Exposure", value: "$21,400", detail: "", tone: "Default" }
        ]
      })
    });

    expect(note?.rows).toEqual([
      {
        key: "exposure",
        label: "Total exposure",
        first: "$18,900",
        second: "$21,400",
        detail: "Live holdings is gross, Retained run records is net: different measures, so these would differ on the same records."
      },
      { key: "positions", label: "Open positions", first: "1", second: "3" }
    ]);
    expect(note?.sources[1]).toEqual({ name: "Retained run records", text: "Source: Retained run records · Scope: run-1" });
  });

  it("compares a gross explorer exposure with the header like for like", () => {
    const note = buildPortfolioFigureDifferenceNote({
      headerMetrics,
      headerProvenance,
      explorer: explorer({
        scopeItems: hostScope,
        summaryItems: [
          { label: "Positions", value: "1", detail: "", tone: "Success" },
          { label: "Gross Exposure", value: "$21,400", detail: "", tone: "Default" }
        ]
      })
    });

    expect(note?.rows).toEqual([{ key: "exposure", label: "Total exposure", first: "$18,900", second: "$21,400" }]);
  });

  it("stays silent when the figures agree, the explorer is blocked or it has not loaded", () => {
    const agreeing = explorer({
      summaryItems: [
        { label: "Positions", value: "1", detail: "", tone: "Success" },
        { label: "Market value", value: "$18,900.00", detail: "", tone: "Default" },
        { label: "Unrealized P&L", value: "+$90", detail: "", tone: "Success" }
      ]
    });

    expect(buildPortfolioFigureDifferenceNote({ headerMetrics, headerProvenance, explorer: agreeing })).toBeNull();
    expect(buildPortfolioFigureDifferenceNote({ headerMetrics, headerProvenance, explorer: explorer({ isBlocked: true }) })).toBeNull();
    expect(buildPortfolioFigureDifferenceNote({ headerMetrics, headerProvenance, explorer: null })).toBeNull();
  });
});
