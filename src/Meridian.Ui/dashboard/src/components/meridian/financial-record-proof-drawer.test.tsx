import { render, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { FinancialRecordProofDrawer } from "@/components/meridian/financial-record-proof-drawer";
import type {
  FinancialRecordExplorerDto,
  FinancialRecordExplorerRowDto,
  FinancialRecordExplorerSelectedRecordDto
} from "@/types";

describe("FinancialRecordProofDrawer", () => {
  it("renders blocked or empty proof guidance without inventing selected proof", () => {
    render(<FinancialRecordProofDrawer record={null} row={null} explorer={null} blockedReason="Ledger projection is unavailable." />);

    expect(screen.getByText("Ledger projection is unavailable.")).toBeInTheDocument();
    expect(screen.queryByLabelText(/Number Passport/)).not.toBeInTheDocument();
  });

  it("renders selected-record proof details through the shared drawer pattern", () => {
    render(<FinancialRecordProofDrawer record={record} row={row} explorer={explorer} blockedReason="" />);

    const drawer = screen.getByLabelText("Close Score proof detail");
    expect(within(drawer).getByText("Close score")).toBeInTheDocument();
    expect(within(drawer).getByLabelText("Close Score Number Passport")).toBeInTheDocument();
    expect(within(drawer).getByRole("button", { name: "Approval pending" })).toBeDisabled();
    expect(within(drawer).getByText("Score")).toBeInTheDocument();
    expect(within(drawer).getAllByText("Close package")).toHaveLength(2);
    expect(within(drawer).getAllByText("Report delivery")).toHaveLength(2);
    const fullRecordLinks = within(drawer).getAllByRole("link", { name: "Full record" });
    expect(fullRecordLinks).toHaveLength(2);
    expect(fullRecordLinks[1]).toHaveAttribute("href", "/api/workstation/close/score/full");
  });
});

const record: FinancialRecordExplorerSelectedRecordDto = {
  recordId: "close-score:fund-a",
  recordType: "Close score",
  title: "Close Score",
  subtitle: "Fund A close",
  description: "Close support score retained from the shared close package.",
  tone: "Warning",
  fields: [
    { label: "Score", value: "82%", detail: "2 blockers remain.", tone: "Warning" },
    { label: "Reconciliation", value: "Matched", detail: "Cash and portfolio tie out.", tone: "Success" }
  ],
  proofActions: [
    {
      actionId: "approval-pending",
      label: "Approval pending",
      description: "Controller approval is still pending.",
      href: "",
      isEnabled: false,
      disabledReason: "Controller approval is still pending.",
      tone: "Warning"
    }
  ],
  usedIn: [
    {
      relationshipId: "close-package",
      label: "Close package",
      description: "Close package uses this score.",
      href: "/api/workstation/close/packages/fund-a",
      tone: "Info"
    }
  ],
  impacts: [
    {
      relationshipId: "report-delivery",
      label: "Report delivery",
      description: "Investor statement release waits for this score.",
      href: "/api/workstation/reporting/delivery/fund-a",
      tone: "Warning"
    }
  ],
  fullRecordHref: "/api/workstation/close/score/full"
};

const row: FinancialRecordExplorerRowDto = {
  recordId: record.recordId,
  recordType: record.recordType,
  label: record.title,
  source: "Close package",
  status: "Review",
  tone: "Warning",
  cells: [
    { columnId: "score", displayValue: "82%", rawValue: "82", tone: "Warning", linkHref: "" }
  ],
  detail: record
};

const explorer: FinancialRecordExplorerDto = {
  explorerId: "ledger",
  title: "Ledger Explorer",
  description: "Close proof fixture.",
  sourceState: "Close package retained.",
  isBlocked: false,
  blockedReason: "",
  scopeItems: [],
  savedViews: [],
  summaryItems: [],
  filters: [],
  columns: [],
  rows: [row],
  selectedRecord: record,
  proofActions: [],
  recordGraph: { nodes: [], edges: [] }
};
