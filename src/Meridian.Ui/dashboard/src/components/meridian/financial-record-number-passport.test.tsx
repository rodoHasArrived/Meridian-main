import { render, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { FinancialRecordNumberPassport } from "@/components/meridian/financial-record-number-passport";
import type {
  FinancialRecordExplorerDto,
  FinancialRecordExplorerRowDto,
  FinancialRecordExplorerSelectedRecordDto
} from "@/types";

describe("FinancialRecordNumberPassport", () => {
  it("renders the standard proof facets from a selected explorer record", () => {
    render(<FinancialRecordNumberPassport record={selectedRecord} row={selectedRow} explorer={explorer} />);

    const passport = screen.getByLabelText("Statement NAV Number Passport");
    expect(within(passport).getByRole("heading", { name: "Number Passport" })).toBeInTheDocument();
    expect(within(passport).getByText(/Number Passport for Statement NAV:/)).toBeInTheDocument();

    for (const label of ["Source", "Freshness", "Reconciliation", "Approvals", "Report Usage", "Blockers", "Evidence Packet", "Audit Trail"]) {
      expect(within(passport).getByText(label)).toBeInTheDocument();
    }

    expect(within(passport).getByRole("link", { name: "Statement system" })).toHaveAttribute("href", "/api/workstation/statements/nav/source");
    expect(within(passport).getByRole("link", { name: "Reviewed" })).toHaveAttribute("href", "/api/workstation/approvals/nav");
    expect(within(passport).getByText("None disclosed")).toBeInTheDocument();
    expect(within(passport).getByText("2 graph nodes")).toBeInTheDocument();
  });
});

const selectedRecord: FinancialRecordExplorerSelectedRecordDto = {
  recordId: "report-line:statement-nav",
  recordType: "Report line",
  title: "Statement NAV",
  subtitle: "Investor statement",
  description: "Statement NAV retained from the report-line provenance chain.",
  tone: "Success",
  fields: [
    { label: "Freshness", value: "Current", detail: "Updated 2026-06-26.", tone: "Success" },
    { label: "Reconciliation", value: "Matched", detail: "Tied to ledger close.", tone: "Success" },
    { label: "Approvals", value: "Reviewed", detail: "/api/workstation/approvals/nav", tone: "Success" },
    { label: "Report Usage", value: "Investor statement", detail: "/api/workstation/reports/statement-nav", tone: "Info" }
  ],
  proofActions: [
    {
      actionId: "open-evidence-packet",
      label: "Open evidence packet",
      description: "Open retained evidence packet.",
      href: "/api/workstation/evidence/statement-nav",
      isEnabled: true,
      disabledReason: "",
      tone: "Info"
    }
  ],
  usedIn: [],
  impacts: [],
  fullRecordHref: "/api/workstation/reports/statement-nav/full-record"
};

const selectedRow: FinancialRecordExplorerRowDto = {
  recordId: selectedRecord.recordId,
  recordType: selectedRecord.recordType,
  label: selectedRecord.title,
  source: "Statement system",
  status: "Ready",
  tone: "Success",
  cells: [
    {
      columnId: "source",
      displayValue: "Statement system",
      rawValue: "Statement system",
      tone: "Success",
      linkHref: "/api/workstation/statements/nav/source"
    }
  ],
  detail: selectedRecord
};

const explorer: FinancialRecordExplorerDto = {
  explorerId: "report-line-provenance",
  title: "Report-Line Provenance Explorer",
  description: "Retained report-line evidence.",
  sourceState: "Source-backed report-line chain.",
  isBlocked: false,
  blockedReason: "",
  scopeItems: [],
  savedViews: [],
  summaryItems: [],
  filters: [],
  columns: [],
  rows: [selectedRow],
  selectedRecord,
  proofActions: [],
  recordGraph: {
    nodes: [
      { nodeId: "ledger", label: "Ledger close", nodeType: "ledger", href: "", tone: "Success" },
      { nodeId: "statement", label: "Statement NAV", nodeType: "report", href: "/api/workstation/reports/statement-nav", tone: "Info" }
    ],
    edges: [
      { sourceNodeId: "ledger", targetNodeId: "statement", label: "supports", tone: "Info" }
    ]
  }
};
