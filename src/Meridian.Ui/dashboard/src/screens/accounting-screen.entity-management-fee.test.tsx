import { fireEvent, screen, waitFor } from "@testing-library/react";
import { axe } from "jest-axe";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { renderWithRouter } from "@/test/render";
import { postEntityOperation } from "@/lib/api/entity-operations.api";
import type { LedgerBook } from "@/types";
import type { EntityOperationScope, IntercompanyAccountingArrangement } from "@/types/entity-operations";
import type { IntercompanyFeeAccrualProposal } from "@/types/intercompany-fees";
import { IntercompanyManagementFeeAccrual } from "./accounting-screen.entity-management-fee";

vi.mock("@/lib/api/entity-operations.api", async () => ({
  ...await vi.importActual<typeof import("@/lib/api/entity-operations.api")>("@/lib/api/entity-operations.api"),
  getEntityOperation: vi.fn(), postEntityOperation: vi.fn()
}));

// Synthetic fixtures: a service-provider book and one approved management-fee profile mapping it.
const book: LedgerBook = { ledgerBookId: "book-a", fundProfileId: "group", fundStructureNodeId: "entity-a", fundStructureNodeKind: "Entity",
  displayName: "Entity A primary", baseCurrency: "USD", createdAt: "2026-01-01", updatedAt: "2026-01-01", accountingBasis: "Primary", accountingPolicyId: "operating", accountingPolicyVersion: "v1" };
const scope: EntityOperationScope = { book, books: [book], graph: null,
  capabilities: { canManageAgreements: true, canManageLedger: true, canManageLifecycle: true, canViewLedger: true } };
function profile(ledgerBookId = "book-a"): IntercompanyAccountingArrangement {
  return { arrangementId: "fee-a", templateKind: "ManagementFee", canonicalAgreementId: "legal-fee", arrangementReference: "Management fee profile", status: "Approved",
    partyMappings: [{ entityId: "entity-a", ledgerBookId, receivableAccountNodeId: "ar", payableAccountNodeId: "ap", settlementAccountNodeId: null, revenueAccountNodeId: "income", expenseAccountNodeId: "expense" }]
  } as unknown as IntercompanyAccountingArrangement;
}
const fee = profile();
const digest = "f".repeat(64);
const computation: IntercompanyFeeAccrualProposal["computation"] = { status: "Ready", ruleVersion: "intercompany-fee-v1", agreementId: "legal-fee", currency: "USD",
  accrualFrom: "2026-06-01", accrualTo: "2026-06-16", amount: "5000.00", unroundedAmount: "5000.000000000000000000000000", kernelVersion: "schedule-v1",
  calendars: [{ calendarId: "WEEKENDS", contentHash: "calendar-hash" }],
  slices: [{ sequence: 1, from: "2026-06-01", to: "2026-06-16", accountingArrangementRevisionId: "mapping-revision-a", agreementVersion: 3, agreementPayloadHash: "legal-hash",
    schedulePeriodSequence: 6, periodStart: "2026-06-01", periodEnd: "2026-07-01", basis: "PerPeriod", declaredFee: "10000.00", dayCount: null,
    fraction: "0.5", unroundedAmount: "5000.000000000000000000000000" }], diagnostics: [] };
const readyProposal: IntercompanyFeeAccrualProposal = {
  computation, diagnostics: [], evidenceDigest: digest,
  evidence: { ruleVersion: "intercompany-fee-v1", draftId: "fee-draft-a", sourceBookId: "book-a", sourceRole: "Receivable", transactionId: "management-fee:20260601-20260616",
    postingDate: "2026-06-15", functionalCurrency: "USD", functionalAmount: "5000.00", postingAgreementVersion: 3, postingArrangementRevisionId: "mapping-revision-a", computation },
  draft: { journalEntryId: "fee-draft-a", status: "Draft", ledgerBookId: "book-a", accountingDate: "2026-06-15", version: 0,
    lines: [{ lineId: "line-1", side: "Debit", amount: 5000, currency: "USD", accountPath: "Assets:Fee receivable" }, { lineId: "line-2", side: "Credit", amount: 5000, currency: "USD", accountPath: "Income:Management fees" }]
  } as unknown as IntercompanyFeeAccrualProposal["draft"]
};
const selection = { sourceBookId: "book-a", feeArrangementId: "fee-a", accrualFrom: "2026-06-01", accrualTo: "2026-06-16" };

function enterWindow(from = "2026-06-01", to = "2026-06-16") {
  fireEvent.change(screen.getByLabelText("First accrued day"), { target: { value: from } });
  fireEvent.change(screen.getByLabelText(/Accrue up to/), { target: { value: to } });
}

beforeEach(() => { vi.clearAllMocks(); });

describe("Intercompany management fee accrual", () => {
  it("previews from the window only, shows the server's slices and direction, then drafts exactly the previewed digest", async () => {
    vi.mocked(postEntityOperation).mockImplementation(async (path) => path.endsWith("/preview") ? readyProposal : { ...readyProposal.draft!, version: 1 });
    const { container } = renderWithRouter(<IntercompanyManagementFeeAccrual scope={scope} arrangement={fee} />);
    expect(screen.getByText(/Percentage fees are not calculated/)).toBeVisible();
    expect(screen.getByRole("button", { name: "Preview management fee" })).toBeDisabled();
    enterWindow();
    fireEvent.click(screen.getByRole("button", { name: "Preview management fee" }));
    await screen.findByText(/management fee for 2026-06-01 up to 2026-06-16/);
    expect(postEntityOperation).toHaveBeenCalledWith("/api/ledger/intercompany/fees/preview", selection, expect.objectContaining({ signal: expect.any(AbortSignal) }));
    expect(screen.getByText(/rule intercompany-fee-v1/)).toBeVisible();
    expect(screen.getByText(/Service provider side/)).toBeVisible();
    // Exact server decimals are rendered as text, never re-parsed.
    expect(screen.getAllByText("5000.000000000000000000000000")[0]).toBeVisible();
    expect(screen.getByText("10000.00")).toBeVisible();
    expect(screen.getByText("0.5")).toBeVisible();
    expect(screen.getByText("Each schedule period")).toBeVisible();
    expect(screen.getByText("Period 6 · 2026-06-01 up to 2026-07-01")).toBeVisible();
    expect(screen.getByText("Assets:Fee receivable")).toBeVisible();

    fireEvent.click(screen.getByRole("button", { name: "Create fee draft for review" }));
    await screen.findByRole("link", { name: /Open fee draft Draft · version 1/ });
    expect(postEntityOperation).toHaveBeenLastCalledWith("/api/ledger/intercompany/fees/drafts",
      { accrual: selection, expectedEvidenceDigest: digest }, expect.objectContaining({ signal: expect.any(AbortSignal) }));
    expect(screen.getByText(/Nothing was approved or posted/)).toBeVisible();
    expect(screen.getByRole("button", { name: "Create fee draft for review" })).toBeDisabled();
    expect((await axe(container)).violations).toEqual([]);
  });

  it("shows the recipient direction and the per-annum day count", async () => {
    const perAnnum = { ...computation, slices: [{ ...computation.slices[0], basis: "PerAnnum" as const, declaredFee: "120000", dayCount: "Actual365", fraction: "0.0410958904109589041095890411" }] };
    vi.mocked(postEntityOperation).mockResolvedValue({ ...readyProposal, computation: perAnnum, evidence: { ...readyProposal.evidence!, sourceRole: "Payable", computation: perAnnum } });
    renderWithRouter(<IntercompanyManagementFeeAccrual scope={scope} arrangement={fee} />);
    enterWindow();
    fireEvent.click(screen.getByRole("button", { name: "Preview management fee" }));
    expect(await screen.findByText(/Recipient side/)).toBeVisible();
    expect(screen.getByText("One year (per annum) · Actual365")).toBeVisible();
    expect(screen.getByText("0.0410958904109589041095890411")).toBeVisible();
  });

  it("withdraws the preview when the window changes so a stale amount cannot be drafted", async () => {
    vi.mocked(postEntityOperation).mockResolvedValue(readyProposal);
    renderWithRouter(<IntercompanyManagementFeeAccrual scope={scope} arrangement={fee} />);
    enterWindow();
    fireEvent.click(screen.getByRole("button", { name: "Preview management fee" }));
    await screen.findByRole("button", { name: "Create fee draft for review" });
    fireEvent.change(screen.getByLabelText(/Accrue up to/), { target: { value: "2026-06-20" } });
    await waitFor(() => expect(screen.queryByRole("button", { name: "Create fee draft for review" })).not.toBeInTheDocument());
    expect(screen.queryByText(/management fee for 2026-06-01/)).not.toBeInTheDocument();
    expect(postEntityOperation).toHaveBeenCalledTimes(1);
  });

  it("shows the server's refusal and offers no draft when the fee is unavailable", async () => {
    vi.mocked(postEntityOperation).mockResolvedValue({ computation: { ...computation, status: "Unavailable", amount: null, unroundedAmount: null, slices: [], diagnostics: [] },
      diagnostics: ["The approved terms declare no fixed fee basis; no fee is calculated."] });
    renderWithRouter(<IntercompanyManagementFeeAccrual scope={scope} arrangement={fee} />);
    enterWindow();
    fireEvent.click(screen.getByRole("button", { name: "Preview management fee" }));
    expect(await screen.findByText(/declare no fixed fee basis/)).toBeVisible();
    expect(screen.getByText("Fee unavailable. No amount is estimated.")).toBeVisible();
    expect(screen.queryByRole("button", { name: "Create fee draft for review" })).not.toBeInTheDocument();
  });

  it("offers no fee request without ledger management permission and notes an unmapped book", () => {
    const { unmount } = renderWithRouter(<IntercompanyManagementFeeAccrual scope={{ ...scope, capabilities: { ...scope.capabilities, canManageLedger: false } }} arrangement={fee} />);
    expect(screen.getByText(/Ledger management permission is required to preview or draft a management fee/)).toBeVisible();
    expect(screen.queryByRole("button", { name: "Preview management fee" })).not.toBeInTheDocument();
    unmount();
    renderWithRouter(<IntercompanyManagementFeeAccrual scope={scope} arrangement={profile("book-other")} />);
    expect(screen.getByText(/This profile does not map Entity A primary/)).toBeVisible();
    expect(postEntityOperation).not.toHaveBeenCalled();
  });
});
