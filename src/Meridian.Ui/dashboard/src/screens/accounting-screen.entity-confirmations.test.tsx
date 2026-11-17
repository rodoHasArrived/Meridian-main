import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { axe } from "jest-axe";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { renderWithRouter } from "@/test/render";
import { getEntityOperation, postEntityOperation } from "@/lib/api/entity-operations.api";
import type { EntityOperationScope } from "@/types/entity-operations";
import type { IntercompanyBalanceConfirmation, IntercompanyConfirmationSummary } from "@/types/intercompany-confirmations";
import { EntityBalanceConfirmations } from "./accounting-screen.entity-confirmations";

vi.mock("@/lib/api/entity-operations.api", async () => ({ ...await vi.importActual<typeof import("@/lib/api/entity-operations.api")>("@/lib/api/entity-operations.api"), getEntityOperation: vi.fn(), postEntityOperation: vi.fn() }));

const book = { ledgerBookId: "lender-book", fundProfileId: "lender-fund", fundStructureNodeId: "lender", fundStructureNodeKind: "Entity", displayName: "Lender book", baseCurrency: "USD", createdAt: "2026-01-01", updatedAt: "2026-01-01", accountingBasis: "Primary" as const, accountingPolicyId: "primary", accountingPolicyVersion: "v1" };
const scope: EntityOperationScope = { book, books: [book], capabilities: { canManageAgreements: false, canManageLedger: true, canManageLifecycle: false, canViewLedger: true }, graph: null };
const side = (bookId: string, amount: string) => ({ entityId: bookId.replace("-book", ""), ledgerBookId: bookId, transactionCurrency: "USD", transactionSignedBalance: amount, functionalCurrency: "USD", functionalSignedBalance: amount, scheduleContentHash: "hash", journalEntryIds: ["journal-1"] });
const proposed: IntercompanyBalanceConfirmation = {
  confirmationId: "confirmation-1", tenantId: "tenant-a", companyId: "company-a", accountingArrangementId: "loan-profile", intercompanyAgreementId: "loan-agreement",
  asOf: "2026-06-30", version: 1, status: "Proposed", partyOne: side("lender-book", "12345678901234567.89"), partyTwo: side("borrower-book", "-12345678901234567.89"),
  netTransactionBalance: "0.00", balanceDigest: "digest-1",
  events: [{ eventId: "event-1", requestId: "request-1", action: "Propose", party: null, actor: "accounting-operations", recordedAt: "2026-07-02T09:00:00Z", balanceDigest: "digest-1", reason: null }]
};
const summary: IntercompanyConfirmationSummary = { ledgerBookId: "lender-book", asOf: "2026-06-30", confirmations: [proposed],
  findings: [{ code: "AwaitingConfirmation", message: "Neither side has confirmed the pinned balances.", accountingArrangementId: "loan-profile", confirmationId: "confirmation-1" }] };
const profiles = [{ arrangementId: "loan-profile", organizationId: "organization", arrangementReference: "Lender to borrower loan", templateKind: "Loan" }];

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(getEntityOperation).mockImplementation(async (path) => path.includes("/confirmations?") ? summary : path.includes("intercompany-accounting-arrangements") ? profiles : []);
});

async function load(container: EntityOperationScope = scope) {
  const view = renderWithRouter(<EntityBalanceConfirmations scope={container} />);
  fireEvent.change(screen.getByLabelText("Confirmation as of"), { target: { value: "2026-06-30" } });
  fireEvent.click(screen.getByRole("button", { name: "Load confirmations" }));
  await screen.findByText("1 advisory finding(s)");
  return view;
}

describe("Reciprocal balance confirmation", () => {
  it("shows both sides' exact posted balances and states that close eligibility is unchanged", async () => {
    const { container } = await load();
    const [path] = vi.mocked(getEntityOperation).mock.calls.find(([value]) => value.includes("/confirmations?"))!;
    expect(Object.fromEntries(new URL(path, "https://meridian.test").searchParams)).toEqual({ ledgerBookId: "lender-book", asOf: "2026-06-30" });
    expect(screen.getByText("12345678901234567.89 USD (this book)")).toBeVisible();
    expect(screen.getByText("-12345678901234567.89 USD")).toBeVisible();
    expect(screen.getByText(/advisory only; close eligibility is unchanged/)).toBeVisible();
    expect(screen.getByText("Neither side has confirmed the pinned balances.")).toBeVisible();
    expect(postEntityOperation).not.toHaveBeenCalled();
    expect((await axe(container)).violations).toEqual([]);
  });

  it("confirms only this book's side against the exact version and digest shown", async () => {
    vi.mocked(postEntityOperation).mockResolvedValue({ ...proposed, version: 2, status: "ConfirmedByPartyOne" });
    await load();
    fireEvent.click(screen.getByRole("button", { name: "Confirm this book's balance" }));
    await waitFor(() => expect(postEntityOperation).toHaveBeenCalledWith("/api/ledger/intercompany/confirmations/confirmation-1/actions",
      expect.objectContaining({ action: "Confirm", party: "PartyOne", expectedVersion: 1, expectedBalanceDigest: "digest-1", reason: null, requestId: expect.any(String) }), expect.any(Object)));
    expect(await screen.findByText("This book's side confirmed the pinned balances.")).toBeVisible();
  });

  it("requires a reason to dispute and sends it with the dispute", async () => {
    vi.mocked(postEntityOperation).mockResolvedValue({ ...proposed, version: 2, status: "Disputed" });
    await load();
    const dispute = screen.getByRole("button", { name: "Dispute" });
    expect(dispute).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Dispute or resolution reason"), { target: { value: "Borrower is short 1,000 of accrued interest." } });
    expect(dispute).toBeEnabled();
    fireEvent.click(dispute);
    await waitFor(() => expect(postEntityOperation).toHaveBeenCalledWith("/api/ledger/intercompany/confirmations/confirmation-1/actions",
      expect.objectContaining({ action: "Dispute", party: "PartyOne", reason: "Borrower is short 1,000 of accrued interest." }), expect.any(Object)));
  });

  it("pins the chosen profile's current balances without sending any amount", async () => {
    vi.mocked(postEntityOperation).mockResolvedValue(proposed);
    await load();
    fireEvent.click(await screen.findByRole("button", { name: "Pin current balances for confirmation" }));
    await waitFor(() => expect(postEntityOperation).toHaveBeenCalledWith("/api/ledger/intercompany/confirmations",
      { requestId: expect.any(String), accountingArrangementId: "loan-profile", asOf: "2026-06-30" }, expect.any(Object)));
  });

  it("offers no action to a read-only operator", async () => {
    await load({ ...scope, capabilities: { ...scope.capabilities, canManageLedger: false } });
    const table = screen.getByRole("region", { name: "Reciprocal balance confirmations" });
    expect(within(table).getByText("Read only")).toBeVisible();
    expect(screen.queryByRole("button", { name: "Pin current balances for confirmation" })).not.toBeInTheDocument();
    expect(vi.mocked(getEntityOperation).mock.calls.some(([path]) => path.includes("intercompany-accounting-arrangements"))).toBe(false);
  });
});
