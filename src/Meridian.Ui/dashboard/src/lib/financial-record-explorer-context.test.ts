import { describe, expect, it } from "vitest";
import {
  buildExplorerShareHref,
  buildSavedViewDescription,
  readExplorerUrlState,
  resolveInitialRecordId,
  resolveInitialSearchText,
  resolveInitialViewId
} from "@/lib/financial-record-explorer-context";
import type {
  FinancialRecordExplorerDto,
  FinancialRecordExplorerFilterDto,
  FinancialRecordExplorerRowDto,
  FinancialRecordExplorerSelectedRecordDto
} from "@/types";

describe("financial record explorer shared context", () => {
  it("reads scoped URL state and ignores state for another explorer", () => {
    const filters = [filter("accountType", "Account Type", "Income")];
    const search = `?frexExplorer=ledger&frexView=income&frexSearch=aapl&frexRecord=ledger%3Arevenue&frexFilters=${encodeURIComponent(JSON.stringify(filters))}&frexColumns=accountName,symbol`;

    expect(readExplorerUrlState("ledger", search)).toEqual({
      viewId: "income",
      searchText: "aapl",
      recordId: "ledger:revenue",
      filters,
      columnIds: ["accountName", "symbol"]
    });
    expect(readExplorerUrlState("security-instrument", search)).toEqual({
      viewId: null,
      searchText: null,
      recordId: null,
      filters: null,
      columnIds: null
    });
  });

  it("builds share links that preserve unrelated context and clear empty explorer params", () => {
    const location = {
      pathname: "/accounting/ledger",
      search: "?theme=ops&frexView=old&frexSearch=old&frexRecord=old&frexFilters=%5B%5D",
      hash: "#proof"
    };
    const href = buildExplorerShareHref({
      explorerId: "ledger",
      viewId: "income",
      searchText: "aapl",
      recordId: "ledger:revenue",
      filters: [filter("accountType", "Account Type", "Income")],
      columnIds: ["accountName", "symbol", "accountName"]
    }, location);
    const url = new URL(href, "http://localhost");

    expect(url.pathname).toBe("/accounting/ledger");
    expect(url.hash).toBe("#proof");
    expect(url.searchParams.get("theme")).toBe("ops");
    expect(url.searchParams.get("frexExplorer")).toBe("ledger");
    expect(url.searchParams.get("frexView")).toBe("income");
    expect(url.searchParams.get("frexSearch")).toBe("aapl");
    expect(url.searchParams.get("frexRecord")).toBe("ledger:revenue");
    expect(url.searchParams.get("frexColumns")).toBe("accountName,symbol");
    expect(JSON.parse(url.searchParams.get("frexFilters") ?? "[]")).toEqual([filter("accountType", "Account Type", "Income")]);

    const clearedHref = buildExplorerShareHref({
      explorerId: "ledger",
      viewId: "",
      searchText: "",
      recordId: "",
      filters: [],
      columnIds: []
    }, location);
    const clearedUrl = new URL(clearedHref, "http://localhost");
    expect(clearedUrl.searchParams.get("theme")).toBe("ops");
    expect(clearedUrl.searchParams.get("frexExplorer")).toBe("ledger");
    expect(clearedUrl.searchParams.has("frexView")).toBe(false);
    expect(clearedUrl.searchParams.has("frexSearch")).toBe(false);
    expect(clearedUrl.searchParams.has("frexRecord")).toBe(false);
    expect(clearedUrl.searchParams.has("frexFilters")).toBe(false);
    expect(clearedUrl.searchParams.has("frexColumns")).toBe(false);
  });

  it("resolves initial view, search, record, and saved-view copy from shared context", () => {
    const model = explorer();

    expect(resolveInitialViewId(model, "default", "income")).toBe("income");
    expect(resolveInitialViewId(model, "default", "missing")).toBe("default");
    expect(resolveInitialSearchText(model, "income", null)).toBe("aapl");
    expect(resolveInitialSearchText(model, "income", "")).toBe("");
    expect(resolveInitialRecordId(model, "ledger:revenue")).toBe("ledger:revenue");
    expect(resolveInitialRecordId(model, null, "income")).toBe("ledger:revenue");
    expect(resolveInitialRecordId(model, "missing")).toBe("ledger:cash");
    expect(buildSavedViewDescription(" cash ", [filter("accountType", "Account Type", "Assets")], "Controller review")).toBe(
      "Search: cash Filters: Account Type=Assets Based on Controller review."
    );
    expect(buildSavedViewDescription("", [], undefined)).toBe("Saved from current explorer context.");
  });

  it("restores the active saved view selected record when no URL record is present", () => {
    const model = withActiveSavedRecord(explorer(), "ledger:revenue");

    expect(resolveInitialRecordId(model, null)).toBe("ledger:revenue");
  });
});

function filter(filterId: string, label: string, value: string): FinancialRecordExplorerFilterDto {
  return { filterId, label, value, operator: "equals", tone: "Info" };
}

function explorer(): FinancialRecordExplorerDto {
  const cash = row("ledger:cash", "Cash");
  const revenue = row("ledger:revenue", "Revenue");
  return {
    explorerId: "ledger",
    title: "Ledger Explorer",
    description: "Trial balance explorer.",
    sourceState: "Source-backed ledger projection from run run-1.",
    isBlocked: false,
    blockedReason: "",
    scopeItems: [],
    savedViews: [
      {
        viewId: "default",
        label: "Controller review",
        description: "Default controller review.",
        isSystem: true,
        isActive: true,
        filters: [],
        searchText: "",
        columnIds: [],
        selectedRecordId: ""
      },
      {
        viewId: "income",
        label: "Income symbols",
        description: "Income accounts with symbol evidence.",
        isSystem: false,
        isActive: false,
        filters: [filter("accountType", "Account Type", "Income")],
        searchText: "aapl",
        columnIds: [],
        selectedRecordId: "ledger:revenue"
      }
    ],
    summaryItems: [],
    filters: [],
    columns: [],
    rows: [cash, revenue],
    selectedRecord: cash.detail,
    proofActions: [],
    recordGraph: { nodes: [], edges: [] }
  };
}

function withActiveSavedRecord(model: FinancialRecordExplorerDto, selectedRecordId: string): FinancialRecordExplorerDto {
  return {
    ...model,
    savedViews: model.savedViews.map((view) => view.viewId === "income"
      ? { ...view, isActive: true, selectedRecordId }
      : { ...view, isActive: false })
  };
}

function row(recordId: string, label: string): FinancialRecordExplorerRowDto {
  const detail = selectedRecord(recordId, label);
  return {
    recordId,
    recordType: "Ledger account",
    label,
    source: "Trial balance",
    status: "Ready",
    tone: "Success",
    cells: [],
    detail
  };
}

function selectedRecord(recordId: string, title: string): FinancialRecordExplorerSelectedRecordDto {
  return {
    recordId,
    recordType: "Ledger account",
    title,
    subtitle: "Trial balance",
    description: "Retained trial balance account.",
    tone: "Success",
    fields: [],
    proofActions: [],
    usedIn: [],
    impacts: [],
    fullRecordHref: ""
  };
}
