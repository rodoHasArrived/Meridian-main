import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { FinancialRecordExplorerShell } from "@/components/meridian/financial-record-explorer";
import type { FinancialRecordExplorerDto } from "@/types";

describe("FinancialRecordExplorerShell", () => {
  beforeEach(() => {
    window.history.replaceState(null, "", "/");
  });

  it("renders source-backed DTO rows and opens row proof detail", async () => {
    const user = userEvent.setup();
    renderExplorer();

    expect(screen.getByRole("heading", { name: "Ledger Explorer" })).toBeInTheDocument();
    expect(screen.getByLabelText("Explorer summary")).toHaveTextContent("$1,000.00");
    expect(screen.getByRole("cell", { name: "Cash" })).toBeInTheDocument();

    await user.click(screen.getByRole("row", { name: /revenue income aapl/i }));

    const detail = screen.getByLabelText("Revenue proof detail");
    expect(within(detail).getByText("Used In")).toBeInTheDocument();
    expect(within(detail).getByText("Impacts")).toBeInTheDocument();
    expect(within(detail).getByRole("link", { name: "Full record" })).toHaveAttribute("href", "/api/workstation/runs/run-1/ledger/trial-balance");
  });

  it("supports keyboard row selection and exposes the controlled proof drawer", async () => {
    const user = userEvent.setup();
    renderExplorer();

    const cashRow = screen.getByRole("row", { name: /cash assets/i });
    const revenueRow = screen.getByRole("row", { name: /revenue income aapl/i });
    const proofId = "financial-record-proof-detail-ledger";

    expect(cashRow).toHaveAttribute("aria-selected", "true");
    expect(cashRow).toHaveAttribute("aria-controls", proofId);
    expect(screen.getByLabelText("Cash proof detail")).toHaveAttribute("id", proofId);

    revenueRow.focus();
    expect(revenueRow).toHaveFocus();

    await user.keyboard("{Enter}");

    expect(revenueRow).toHaveAttribute("aria-selected", "true");
    expect(screen.getByLabelText("Revenue proof detail")).toHaveAttribute("id", proofId);

    cashRow.focus();
    await user.keyboard(" ");

    expect(cashRow).toHaveAttribute("aria-selected", "true");
    expect(screen.getByLabelText("Cash proof detail")).toHaveAttribute("id", proofId);
  });

  it("enables save view only after an operator name and material state change", async () => {
    const user = userEvent.setup();
    const onSaveView = vi.fn().mockResolvedValue(undefined);
    renderExplorer(onSaveView);

    const saveButton = screen.getByRole("button", { name: "Save view" });
    expect(saveButton).toBeDisabled();

    await user.type(screen.getByRole("textbox", { name: "Saved view name for Ledger Explorer" }), "Cash review");
    expect(saveButton).toBeDisabled();

    await user.type(screen.getByRole("textbox", { name: "Search Ledger Explorer" }), "cash");
    expect(saveButton).toBeEnabled();

    await user.click(saveButton);
    expect(onSaveView).toHaveBeenCalledWith(expect.objectContaining({ label: "Cash review", searchText: "cash" }));
  });

  it("persists the selected proof record when saving a named view", async () => {
    const user = userEvent.setup();
    const onSaveView = vi.fn().mockResolvedValue(undefined);
    renderExplorer(onSaveView);

    await user.click(screen.getByRole("row", { name: /revenue income aapl/i }));
    await user.type(screen.getByRole("textbox", { name: "Saved view name for Ledger Explorer" }), "Revenue proof");
    await user.click(screen.getByRole("button", { name: "Save view" }));

    expect(onSaveView).toHaveBeenCalledWith(expect.objectContaining({
      label: "Revenue proof",
      selectedRecordId: "ledger:run-1:revenue"
    }));
  });

  it("saves operator-named views instead of timestamp-only labels", async () => {
    const user = userEvent.setup();
    const onSaveView = vi.fn().mockResolvedValue(undefined);
    renderExplorer(onSaveView);

    await user.type(screen.getByRole("textbox", { name: "Saved view name for Ledger Explorer" }), "Cash evidence review");
    await user.type(screen.getByRole("textbox", { name: "Search Ledger Explorer" }), "cash");
    await user.click(screen.getByRole("button", { name: "Save view" }));

    expect(onSaveView).toHaveBeenCalledWith(expect.objectContaining({
      label: "Cash evidence review",
      description: expect.stringContaining("Search: cash"),
      searchText: "cash"
    }));
  });

  it("keeps row selection unchanged when a cell link is clicked", async () => {
    const user = userEvent.setup();
    renderExplorer();

    await user.click(screen.getByRole("row", { name: /revenue income aapl/i }));
    expect(screen.getByLabelText("Revenue proof detail")).toBeInTheDocument();

    await user.click(screen.getByRole("link", { name: "Cash" }));

    expect(screen.getByLabelText("Revenue proof detail")).toBeInTheDocument();
    expect(screen.queryByLabelText("Cash proof detail")).not.toBeInTheDocument();
  });

  it("keeps selected proof detail aligned to filtered rows", async () => {
    const user = userEvent.setup();
    renderExplorer();

    await user.click(screen.getByRole("row", { name: /revenue income aapl/i }));
    expect(screen.getByLabelText("Revenue proof detail")).toBeInTheDocument();

    await user.type(screen.getByRole("textbox", { name: "Search Ledger Explorer" }), "cash");

    expect(screen.queryByRole("row", { name: /revenue income aapl/i })).not.toBeInTheDocument();
    expect(screen.getByRole("row", { name: /cash assets/i })).toBeInTheDocument();
    expect(screen.getByLabelText("Cash proof detail")).toBeInTheDocument();
    expect(screen.queryByLabelText("Revenue proof detail")).not.toBeInTheDocument();
  });

  it("applies selected saved-view filters and column selections", async () => {
    const user = userEvent.setup();
    renderExplorer(undefined, createExplorerDto({
      savedViews: [
        {
          viewId: "system-ledger-default",
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
          viewId: "operator-income-symbols",
          label: "Income symbols",
          description: "Income accounts with symbol evidence.",
          isSystem: false,
          isActive: false,
          filters: [{ filterId: "accountType", label: "Account Type", value: "Income", operator: "equals", tone: "Info" }],
          searchText: "aapl",
          columnIds: ["accountName", "symbol"],
          selectedRecordId: "ledger:run-1:revenue"
        }
      ]
    }));

    await user.click(screen.getByRole("button", { name: "Income symbols" }));

    expect(screen.queryByRole("row", { name: /cash assets/i })).not.toBeInTheDocument();
    expect(screen.getByRole("row", { name: /revenue aapl/i })).toBeInTheDocument();
    expect(screen.getByRole("columnheader", { name: "Account" })).toBeInTheDocument();
    expect(screen.getByRole("columnheader", { name: "Symbol" })).toBeInTheDocument();
    expect(screen.queryByRole("columnheader", { name: "Type" })).not.toBeInTheDocument();
    expect(screen.getByLabelText("Revenue proof detail")).toBeInTheDocument();
    expect(screen.getByLabelText("Applied explorer filters")).toHaveTextContent("Income");
    expect(screen.getByRole("textbox", { name: "Search Ledger Explorer" })).toHaveValue("aapl");
  });

  it("hydrates search, saved view, filter snapshot, and selected record from the URL", () => {
    const filters = [{ filterId: "accountType", label: "Account Type", value: "Income", operator: "equals", tone: "Info" as const }];
    const params = new URLSearchParams({
      frexView: "operator-income-symbols",
      frexSearch: "aapl",
      frexRecord: "ledger:run-1:revenue",
      frexFilters: JSON.stringify(filters),
      frexColumns: "accountName"
    });
    window.history.replaceState(null, "", `/accounting/ledger?${params.toString()}`);

    renderExplorer(undefined, createExplorerDto({
      savedViews: [
        {
          viewId: "system-ledger-default",
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
          viewId: "operator-income-symbols",
          label: "Income symbols",
          description: "Income accounts with symbol evidence.",
          isSystem: false,
          isActive: false,
          filters,
          searchText: "",
          columnIds: ["accountName", "symbol"],
          selectedRecordId: "ledger:run-1:revenue"
        }
      ]
    }));

    expect(screen.getByRole("button", { name: "Income symbols" })).toHaveAttribute("aria-current", "true");
    expect(screen.getByRole("textbox", { name: "Search Ledger Explorer" })).toHaveValue("aapl");
    expect(screen.getByLabelText("Applied explorer filters")).toHaveTextContent("Income");
    expect(screen.getByRole("columnheader", { name: "Account" })).toBeInTheDocument();
    expect(screen.queryByRole("columnheader", { name: "Symbol" })).not.toBeInTheDocument();
    expect(screen.getByLabelText("Revenue proof detail")).toBeInTheDocument();
    expect(screen.queryByRole("row", { name: /cash assets/i })).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Share Ledger Explorer evidence state" })).toHaveAttribute(
      "href",
      expect.stringContaining("frexRecord=ledger%3Arun-1%3Arevenue")
    );
    expect(screen.getByRole("link", { name: "Share Ledger Explorer evidence state" })).toHaveAttribute(
      "href",
      expect.stringContaining("frexExplorer=ledger")
    );
    expect(screen.getByRole("link", { name: "Share Ledger Explorer evidence state" })).toHaveAttribute(
      "href",
      expect.stringContaining("frexColumns=accountName")
    );
    expect(window.location.search).toContain("frexExplorer=ledger");
    expect(window.location.search).toContain("frexView=operator-income-symbols");
    expect(window.location.search).toContain("frexSearch=aapl");
    expect(window.location.search).toContain("frexColumns=accountName");
  });

  it("ignores shared URL state scoped to another explorer", () => {
    const filters = [{ filterId: "accountType", label: "Account Type", value: "Income", operator: "equals", tone: "Info" as const }];
    const params = new URLSearchParams({
      frexExplorer: "ledger",
      frexView: "operator-income-symbols",
      frexSearch: "cash",
      frexRecord: "ledger:run-1:revenue",
      frexFilters: JSON.stringify(filters),
      frexColumns: "accountName,symbol"
    });
    window.history.replaceState(null, "", `/accounting/security-master?${params.toString()}`);

    renderExplorer(undefined, createSecurityInstrumentExplorerDto());

    expect(screen.getByRole("textbox", { name: "Search Security & Instrument Explorer" })).toHaveValue("");
    expect(screen.getByRole("cell", { name: "Apple Inc." })).toBeInTheDocument();
    expect(screen.getByLabelText("Apple Inc. proof detail")).toBeInTheDocument();
    expect(window.location.search).toContain("frexExplorer=security-instrument");
    expect(window.location.search).not.toContain("frexSearch=cash");
    expect(window.location.search).not.toContain("frexColumns=accountName");
    expect(screen.getByRole("link", { name: "Share Security & Instrument Explorer evidence state" })).toHaveAttribute(
      "href",
      expect.stringContaining("frexExplorer=security-instrument")
    );
  });

  it("lets operators save filter-only shared links as named views", async () => {
    const user = userEvent.setup();
    const onSaveView = vi.fn().mockResolvedValue(undefined);
    const filters = [{ filterId: "accountType", label: "Account Type", value: "Income", operator: "equals", tone: "Info" as const }];
    const params = new URLSearchParams({
      frexFilters: JSON.stringify(filters),
      frexColumns: "accountName"
    });
    window.history.replaceState(null, "", `/accounting/ledger?${params.toString()}`);

    renderExplorer(onSaveView);

    const saveButton = screen.getByRole("button", { name: "Save view" });
    expect(screen.getByLabelText("Applied explorer filters")).toHaveTextContent("Income");
    expect(saveButton).toBeDisabled();

    await user.type(screen.getByRole("textbox", { name: "Saved view name for Ledger Explorer" }), "Income link review");
    expect(saveButton).toBeEnabled();

    await user.click(saveButton);

    expect(onSaveView).toHaveBeenCalledWith(expect.objectContaining({
      label: "Income link review",
      filters,
      searchText: "",
      columnIds: ["accountName"],
      description: expect.stringContaining("Filters: Account Type=Income")
    }));
  });

  it("renders Security & Instrument Explorer DTO fields used by WPF parity proof", async () => {
    const user = userEvent.setup();
    renderExplorer(undefined, createSecurityInstrumentExplorerDto());

    expect(screen.getByRole("heading", { name: "Security & Instrument Explorer" })).toBeInTheDocument();
    expect(screen.getByRole("cell", { name: "Apple Inc." })).toBeInTheDocument();
    expect(screen.getByRole("cell", { name: "96%" })).toBeInTheDocument();
    expect(screen.getByRole("cell", { name: "Ready" })).toBeInTheDocument();
    expect(screen.getByRole("cell", { name: "1 projection" })).toBeInTheDocument();

    await user.click(screen.getByRole("row", { name: /apple inc.*96%.*ready.*1 projection/i }));

    const detail = screen.getByLabelText("Apple Inc. proof detail");
    expect(within(detail).getByText("Instrument Identity")).toBeInTheDocument();
    expect(within(detail).getByText("Provider Evidence")).toBeInTheDocument();
    expect(within(detail).getByText("AssetOperations Readiness")).toBeInTheDocument();
    expect(within(detail).getByText("Ledger Impact")).toBeInTheDocument();
    expect(within(detail).getAllByText("Report Usage").length).toBeGreaterThan(0);
    expect(within(detail).getByRole("link", { name: "Open instrument passport" })).toHaveAttribute("href", "/api/workstation/security-master/securities/11111111-1111-1111-1111-111111111111/passport");
  });

  it("renders Number Passport proof facets from selected record evidence", () => {
    renderExplorer(undefined, loadSecurityInstrumentParityFixture());

    const passport = screen.getByLabelText("AAPL - Apple Inc. Number Passport");
    expect(within(passport).getByRole("heading", { name: "Number Passport" })).toBeInTheDocument();
    for (const label of ["Source", "Freshness", "Reconciliation", "Approvals", "Report Usage", "Blockers", "Evidence Packet", "Audit Trail"]) {
      expect(within(passport).getByText(label)).toBeInTheDocument();
    }

    expect(within(passport).getByRole("link", { name: "Security Master" })).toHaveAttribute("href", "/api/workstation/security-master/securities/11111111-1111-1111-1111-111111111111");
    expect(within(passport).getByText("No approval link in this record detail.")).toBeInTheDocument();
    expect(within(passport).getByText("None disclosed")).toBeInTheDocument();
    expect(within(passport).getByRole("link", { name: "Instrument passport" })).toHaveAttribute("href", "/api/workstation/security-master/securities/11111111-1111-1111-1111-111111111111/passport");
    expect(within(passport).getByText("5 graph nodes")).toBeInTheDocument();
  });

  it("shows blocked source state with disabled actions instead of synthetic rows", () => {
    renderExplorer(undefined, {
      ...createExplorerDto(),
      isBlocked: true,
      blockedReason: "Strategy run read service is not registered.",
      rows: [],
      columns: [],
      selectedRecord: null,
      proofActions: [
        {
          actionId: "source-blocked",
          label: "Source unavailable",
          description: "Strategy run read service is not registered.",
          href: "",
          isEnabled: false,
          disabledReason: "Strategy run read service is not registered.",
          tone: "Danger"
        }
      ],
      recordGraph: { nodes: [], edges: [] }
    });

    expect(screen.getByRole("status")).toHaveTextContent("Strategy run read service is not registered.");
    expect(screen.getByRole("button", { name: "Source unavailable" })).toBeDisabled();
    expect(screen.queryByLabelText(/Number Passport/)).not.toBeInTheDocument();
  });

  it("renders the shared Security & Instrument Explorer parity DTO", () => {
    renderExplorer(undefined, loadSecurityInstrumentParityFixture());

    expect(screen.getByRole("heading", { name: "Security & Instrument Explorer" })).toBeInTheDocument();
    expect(screen.getByLabelText("Explorer summary")).toHaveTextContent("Provider Evidence");
    expect(screen.getByRole("cell", { name: "AAPL - Apple Inc." })).toBeInTheDocument();
    expect(screen.getByRole("cell", { name: "96% trusted" })).toBeInTheDocument();
    expect(screen.getByRole("cell", { name: "Ready" })).toBeInTheDocument();
    expect(screen.getByRole("cell", { name: "1 projection" })).toBeInTheDocument();
    expect(screen.getByRole("cell", { name: "Board pack holdings.aapl.market-value" })).toBeInTheDocument();

    const detail = screen.getByLabelText("AAPL - Apple Inc. proof detail");
    expect(within(detail).getByText("Instrument Identity")).toBeInTheDocument();
    expect(within(detail).getByText("AAPL / US0378331005")).toBeInTheDocument();
    expect(within(detail).getByText("Provider Evidence")).toBeInTheDocument();
    expect(within(detail).getByText("AssetOperations Readiness")).toBeInTheDocument();
    expect(within(detail).getByText("Ledger Impact")).toBeInTheDocument();
    expect(within(detail).getAllByText("Report Usage").length).toBeGreaterThan(0);
    expect(within(detail).getByText("Portfolio position")).toBeInTheDocument();
    expect(within(detail).getAllByText("AssetOperations reconciliation").length).toBeGreaterThan(0);
    expect(within(detail).getAllByText("Report usage")).toHaveLength(2);
    expect(screen.getByLabelText("Record graph")).toHaveTextContent("AssetOperations ready");
  });
});

function renderExplorer(
  onSaveView?: Parameters<typeof FinancialRecordExplorerShell>[0]["onSaveView"],
  explorer: FinancialRecordExplorerDto = createExplorerDto()
) {
  return render(
    <FinancialRecordExplorerShell
      explorerLabel="Accounting"
      title="Ledger Explorer"
      description="Static fallback"
      scopeItems={[]}
      savedViews={[]}
      summaryItems={[]}
      appliedFilters={[]}
      explorer={explorer}
      onSaveView={onSaveView}
    >
      <div>Fallback static content</div>
    </FinancialRecordExplorerShell>
  );
}

function createSecurityInstrumentExplorerDto(): FinancialRecordExplorerDto {
  const securityHref = "/api/workstation/security-master/securities/11111111-1111-1111-1111-111111111111";
  const passportHref = `${securityHref}/passport`;
  const operationsHref = "/api/workstation/assets/11111111-1111-1111-1111-111111111111/operations";
  const reportHref = "/api/workstation/financial-record-explorers/report-line-provenance?lineKey=holdings.aapl.market-value&sourceId=AAPL";
  const detail = {
    recordId: "security:11111111-1111-1111-1111-111111111111",
    recordType: "security-instrument",
    title: "Apple Inc.",
    subtitle: "AAPL · USD · Equity",
    description: "Security Master identity with provider, operations, ledger, and report proof.",
    tone: "Success" as const,
    fields: [
      { label: "Instrument Identity", value: "Apple Inc. / AAPL", detail: "SecurityId 11111111-1111-1111-1111-111111111111.", tone: "Success" as const },
      { label: "Provider Evidence", value: "Polygon primary · 96%", detail: passportHref, tone: "Success" as const },
      { label: "AssetOperations Readiness", value: "Ready", detail: operationsHref, tone: "Success" as const },
      { label: "Ledger Impact", value: "1 projection", detail: "Ledger projection retained for the instrument.", tone: "Info" as const },
      { label: "Report Usage", value: "1 report line", detail: reportHref, tone: "Info" as const }
    ],
    proofActions: [
      { actionId: "open-security-master", label: "Open Security Master", description: "Open Security Master detail.", href: securityHref, isEnabled: true, disabledReason: "", tone: "Success" as const },
      { actionId: "open-instrument-passport", label: "Open instrument passport", description: "Open provider evidence.", href: passportHref, isEnabled: true, disabledReason: "", tone: "Success" as const },
      { actionId: "open-asset-operations", label: "Open AssetOperations", description: "Open operations readiness.", href: operationsHref, isEnabled: true, disabledReason: "", tone: "Info" as const },
      { actionId: "open-report-line-provenance", label: "Open report-line provenance", description: "Open report usage.", href: reportHref, isEnabled: true, disabledReason: "", tone: "Info" as const }
    ],
    usedIn: [
      { relationshipId: "portfolio", label: "Portfolio position", description: "Portfolio uses Apple Inc.", href: "/api/workstation/financial-record-explorers/portfolio", tone: "Info" as const },
      { relationshipId: "ledger", label: "Ledger trial balance", description: "Ledger projection uses Apple Inc.", href: "/api/workstation/financial-record-explorers/ledger", tone: "Info" as const },
      { relationshipId: "report", label: "Report-line provenance", description: "Board pack reports Apple Inc.", href: reportHref, tone: "Info" as const }
    ],
    impacts: [
      { relationshipId: "passport", label: "Instrument passport", description: "Provider confidence and identity evidence.", href: passportHref, tone: "Success" as const },
      { relationshipId: "operations", label: "AssetOperations readiness", description: "Cash-flow and reconciliation readiness.", href: operationsHref, tone: "Success" as const },
      { relationshipId: "ledger", label: "Ledger projection", description: "Ledger impact is retained.", href: "/api/workstation/runs/run-1/ledger/journal", tone: "Info" as const }
    ],
    fullRecordHref: securityHref
  };

  return {
    explorerId: "security-instrument",
    title: "Security & Instrument Explorer",
    description: "Explore Security Master references used by retained accounting and portfolio records.",
    sourceState: "Source-backed Security Master references from run run-1.",
    isBlocked: false,
    blockedReason: "",
    scopeItems: [{ label: "Run", value: "run-1", tone: "Info" }],
    savedViews: [{ viewId: "system-security-instrument-default", label: "Security references", description: "Default security view.", isSystem: true, isActive: true, filters: [], searchText: "" }],
    summaryItems: [{ label: "Securities", value: "1", detail: "Distinct resolved instruments.", tone: "Default" }],
    filters: [{ filterId: "coverage", label: "Coverage", value: "Resolved", operator: "equals", tone: "Info" }],
    columns: [
      { columnId: "security", header: "Security", cellKind: "text", width: 220, isRightAligned: false },
      { columnId: "identifierConfidence", header: "Identifier Confidence", cellKind: "text", width: 170, isRightAligned: false },
      { columnId: "operations", header: "Operations", cellKind: "text", width: 140, isRightAligned: false },
      { columnId: "cashFlow", header: "Cash Flow", cellKind: "text", width: 120, isRightAligned: false },
      { columnId: "ledger", header: "Ledger", cellKind: "text", width: 120, isRightAligned: false }
    ],
    rows: [{
      recordId: "security:11111111-1111-1111-1111-111111111111",
      recordType: "security-instrument",
      label: "Apple Inc.",
      source: "AAPL",
      status: "Resolved",
      tone: "Success",
      cells: [
        { columnId: "security", displayValue: "Apple Inc.", rawValue: "Apple Inc.", tone: "Success", linkHref: securityHref },
        { columnId: "identifierConfidence", displayValue: "96%", rawValue: "0.96", tone: "Success", linkHref: passportHref },
        { columnId: "operations", displayValue: "Ready", rawValue: "Ready", tone: "Success", linkHref: operationsHref },
        { columnId: "cashFlow", displayValue: "1 projected", rawValue: "1", tone: "Info", linkHref: operationsHref },
        { columnId: "ledger", displayValue: "1 projection", rawValue: "1", tone: "Info", linkHref: "/api/workstation/runs/run-1/ledger/journal" }
      ],
      detail
    }],
    selectedRecord: detail,
    proofActions: [],
    recordGraph: { nodes: [], edges: [] }
  };
}

function createExplorerDto(overrides: Partial<FinancialRecordExplorerDto> = {}): FinancialRecordExplorerDto {
  const cashDetail = {
    recordId: "ledger:run-1:cash",
    recordType: "Ledger account",
    title: "Cash",
    subtitle: "Assets - run-1",
    description: "Source-backed cash balance.",
    tone: "Success" as const,
    fields: [
      { label: "Balance", value: "$1,000.00", detail: "Source-backed balance.", tone: "Success" as const }
    ],
    proofActions: [
      {
        actionId: "open-source",
        label: "Open source record",
        description: "Open the retained source record.",
        href: "/api/workstation/runs/run-1/ledger/trial-balance",
        isEnabled: true,
        disabledReason: "",
        tone: "Info" as const
      }
    ],
    usedIn: [
      {
        relationshipId: "ledger-run",
        label: "Run ledger",
        description: "Trial balance belongs to run run-1.",
        href: "/api/workstation/runs/run-1/ledger",
        tone: "Info" as const
      }
    ],
    impacts: [
      {
        relationshipId: "balance-sheet",
        label: "Balance sheet",
        description: "Cash contributes to assets.",
        href: "/api/workstation/accounting",
        tone: "Success" as const
      }
    ],
    fullRecordHref: "/api/workstation/runs/run-1/ledger/trial-balance"
  };

  const revenueDetail = {
    ...cashDetail,
    recordId: "ledger:run-1:revenue",
    title: "Revenue",
    subtitle: "Income - run-1",
    description: "Source-backed revenue balance."
  };

  return {
    explorerId: "ledger",
    title: "Ledger Explorer",
    description: "Explore retained trial-balance records and proof links.",
    sourceState: "Source-backed ledger projection from run run-1.",
    isBlocked: false,
    blockedReason: "",
    scopeItems: [
      { label: "Run", value: "run-1", tone: "Info" },
      { label: "Source", value: "Journal entries and ledger detail", tone: "Default" }
    ],
    savedViews: [
      {
        viewId: "system-ledger-default",
        label: "Controller review",
        description: "Default controller review.",
        isSystem: true,
        isActive: true,
        filters: [],
        searchText: ""
      }
    ],
    summaryItems: [
      { label: "Assets", value: "$1,000.00", detail: "Source-backed asset balance.", tone: "Success" },
      { label: "Rows", value: "2", detail: "Retained rows.", tone: "Default" }
    ],
    filters: [
      { filterId: "accounts", label: "All accounts", value: "All accounts", operator: "equals", tone: "Info" }
    ],
    columns: [
      { columnId: "accountName", header: "Account", cellKind: "text", width: 220, isRightAligned: false },
      { columnId: "accountType", header: "Type", cellKind: "text", width: 110, isRightAligned: false },
      { columnId: "symbol", header: "Symbol", cellKind: "text", width: 90, isRightAligned: false }
    ],
    rows: [
      {
        recordId: "ledger:run-1:cash",
        recordType: "ledger",
        label: "Cash",
        source: "Trial balance",
        status: "Assets",
        tone: "Success",
        cells: [
          { columnId: "accountName", displayValue: "Cash", rawValue: "Cash", tone: "Success", linkHref: "#cash-record" },
          { columnId: "accountType", displayValue: "Assets", rawValue: "Assets", tone: "Default", linkHref: "" },
          { columnId: "symbol", displayValue: "-", rawValue: "", tone: "Default", linkHref: "" }
        ],
        detail: cashDetail
      },
      {
        recordId: "ledger:run-1:revenue",
        recordType: "ledger",
        label: "Revenue",
        source: "Trial balance",
        status: "Income",
        tone: "Default",
        cells: [
          { columnId: "accountName", displayValue: "Revenue", rawValue: "Revenue", tone: "Default", linkHref: "" },
          { columnId: "accountType", displayValue: "Income", rawValue: "Income", tone: "Default", linkHref: "" },
          { columnId: "symbol", displayValue: "AAPL", rawValue: "AAPL", tone: "Default", linkHref: "" }
        ],
        detail: revenueDetail
      }
    ],
    selectedRecord: cashDetail,
    proofActions: [
      {
        actionId: "evidence",
        label: "Evidence packet",
        description: "Open retained evidence.",
        href: "/api/workstation/evidence/subjects/run/run-1",
        isEnabled: true,
        disabledReason: "",
        tone: "Info"
      }
    ],
    recordGraph: {
      nodes: [
        { nodeId: "ledger:run-1:cash", label: "Cash", nodeType: "ledger", tone: "Success", href: "/api/workstation/runs/run-1/ledger/trial-balance" },
        { nodeId: "rel:ledger-run", label: "Run ledger", nodeType: "relationship", tone: "Info", href: "/api/workstation/runs/run-1/ledger" }
      ],
      edges: [
        { sourceNodeId: "ledger:run-1:cash", targetNodeId: "rel:ledger-run", label: "used in", tone: "Info" }
      ]
    },
    ...overrides
  };
}

function loadSecurityInstrumentParityFixture(): FinancialRecordExplorerDto {
  const fixturePath = resolve(process.cwd(), "../../../tests/fixtures/security-instrument-explorer-parity.json");
  return JSON.parse(readFileSync(fixturePath, "utf8")) as FinancialRecordExplorerDto;
}
