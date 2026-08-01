import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
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
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();

    await user.click(screen.getByRole("row", { name: /revenue income aapl/i }));

    const detail = screen.getByRole("dialog", { name: "Revenue proof detail" });
    const passport = within(detail).getByRole("region", { name: "Revenue Number Passport" });
    expect(passport).toBeInTheDocument();
    [
      "Source",
      "Freshness",
      "Reconciliation",
      "Approvals",
      "Report Usage",
      "Blockers",
      "Evidence Packet",
      "Audit Trail"
    ].forEach((label) => {
      expect(within(passport).getByText(label)).toBeInTheDocument();
    });
    expect(within(detail).getByText("Used In")).toBeInTheDocument();
    expect(within(detail).getByText("Impacts")).toBeInTheDocument();
    expect(within(detail).getByRole("link", { name: "Full record" })).toHaveAttribute("href", "/api/workstation/runs/run-1/ledger/trial-balance");
    expect(screen.getByRole("link", {
      name: "Share Ledger Explorer evidence state: view Controller review; record Revenue"
    })).toHaveAttribute(
      "href",
      "/?frexExplorer=ledger&frexView=system-ledger-default&frexRecord=ledger%3Arun-1%3Arevenue"
    );
  });

  it("publishes the selected record so route-owned detail can stay synchronized", async () => {
    const onSelectRecord = vi.fn();

    renderExplorer(undefined, createSecurityInstrumentExplorerDto(), onSelectRecord);

    await waitFor(() => expect(onSelectRecord).toHaveBeenCalledWith("security:11111111-1111-1111-1111-111111111111"));
  });

  it("opens row proof detail via keyboard activation", async () => {
    const user = userEvent.setup();
    renderExplorer();

    const row = screen.getByRole("row", { name: /revenue income aapl/i });
    expect(row).toHaveAttribute("aria-selected", "false");

    act(() => row.focus());
    expect(row).toHaveAttribute("tabindex", "0");
    await user.keyboard("{Enter}");

    expect(screen.getByRole("dialog", { name: "Revenue proof detail" })).toBeInTheDocument();
    expect(screen.getByRole("row", { name: /revenue income aapl/i })).toHaveAttribute("aria-selected", "true");

    await user.keyboard("{Escape}");
    expect(screen.queryByRole("dialog", { name: "Revenue proof detail" })).not.toBeInTheDocument();
    expect(row).toHaveFocus();

    // Space reopens the selected proof record (default scroll suppressed).
    await user.keyboard(" ");
    expect(screen.getByRole("dialog", { name: "Revenue proof detail" })).toBeInTheDocument();
  });

  it("exposes the record table as a selectable grid", () => {
    renderExplorer();

    const grid = screen.getByRole("grid", { name: "Ledger Explorer records" });
    const rows = within(grid).getAllByRole("row").filter((row) => row.hasAttribute("aria-selected"));

    expect(rows.length).toBeGreaterThan(1);
    // aria-selected is only meaningful inside a grid; on a plain table row assistive
    // technology has no selection concept to report.
    rows.forEach((row, index) => {
      expect(row).toHaveAttribute("aria-selected");
      // The header occupies row 1, so the first record is row 2.
      expect(row).toHaveAttribute("aria-rowindex", String(index + 2));
    });
    expect(grid).toHaveAttribute("aria-rowcount", String(rows.length + 1));

    const header = within(grid).getAllByRole("row").find((row) => !row.hasAttribute("aria-selected"));
    expect(header).toHaveAttribute("aria-rowindex", "1");
  });

  it("keeps one tab stop for the whole grid", () => {
    renderExplorer();

    const grid = screen.getByRole("grid", { name: "Ledger Explorer records" });
    const rows = within(grid).getAllByRole("row").filter((row) => row.hasAttribute("aria-selected"));

    // Every row being tabbable would put a long explorer many Tab presses away from the
    // next control; the grid keeps a single tab stop and moves it with the arrow keys.
    const tabbable = rows.filter((row) => row.getAttribute("tabindex") === "0");
    expect(tabbable).toHaveLength(1);
  });

  it("moves row focus with the arrow, Home, and End keys", async () => {
    const user = userEvent.setup();
    renderExplorer();

    const grid = screen.getByRole("grid", { name: "Ledger Explorer records" });
    const rows = within(grid).getAllByRole("row").filter((row) => row.hasAttribute("aria-selected"));
    expect(rows.length).toBeGreaterThanOrEqual(2);

    act(() => rows[0].focus());
    expect(rows[0]).toHaveFocus();

    await user.keyboard("{ArrowDown}");
    expect(rows[1]).toHaveFocus();
    expect(rows[1]).toHaveAttribute("tabindex", "0");
    expect(rows[0]).toHaveAttribute("tabindex", "-1");

    await user.keyboard("{ArrowUp}");
    expect(rows[0]).toHaveFocus();

    await user.keyboard("{End}");
    expect(rows[rows.length - 1]).toHaveFocus();

    await user.keyboard("{Home}");
    expect(rows[0]).toHaveFocus();
  });

  it("does not move focus past the first or last row", async () => {
    const user = userEvent.setup();
    renderExplorer();

    const grid = screen.getByRole("grid", { name: "Ledger Explorer records" });
    const rows = within(grid).getAllByRole("row").filter((row) => row.hasAttribute("aria-selected"));

    act(() => rows[0].focus());
    await user.keyboard("{ArrowUp}");
    expect(rows[0]).toHaveFocus();

    act(() => rows[rows.length - 1].focus());
    await user.keyboard("{ArrowDown}");
    expect(rows[rows.length - 1]).toHaveFocus();
  });

  it("marks only the selected row as selected", async () => {
    const user = userEvent.setup();
    renderExplorer();

    await user.click(screen.getByRole("row", { name: /revenue income aapl/i }));
    await user.keyboard("{Escape}");

    const grid = screen.getByRole("grid", { name: "Ledger Explorer records" });
    const selected = within(grid)
      .getAllByRole("row")
      .filter((row) => row.getAttribute("aria-selected") === "true");

    expect(selected).toHaveLength(1);
    expect(selected[0]).toHaveTextContent("Revenue");
  });

  it("has no detectable accessibility violations in the record grid", async () => {
    const { container } = renderExplorer();

    const results = await axe(container);

    expect(results.violations).toHaveLength(0);
  });

  it("requires an operator name before saving a material view change", async () => {
    const user = userEvent.setup();
    const onSaveView = vi.fn().mockResolvedValue(undefined);
    renderExplorer(onSaveView);

    const saveButton = screen.getByRole("button", { name: "Save view" });
    expect(saveButton).toBeDisabled();

    await user.type(screen.getByRole("textbox", { name: "Search Ledger Explorer" }), "cash");
    expect(saveButton).toBeDisabled();

    await user.type(screen.getByRole("textbox", { name: "Saved view name" }), "Cash review");
    expect(saveButton).toBeEnabled();

    await user.click(saveButton);
    expect(onSaveView).toHaveBeenCalledWith(expect.objectContaining({
      label: "Cash review",
      searchText: "cash"
    }));
  });

  it("saves operator-named views without timestamp-only labels", async () => {
    const user = userEvent.setup();
    const onSaveView = vi.fn().mockResolvedValue(undefined);
    renderExplorer(onSaveView);

    const saveButton = screen.getByRole("button", { name: "Save view" });
    expect(saveButton).toBeDisabled();

    await user.type(screen.getByRole("textbox", { name: "Saved view name" }), "Month-end evidence");
    expect(saveButton).toBeEnabled();

    await user.click(saveButton);
    expect(onSaveView).toHaveBeenCalledWith(expect.objectContaining({
      label: "Month-end evidence",
      description: "Saved from Controller review.",
      searchText: ""
    }));
  });

  it("keeps row selection unchanged when a cell link is clicked", async () => {
    const user = userEvent.setup();
    renderExplorer();

    await user.click(screen.getByRole("row", { name: /revenue income aapl/i }));
    expect(screen.getByRole("dialog", { name: "Revenue proof detail" })).toBeInTheDocument();
    await user.keyboard("{Escape}");

    await user.click(screen.getByRole("link", { name: "Cash" }));

    expect(screen.getByRole("row", { name: /revenue income aapl/i })).toHaveAttribute("aria-selected", "true");
    expect(screen.queryByRole("dialog", { name: "Revenue proof detail" })).not.toBeInTheDocument();
    expect(screen.queryByRole("dialog", { name: "Cash proof detail" })).not.toBeInTheDocument();
  });

  it("keeps selected proof detail aligned to filtered rows", async () => {
    const user = userEvent.setup();
    renderExplorer();

    await user.click(screen.getByRole("row", { name: /revenue income aapl/i }));
    expect(screen.getByRole("dialog", { name: "Revenue proof detail" })).toBeInTheDocument();
    await user.keyboard("{Escape}");

    await user.type(screen.getByRole("textbox", { name: "Search Ledger Explorer" }), "cash");

    expect(screen.queryByRole("row", { name: /revenue income aapl/i })).not.toBeInTheDocument();
    await user.click(screen.getByRole("row", { name: /cash assets/i }));
    expect(screen.getByRole("dialog", { name: "Cash proof detail" })).toBeInTheDocument();
    expect(screen.queryByRole("dialog", { name: "Revenue proof detail" })).not.toBeInTheDocument();
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
          columnIds: []
        },
        {
          viewId: "operator-income-symbols",
          label: "Income symbols",
          description: "Income accounts with symbol evidence.",
          isSystem: false,
          isActive: false,
          filters: [{ filterId: "accountType", label: "Account Type", value: "Income", operator: "equals", tone: "Info" }],
          searchText: "aapl",
          columnIds: ["accountName", "symbol"]
        }
      ]
    }));

    await user.click(screen.getByRole("button", { name: "Income symbols" }));

    expect(screen.queryByRole("row", { name: /cash assets/i })).not.toBeInTheDocument();
    expect(screen.getByRole("row", { name: /revenue aapl/i })).toBeInTheDocument();
    expect(screen.getByRole("columnheader", { name: "Account" })).toBeInTheDocument();
    expect(screen.getByRole("columnheader", { name: "Symbol" })).toBeInTheDocument();
    expect(screen.queryByRole("columnheader", { name: "Type" })).not.toBeInTheDocument();
    expect(screen.getByLabelText("Applied explorer filters")).toHaveTextContent("Income");
    expect(screen.getByRole("textbox", { name: "Search Ledger Explorer" })).toHaveValue("aapl");
  });

  it("restores and shares saved explorer views from URL state", () => {
    window.history.replaceState(
      null,
      "",
      "/accounting?frexExplorer=ledger&frexView=operator-income-symbols&frexSearch=aapl&frexRecord=ledger:run-1:revenue"
    );
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
          columnIds: []
        },
        {
          viewId: "operator-income-symbols",
          label: "Income symbols",
          description: "Income accounts with symbol evidence.",
          isSystem: false,
          isActive: false,
          filters: [{ filterId: "accountType", label: "Account Type", value: "Income", operator: "equals", tone: "Info" }],
          searchText: "aapl",
          columnIds: ["accountName", "symbol"]
        }
      ]
    }));

    expect(screen.getByRole("button", { name: "Income symbols" })).toHaveAttribute("aria-current", "true");
    expect(screen.getByRole("textbox", { name: "Search Ledger Explorer" })).toHaveValue("aapl");
    expect(screen.getByRole("row", { name: /revenue aapl/i })).toBeInTheDocument();
    expect(screen.getByRole("dialog", { name: "Revenue proof detail" })).toBeInTheDocument();
    expect(screen.queryByRole("row", { name: /cash assets/i })).not.toBeInTheDocument();
    expect(screen.getByRole("link", {
      name: "Share Ledger Explorer evidence state: view Income symbols; search aapl; filter Account Type equals Income; record Revenue"
    })).toHaveAttribute(
      "href",
      "/accounting?frexExplorer=ledger&frexView=operator-income-symbols&frexSearch=aapl&frexFilter=accountType%3AIncome&frexRecord=ledger%3Arun-1%3Arevenue"
    );
  });

  it("restores explicit filter state from share links without relying on a saved view", () => {
    window.history.replaceState(
      null,
      "",
      "/accounting?frexExplorer=ledger&frexFilter=accountType:Income&frexRecord=ledger:run-1:revenue"
    );
    renderExplorer();

    expect(screen.getByLabelText("Applied explorer filters")).toHaveTextContent("Income");
    expect(screen.getByRole("row", { name: /revenue income aapl/i })).toBeInTheDocument();
    expect(screen.queryByRole("row", { name: /cash assets/i })).not.toBeInTheDocument();
    expect(screen.getByRole("dialog", { name: "Revenue proof detail" })).toBeInTheDocument();
    expect(screen.getByRole("link", {
      name: "Share Ledger Explorer evidence state: filter Type equals Income; record Revenue"
    })).toHaveAttribute(
      "href",
      "/accounting?frexExplorer=ledger&frexFilter=accountType%3AIncome&frexRecord=ledger%3Arun-1%3Arevenue"
    );
  });

  it("saves URL-restored filters into operator-named views", async () => {
    const user = userEvent.setup();
    const onSaveView = vi.fn().mockResolvedValue(undefined);
    window.history.replaceState(
      null,
      "",
      "/accounting?frexExplorer=ledger&frexFilter=accountType:Income&frexRecord=ledger:run-1:revenue"
    );
    renderExplorer(onSaveView);

    await waitFor(() => expect(screen.getByRole("button", { name: "Close drawer" })).toHaveFocus());
    await user.type(screen.getByRole("textbox", { name: "Saved view name" }), "Income evidence review");
    await user.click(screen.getByRole("button", { name: "Save view" }));

    expect(onSaveView).toHaveBeenCalledWith(expect.objectContaining({
      label: "Income evidence review",
      description: "Saved from shared explorer link.",
      filters: [
        expect.objectContaining({
          filterId: "accountType",
          label: "Type",
          value: "Income"
        })
      ]
    }));
  });

  it("keeps explorer evidence state out of the browser URL until it is explicitly shared", async () => {
    const user = userEvent.setup();
    window.history.replaceState(null, "", "/accounting?period=2026-06");
    renderExplorer();

    expect(window.location.pathname).toBe("/accounting");
    expect(window.location.search).toBe("?period=2026-06");

    await user.click(screen.getByRole("row", { name: /revenue income aapl/i }));
    await user.click(screen.getByRole("button", { name: "Close drawer" }));
    expect(screen.queryByRole("dialog", { name: "Revenue proof detail" })).not.toBeInTheDocument();
    await user.type(screen.getByRole("textbox", { name: "Search Ledger Explorer" }), "aapl");

    expect(window.location.pathname).toBe("/accounting");
    expect(window.location.search).toBe("?period=2026-06");
    expect(screen.getByRole("link", { name: /share ledger explorer evidence state/i })).toHaveAttribute(
      "href",
      "/accounting?period=2026-06&frexExplorer=ledger&frexView=system-ledger-default&frexSearch=aapl&frexRecord=ledger%3Arun-1%3Arevenue"
    );
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

    const detail = screen.getByRole("dialog", { name: "Apple Inc. proof detail" });
    const passport = within(detail).getByRole("region", { name: "Apple Inc. Number Passport" });
    expect(within(passport).getByText("Report Usage")).toBeInTheDocument();
    expect(within(passport).getByText("/api/workstation/financial-record-explorers/report-line-provenance?lineKey=holdings.aapl.market-value&sourceId=AAPL")).toBeInTheDocument();
    expect(within(passport).getByText("/api/workstation/security-master/securities/11111111-1111-1111-1111-111111111111/passport")).toBeInTheDocument();
    expect(within(detail).getByText("Instrument Identity")).toBeInTheDocument();
    expect(within(detail).getByText("Provider Evidence")).toBeInTheDocument();
    expect(within(detail).getByText("AssetOperations Readiness")).toBeInTheDocument();
    expect(within(detail).getByText("Ledger Impact")).toBeInTheDocument();
    expect(within(detail).getAllByText("Report Usage").length).toBeGreaterThanOrEqual(1);
    expect(within(detail).getByRole("link", { name: "Open instrument passport" })).toHaveAttribute("href", "/api/workstation/security-master/securities/11111111-1111-1111-1111-111111111111/passport");
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

    expect(screen.getAllByRole("status").some((status) => status.textContent === "Strategy run read service is not registered.")).toBe(true);
    expect(screen.getByRole("button", { name: "Source unavailable" })).toBeDisabled();
  });

  it("renders the shared Security & Instrument Explorer parity DTO", async () => {
    const user = userEvent.setup();
    renderExplorer(undefined, loadSecurityInstrumentParityFixture());

    expect(screen.getByRole("heading", { name: "Security & Instrument Explorer" })).toBeInTheDocument();
    expect(screen.getByLabelText("Explorer summary")).toHaveTextContent("Provider Evidence");
    expect(screen.getByRole("cell", { name: "AAPL - Apple Inc." })).toBeInTheDocument();
    expect(screen.getByRole("cell", { name: "96% trusted" })).toBeInTheDocument();
    expect(screen.getByRole("cell", { name: "Ready" })).toBeInTheDocument();
    expect(screen.getByRole("cell", { name: "1 projection" })).toBeInTheDocument();
    expect(screen.getByRole("cell", { name: "Board pack holdings.aapl.market-value" })).toBeInTheDocument();

    await user.click(screen.getByRole("row", { name: /aapl - apple inc/i }));
    const detail = screen.getByRole("dialog", { name: "AAPL - Apple Inc. proof detail" });
    expect(within(detail).getByText("Instrument Identity")).toBeInTheDocument();
    expect(within(detail).getByText("AAPL / US0378331005")).toBeInTheDocument();
    expect(within(detail).getByText("Provider Evidence")).toBeInTheDocument();
    expect(within(detail).getByText("AssetOperations Readiness")).toBeInTheDocument();
    expect(within(detail).getByText("Ledger Impact")).toBeInTheDocument();
    expect(within(detail).getAllByText("Report Usage").length).toBeGreaterThanOrEqual(1);
    expect(within(detail).getByText("Portfolio position")).toBeInTheDocument();
    expect(within(detail).getByText("AssetOperations reconciliation")).toBeInTheDocument();
    expect(within(detail).getAllByText("Report usage")).toHaveLength(2);
    expect(screen.getByLabelText("Record graph")).toHaveTextContent("AssetOperations ready");
  });
});

function renderExplorer(
  onSaveView?: Parameters<typeof FinancialRecordExplorerShell>[0]["onSaveView"],
  explorer: FinancialRecordExplorerDto = createExplorerDto(),
  onSelectRecord?: Parameters<typeof FinancialRecordExplorerShell>[0]["onSelectRecord"]
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
      onSelectRecord={onSelectRecord}
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
