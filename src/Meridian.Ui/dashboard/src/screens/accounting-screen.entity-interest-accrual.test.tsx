import { fireEvent, screen, waitFor } from "@testing-library/react";
import { axe } from "jest-axe";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { renderWithRouter } from "@/test/render";
import { postEntityOperation } from "@/lib/api/entity-operations.api";
import type { LedgerBook } from "@/types";
import type { EntityOperationScope, IntercompanyAccountingArrangement, IntercompanyAccrualProposal } from "@/types/entity-operations";
import { IntercompanyInterestAccrual } from "./accounting-screen.entity-interest-accrual";

vi.mock("@/lib/api/entity-operations.api", async () => ({
  ...await vi.importActual<typeof import("@/lib/api/entity-operations.api")>("@/lib/api/entity-operations.api"),
  getEntityOperation: vi.fn(), postEntityOperation: vi.fn()
}));

// Synthetic fixtures: a lender book, one approved loan-interest profile and the loan principal profile of the same agreement.
const book: LedgerBook = { ledgerBookId: "book-a", fundProfileId: "group", fundStructureNodeId: "entity-a", fundStructureNodeKind: "Entity",
  displayName: "Entity A primary", baseCurrency: "USD", createdAt: "2026-01-01", updatedAt: "2026-01-01", accountingBasis: "Primary", accountingPolicyId: "operating", accountingPolicyVersion: "v1" };
const scope: EntityOperationScope = { book, books: [book], graph: null,
  capabilities: { canManageAgreements: true, canManageLedger: true, canManageLifecycle: true, canViewLedger: true } };
function profile(arrangementId: string, templateKind: "Loan" | "LoanInterest", canonicalAgreementId = "legal-loan"): IntercompanyAccountingArrangement {
  return { arrangementId, templateKind, canonicalAgreementId, arrangementReference: `${templateKind} profile`, status: "Approved",
    partyMappings: [{ entityId: "entity-a", ledgerBookId: "book-a", receivableAccountNodeId: "ar", payableAccountNodeId: "ap", settlementAccountNodeId: null, revenueAccountNodeId: "income", expenseAccountNodeId: "expense" }]
  } as unknown as IntercompanyAccountingArrangement;
}
const interest = profile("interest-a", "LoanInterest");
const principal = profile("principal-a", "Loan");
const otherLoan = profile("principal-other", "Loan", "another-agreement");
const digest = "d".repeat(64);
const readyProposal: IntercompanyAccrualProposal = {
  computation: { status: "Ready", ruleVersion: "intercompany-accrual-v1", agreementId: "legal-loan", currency: "USD", accrualFrom: "2026-06-01", accrualTo: "2026-07-01",
    amount: "4166.67", unroundedAmount: "4166.666666666666666666666667", kernelVersion: "day-count-v1", calendars: [{ calendarId: "WEEKENDS", contentHash: "calendar-hash" }],
    slices: [{ sequence: 1, from: "2026-06-01", to: "2026-07-01", accountingArrangementRevisionId: "mapping-revision-a", agreementVersion: 2, agreementPayloadHash: "legal-hash",
      schedulePeriodSequence: 1, principal: "1000000", annualRate: "0.05", dayCount: "Actual360", yearFraction: "0.0833333333333333333333333333", unroundedAmount: "4166.666666666666666666666667", principalEvidenceReference: "opening:1" }], diagnostics: [] },
  diagnostics: [], evidenceDigest: digest,
  evidence: { postingDate: "2026-06-30", transactionId: "loan-interest:20260601-20260701", functionalCurrency: "USD", functionalAmount: "4166.67", postingAgreementVersion: 2,
    postingArrangementRevisionId: "mapping-revision-a", principal: { openingPrincipal: "1000000", openingJournalCount: 1, movements: [] } },
  draft: { journalEntryId: "accrual-draft-a", status: "Draft", ledgerBookId: "book-a", accountingDate: "2026-06-30", version: 0,
    lines: [{ lineId: "line-1", side: "Debit", amount: 4166.67, currency: "USD", accountPath: "Assets:Interest receivable" }, { lineId: "line-2", side: "Credit", amount: 4166.67, currency: "USD", accountPath: "Income:Interest" }]
  } as unknown as IntercompanyAccrualProposal["draft"]
};
const selection = { sourceBookId: "book-a", interestArrangementId: "interest-a", principalArrangementId: "principal-a", accrualFrom: "2026-06-01", accrualTo: "2026-07-01" };

function enterWindow(from = "2026-06-01", to = "2026-07-01") {
  fireEvent.change(screen.getByLabelText("First accrued day"), { target: { value: from } });
  fireEvent.change(screen.getByLabelText(/Accrue up to/), { target: { value: to } });
}

beforeEach(() => { vi.clearAllMocks(); });

describe("Intercompany interest accrual", () => {
  it("previews from the operator's selection only, then drafts exactly the previewed evidence digest", async () => {
    vi.mocked(postEntityOperation).mockImplementation(async (path) => path.endsWith("/preview") ? readyProposal : { ...readyProposal.draft!, version: 1 });
    const { container } = renderWithRouter(<IntercompanyInterestAccrual scope={scope} arrangement={interest} arrangements={[interest, principal, otherLoan]} />);
    // Only a Loan profile of the same legal agreement can supply principal.
    expect(screen.getByLabelText("Loan principal profile").querySelectorAll("option")).toHaveLength(1);
    expect(screen.getByRole("button", { name: "Preview interest accrual" })).toBeDisabled();
    enterWindow();
    fireEvent.click(screen.getByRole("button", { name: "Preview interest accrual" }));
    await screen.findByText(/interest for 2026-06-01 up to 2026-07-01/);
    expect(postEntityOperation).toHaveBeenCalledWith("/api/ledger/intercompany/accruals/preview", selection, expect.objectContaining({ signal: expect.any(AbortSignal) }));
    expect(screen.getByText("4166.666666666666666666666667")).toBeVisible();
    expect(screen.getByText("Assets:Interest receivable")).toBeVisible();

    fireEvent.click(screen.getByRole("button", { name: "Create accrual draft for review" }));
    await screen.findByRole("link", { name: /Open accrual draft Draft · version 1/ });
    expect(postEntityOperation).toHaveBeenLastCalledWith("/api/ledger/intercompany/accruals/drafts",
      { accrual: selection, expectedEvidenceDigest: digest }, expect.objectContaining({ signal: expect.any(AbortSignal) }));
    expect(screen.getByText(/Nothing was approved or posted/)).toBeVisible();
    expect(screen.getByRole("button", { name: "Create accrual draft for review" })).toBeDisabled();
    expect((await axe(container)).violations).toEqual([]);
  });

  it("withdraws the preview when the window changes so a stale amount cannot be drafted", async () => {
    vi.mocked(postEntityOperation).mockResolvedValue(readyProposal);
    renderWithRouter(<IntercompanyInterestAccrual scope={scope} arrangement={interest} arrangements={[interest, principal]} />);
    enterWindow();
    fireEvent.click(screen.getByRole("button", { name: "Preview interest accrual" }));
    await screen.findByRole("button", { name: "Create accrual draft for review" });
    fireEvent.change(screen.getByLabelText(/Accrue up to/), { target: { value: "2026-06-16" } });
    await waitFor(() => expect(screen.queryByRole("button", { name: "Create accrual draft for review" })).not.toBeInTheDocument());
    expect(screen.queryByText(/interest for 2026-06-01/)).not.toBeInTheDocument();
    expect(postEntityOperation).toHaveBeenCalledTimes(1);
  });

  it("shows the posted accrued interest a compounding period adds, and Simple for a simple slice", async () => {
    const [slice] = readyProposal.computation.slices;
    vi.mocked(postEntityOperation).mockResolvedValue({ ...readyProposal, computation: { ...readyProposal.computation, ruleVersion: "intercompany-accrual-v3",
      slices: [{ ...slice, compoundedInterest: "4166.6700000000000000000000001" }] } });
    renderWithRouter(<IntercompanyInterestAccrual scope={scope} arrangement={interest} arrangements={[interest, principal]} />);
    enterWindow();
    fireEvent.click(screen.getByRole("button", { name: "Preview interest accrual" }));
    expect(await screen.findByText("4166.6700000000000000000000001")).toBeVisible();
    expect(screen.getByRole("columnheader", { name: "Compounded interest" })).toBeVisible();
  });

  it("shows the server's refusal and offers no draft when the accrual is unavailable", async () => {
    vi.mocked(postEntityOperation).mockResolvedValue({ computation: { ...readyProposal.computation, status: "Unavailable", amount: null, unroundedAmount: null, slices: [], diagnostics: [] },
      diagnostics: ["Exactly one open source period must contain the last accrued day; no date is moved automatically."] });
    renderWithRouter(<IntercompanyInterestAccrual scope={scope} arrangement={interest} arrangements={[interest, principal]} />);
    enterWindow();
    fireEvent.click(screen.getByRole("button", { name: "Preview interest accrual" }));
    expect(await screen.findByText(/Exactly one open source period/)).toBeVisible();
    expect(screen.getByText("Accrual unavailable. No amount is estimated.")).toBeVisible();
    expect(screen.queryByRole("button", { name: "Create accrual draft for review" })).not.toBeInTheDocument();
  });

  it("offers no accrual request without ledger management permission or a principal profile", async () => {
    const { unmount } = renderWithRouter(<IntercompanyInterestAccrual scope={{ ...scope, capabilities: { ...scope.capabilities, canManageLedger: false } }} arrangement={interest} arrangements={[interest, principal]} />);
    expect(screen.getByText(/Ledger management permission is required to preview or draft/)).toBeVisible();
    expect(screen.queryByRole("button", { name: "Preview interest accrual" })).not.toBeInTheDocument();
    unmount();
    renderWithRouter(<IntercompanyInterestAccrual scope={scope} arrangement={interest} arrangements={[interest, otherLoan]} />);
    expect(screen.getByText(/No loan principal profile of the same legal agreement/)).toBeVisible();
    enterWindow();
    expect(screen.getByRole("button", { name: "Preview interest accrual" })).toBeDisabled();
    expect(postEntityOperation).not.toHaveBeenCalled();
  });
});
