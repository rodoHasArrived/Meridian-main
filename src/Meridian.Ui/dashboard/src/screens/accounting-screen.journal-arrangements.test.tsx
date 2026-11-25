import { fireEvent, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { renderWithRouter } from "@/test/render";
import { getEntityOperation } from "@/lib/api/entity-operations.api";
import { listIntercompanyAgreements } from "@/lib/api/intercompany-agreements.api";
import type { LedgerDimensionSet, ManualJournalEntryLine } from "@/types";
import type { AccountingArrangementRecord, IntercompanyAccountBinding, IntercompanyAccountingArrangement } from "@/types/entity-operations";
import type { ManualJournalEntryWorkbenchViewModel } from "./accounting-screen.view-model";
import { ManualJournalIntercompanyDimensions } from "./accounting-screen.journal-intercompany-dimensions";
import { ManualJournalDimensionsInspector } from "./accounting-screen.journal-entry-enhancements";

vi.mock("@/lib/api/entity-operations.api", async () => ({
  ...await vi.importActual<typeof import("@/lib/api/entity-operations.api")>("@/lib/api/entity-operations.api"),
  getEntityOperation: vi.fn()
}));
vi.mock("@/lib/api/intercompany-agreements.api", () => ({ listIntercompanyAgreements: vi.fn() }));

const legalId = "11111111-1111-1111-1111-111111111111";
const entityId = "22222222-2222-2222-2222-222222222222";
const counterpartyId = "33333333-3333-3333-3333-333333333333";
const arrangementId = "44444444-4444-4444-4444-444444444444";
const revisionId = "55555555-5555-5555-5555-555555555555";
const binding: IntercompanyAccountBinding = { bindingId: "binding", revisionId: "binding-revision", revisionNumber: 2,
  ledgerBookId: "book-a", chartAccountNodeId: "ar", role: "Receivable", accountPath: "Assets:Group Receivable", accountName: "Group Receivable", accountType: "Asset",
  effectiveFrom: "2026-01-01", effectiveTo: null, historicalNames: [], recordedBy: "reviewer", recordedAt: "2026-01-01", reason: "Retained account identity", isArchived: false };
const record: AccountingArrangementRecord = { arrangementId, organizationId: "org-a", arrangementReference: "Shared services profile", templateKind: "CostRecharge",
  canonicalAgreementId: legalId, canonicalAgreementVersion: 3, canonicalPayloadHash: "approved-legal-payload", effectiveFrom: "2026-01-01", effectiveTo: null,
  revisionId, revisionNumber: 2, version: 4, status: "Approved", partyMappings: [{ entityId, ledgerBookId: "book-a", receivableAccountNodeId: "ar", payableAccountNodeId: "ap", settlementAccountNodeId: null, revenueAccountNodeId: null, expenseAccountNodeId: null }],
  createdBy: "preparer", createdAt: "2026-01-01", updatedBy: "reviewer", updatedAt: "2026-01-02", approvedBy: "reviewer", approvedAt: "2026-01-02", retiredAt: null, predecessorRevisionId: null };
const arrangement: IntercompanyAccountingArrangement = { ...record, arrangement: record,
  legalRevision: { agreementId: legalId, agreementVersion: 3, action: "Activate", proposalId: "legal-proposal", payloadHash: "approved-legal-payload", authors: ["legal-preparer"], submitter: "legal-preparer", approver: "legal-reviewer", approvedAtUtc: "2026-01-01", rationale: "Reviewed legal terms",
    terms: { code: "LEGAL-1", name: "Shared services agreement", partyOneId: entityId, partyOneRole: "Provider", partyTwoId: counterpartyId, partyTwoRole: "Recipient", agreementType: "CostShare", currency: "USD", effectiveFrom: "2026-01-01", action: "Activate" } },
  partyOneEntityId: entityId, partyTwoEntityId: counterpartyId, partyOneRole: "Provider", partyTwoRole: "Recipient", currency: "USD", settlementCadence: "Monthly", settlementDueDays: 30, canonicalEvidence: [] };

function line(dimensions: LedgerDimensionSet = {}): ManualJournalEntryLine {
  return { lineId: "line-a", side: "Debit", amount: 100, currency: "USD", accountPath: binding.accountPath, entityId,
    dimensions: { instrumentId: "retained-instrument", strategyId: "retained-strategy", ...dimensions } };
}
function viewFor(selectedLine: ManualJournalEntryLine, status = "Draft") {
  const updateLine = vi.fn();
  const view = { selectedLineId: selectedLine.lineId, accountOptions: [{ value: selectedLine.accountPath, label: "Group Receivable" }], securitySearchResults: [],
    draft: { journalEntryId: "journal-a", lines: [selectedLine], ledgerBookId: "book-a", accountingDate: "2026-11-12", currency: "USD", status, entityId, dimensions: {} }, updateLine } as unknown as ManualJournalEntryWorkbenchViewModel;
  return { view, updateLine };
}
function openPanel(container: HTMLElement) {
  const details = container.querySelector("details")!;
  details.open = true;
  fireEvent(details, new Event("toggle"));
}

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(getEntityOperation).mockImplementation(async (path) => path.includes("account-bindings") ? [binding, { ...binding, revisionId: "unmapped-binding", chartAccountNodeId: "unmapped", accountName: "Other receivable" }]
    : path.includes("intercompany-accounting-arrangements") ? [arrangement] : null);
  vi.mocked(listIntercompanyAgreements).mockResolvedValue({ items: [], referencePostingSupported: true });
});

describe("Manual journal accounting arrangements", () => {
  it("pins all seven authority identities together and preserves other attribution", async () => {
    const { view, updateLine } = viewFor(line());
    const { container } = renderWithRouter(<ManualJournalIntercompanyDimensions view={view} line={view.draft.lines[0]} />);
    openPanel(container);
    await screen.findByRole("option", { name: /Shared services profile.*revision 2/ });
    fireEvent.change(screen.getByLabelText("Line approved accounting arrangement"), { target: { value: arrangementId } });
    expect(updateLine).not.toHaveBeenCalled();
    expect(screen.queryByRole("option", { name: /Other receivable/ })).not.toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Shared intercompany transaction reference"), { target: { value: "recharge-123" } });
    fireEvent.click(screen.getByRole("button", { name: "Apply accounting arrangement" }));
    expect(updateLine).toHaveBeenCalledOnce();
    expect(updateLine).toHaveBeenCalledWith("line-a", { accountPath: binding.accountPath, entityId,
      dimensions: { instrumentId: "retained-instrument", strategyId: "retained-strategy", entityId, counterpartyId,
        intercompanyAgreementId: legalId, intercompanyAgreementVersion: 3, accountingArrangementId: arrangementId,
        accountingArrangementRevisionId: revisionId, chartAccountNodeId: "ar", accountBindingRevisionId: "binding-revision", intercompanyTransactionId: "recharge-123" } });
    expect(getEntityOperation).toHaveBeenCalledWith(expect.stringContaining("intercompany-accounting-arrangements?bookId=book-a&asOf=2026-11-12"), expect.anything());
  });

  it("requires an explicit mapped role when the current account is not mapped", async () => {
    const selectedLine = { ...line(), accountPath: "Assets:Cash" };
    const { view, updateLine } = viewFor(selectedLine);
    const { container } = renderWithRouter(<ManualJournalIntercompanyDimensions view={view} line={selectedLine} />);
    openPanel(container);
    await screen.findByRole("option", { name: /Shared services profile/ });
    fireEvent.change(screen.getByLabelText("Line approved accounting arrangement"), { target: { value: arrangementId } });
    expect(screen.getByRole("button", { name: "Apply accounting arrangement" })).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Line intercompany account binding"), { target: { value: "binding-revision" } });
    fireEvent.click(screen.getByRole("button", { name: "Apply accounting arrangement" }));
    expect(updateLine).toHaveBeenCalledWith("line-a", expect.objectContaining({ accountPath: binding.accountPath }));
  });

  it("keeps retained legal and arrangement identities inspectable when lookup fails", async () => {
    vi.mocked(getEntityOperation).mockRejectedValue(new Error("Arrangement authority unavailable"));
    const selectedLine = line({ intercompanyAgreementId: legalId, intercompanyAgreementVersion: 3, accountingArrangementId: arrangementId, accountingArrangementRevisionId: revisionId });
    const { view, updateLine } = viewFor(selectedLine, "Approved");
    const { container } = renderWithRouter(<ManualJournalIntercompanyDimensions view={view} line={selectedLine} />);
    openPanel(container);
    expect(await screen.findAllByRole("alert")).not.toHaveLength(0);
    fireEvent.click(screen.getByText("Retained intercompany dimension identities"));
    expect(screen.getByText(legalId, { exact: true })).toBeVisible();
    expect(screen.getByText(revisionId, { exact: true })).toBeVisible();
    expect(screen.getByRole("button", { name: "Apply accounting arrangement" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Clear accounting arrangement" })).toBeDisabled();
    expect(updateLine).not.toHaveBeenCalled();
  });

  it("preserves native legal authoring beside profiles and clears a stale profile after legal amendment", async () => {
    const selectedLine = line({ counterpartyId, intercompanyAgreementId: legalId, intercompanyAgreementVersion: 3,
      accountingArrangementId: arrangementId, accountingArrangementRevisionId: revisionId, chartAccountNodeId: "ar", accountBindingRevisionId: "binding-revision", intercompanyTransactionId: "retained-transaction" });
    const { view, updateLine } = viewFor(selectedLine);
    renderWithRouter(<ManualJournalDimensionsInspector view={view} />);
    const version = await screen.findByLabelText("Line approved agreement version");
    expect(screen.getByText("Intercompany accounting arrangement and original currency")).toBeVisible();
    fireEvent.change(version, { target: { value: "4" } });
    fireEvent.click(screen.getByRole("button", { name: "Apply line agreement reference" }));
    expect(updateLine).toHaveBeenCalledWith("line-a", { dimensions: { ...selectedLine.dimensions,
      intercompanyAgreementVersion: 4, accountingArrangementId: null, accountingArrangementRevisionId: null } });
  });

  it("retains exact original currency text without converting the functional line", async () => {
    vi.mocked(getEntityOperation).mockImplementation(async (path) => path.includes("/fx/books/")
      ? { policy: { policyId: "approved-fx", revision: "v3" }, rateSetId: "rate-set", rateSetRevision: 2 }
      : []);
    const { view, updateLine } = viewFor(line());
    const { container } = renderWithRouter(<ManualJournalIntercompanyDimensions view={view} line={view.draft.lines[0]} />);
    openPanel(container);
    await screen.findByText(/Approved FX policy approved-fx/);
    fireEvent.change(screen.getByLabelText("Original transaction currency"), { target: { value: "KWD" } });
    fireEvent.change(screen.getByLabelText("Original transaction amount"), { target: { value: "100.001" } });
    fireEvent.change(screen.getByLabelText("Original rate to functional currency"), { target: { value: "3.24567890123456789" } });
    fireEvent.click(screen.getByRole("button", { name: "Apply original currency evidence" }));
    expect(updateLine).toHaveBeenCalledWith("line-a", { transactionCurrency: { currency: "KWD", amount: "100.001", fxRateToFunctional: "3.24567890123456789" } });
    expect(view.draft.lines[0].amount).toBe(100);
  });
});
