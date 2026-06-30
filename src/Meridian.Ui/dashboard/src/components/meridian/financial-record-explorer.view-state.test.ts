import { describe, expect, it } from "vitest";
import {
  buildShareableExplorerHref,
  buildShareableExplorerStateSummary,
  readSharedExplorerViewState,
  resolveSharedFilters
} from "@/components/meridian/financial-record-explorer.view-state";
import type {
  FinancialRecordExplorerDto,
  FinancialRecordExplorerFilterDto,
  FinancialRecordExplorerRowDto,
  FinancialRecordExplorerSelectedRecordDto
} from "@/types";

describe("financial record explorer view state", () => {
  it("reads matching explorer query state and drops malformed filters", () => {
    const state = readSharedExplorerViewState(
      createExplorerDto(),
      "?frexExplorer=ledger&frexView=income&frexSearch=aapl&frexRecord=ledger:run-1:revenue&frexFilter=accountType%3A%20Income&frexFilter=bad-filter&frexFilter=%3Ablank"
    );

    expect(state).toEqual({
      viewId: "income",
      searchText: "aapl",
      recordId: "ledger:run-1:revenue",
      filters: [{ filterId: "accountType", value: "Income" }]
    });
  });

  it("ignores shared state for a different explorer id", () => {
    expect(readSharedExplorerViewState(createExplorerDto(), "?frexExplorer=portfolio&frexView=income")).toEqual({
      viewId: null,
      searchText: null,
      recordId: null,
      filters: []
    });
  });

  it("resolves share-link filters from saved views and columns", () => {
    const filters = resolveSharedFilters(
      [
        { filterId: "accountType", value: "Income" },
        { filterId: "symbol", value: "AAPL" }
      ],
      createExplorerDto()
    );

    expect(filters).toEqual([
      expect.objectContaining({ filterId: "accountType", label: "Account Type", value: "Income", operator: "equals" }),
      expect.objectContaining({ filterId: "symbol", label: "Symbol", value: "AAPL", operator: "equals" })
    ]);
  });

  it("builds portable share hrefs without discarding non-explorer route context", () => {
    const href = buildShareableExplorerHref({
      explorer: createExplorerDto(),
      selectedViewId: "income",
      searchText: " aapl ",
      selectedFilters: [
        { filterId: "accountType", label: "Account Type", value: "Income", operator: "equals", tone: "Info" }
      ],
      selectedRecordId: "ledger:run-1:revenue",
      currentHref: "http://localhost/accounting?period=2026-06&frexSearch=stale#proof"
    });

    expect(href).toBe("/accounting?period=2026-06&frexExplorer=ledger&frexView=income&frexSearch=aapl&frexFilter=accountType%3AIncome&frexRecord=ledger%3Arun-1%3Arevenue#proof");
  });

  it("summarizes the share state for accessible link names", () => {
    const explorer = createExplorerDto();
    const filters: FinancialRecordExplorerFilterDto[] = [
      { filterId: "accountType", label: "Account Type", value: "Income", operator: "equals", tone: "Info" }
    ];

    expect(buildShareableExplorerStateSummary({
      savedViewLabel: "Income review",
      searchText: "aapl",
      selectedFilters: filters,
      selectedRow: explorer.rows[1]
    })).toBe("view Income review; search aapl; filter Account Type equals Income; record Revenue");

    expect(buildShareableExplorerStateSummary({
      savedViewLabel: null,
      searchText: "",
      selectedFilters: [],
      selectedRow: null
    })).toBe("current explorer state");
  });
});

function createExplorerDto(): FinancialRecordExplorerDto {
  const cashDetail = createDetail("ledger:run-1:cash", "Cash", "Assets");
  const revenueDetail = createDetail("ledger:run-1:revenue", "Revenue", "Income");
  return {
    explorerId: "ledger",
    title: "Ledger Explorer",
    description: "Explore retained trial-balance records and proof links.",
    sourceState: "Source-backed ledger projection from run run-1.",
    isBlocked: false,
    blockedReason: "",
    scopeItems: [],
    savedViews: [
      {
        viewId: "income",
        label: "Income review",
        description: "Income accounts with symbol evidence.",
        isSystem: false,
        isActive: false,
        filters: [{ filterId: "accountType", label: "Account Type", value: "Income", operator: "equals", tone: "Info" }],
        searchText: "aapl",
        columnIds: ["accountName", "symbol"]
      }
    ],
    summaryItems: [],
    filters: [],
    columns: [
      { columnId: "accountName", header: "Account", cellKind: "text", width: 220, isRightAligned: false },
      { columnId: "accountType", header: "Type", cellKind: "text", width: 110, isRightAligned: false },
      { columnId: "symbol", header: "Symbol", cellKind: "text", width: 90, isRightAligned: false }
    ],
    rows: [
      createRow("ledger:run-1:cash", "Cash", "Assets", "", cashDetail),
      createRow("ledger:run-1:revenue", "Revenue", "Income", "AAPL", revenueDetail)
    ],
    selectedRecord: cashDetail,
    proofActions: [],
    recordGraph: { nodes: [], edges: [] }
  };
}

function createDetail(recordId: string, title: string, recordType: string): FinancialRecordExplorerSelectedRecordDto {
  return {
    recordId,
    recordType,
    title,
    subtitle: `${recordType} - run-1`,
    description: "Source-backed ledger record.",
    tone: "Info",
    fields: [],
    proofActions: [],
    usedIn: [],
    impacts: [],
    fullRecordHref: "/api/workstation/runs/run-1/ledger/trial-balance"
  };
}

function createRow(
  recordId: string,
  label: string,
  accountType: string,
  symbol: string,
  detail: FinancialRecordExplorerSelectedRecordDto
): FinancialRecordExplorerRowDto {
  return {
    recordId,
    recordType: "ledger",
    label,
    source: "Trial balance",
    status: accountType,
    tone: "Info",
    cells: [
      { columnId: "accountName", displayValue: label, rawValue: label, tone: "Info", linkHref: "" },
      { columnId: "accountType", displayValue: accountType, rawValue: accountType, tone: "Info", linkHref: "" },
      { columnId: "symbol", displayValue: symbol || "-", rawValue: symbol, tone: "Info", linkHref: "" }
    ],
    detail
  };
}
