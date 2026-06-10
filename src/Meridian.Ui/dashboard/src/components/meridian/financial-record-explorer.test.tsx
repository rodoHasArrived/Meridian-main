import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { FinancialRecordExplorerShell } from "@/components/meridian/financial-record-explorer";
import type { FinancialRecordExplorerDto } from "@/types";

describe("FinancialRecordExplorerShell", () => {
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

  it("enables save view only after a material filter change", async () => {
    const user = userEvent.setup();
    const onSaveView = vi.fn().mockResolvedValue(undefined);
    renderExplorer(onSaveView);

    const saveButton = screen.getByRole("button", { name: "Save view" });
    expect(saveButton).toBeDisabled();

    await user.type(screen.getByRole("textbox", { name: "Search Ledger Explorer" }), "cash");
    expect(saveButton).toBeEnabled();

    await user.click(saveButton);
    expect(onSaveView).toHaveBeenCalledWith(expect.objectContaining({ searchText: "cash" }));
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

function createExplorerDto(): FinancialRecordExplorerDto {
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
          { columnId: "accountName", displayValue: "Cash", rawValue: "Cash", tone: "Success", linkHref: "" },
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
    }
  };
}
