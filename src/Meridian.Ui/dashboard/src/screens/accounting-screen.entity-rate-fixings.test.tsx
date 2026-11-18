import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { axe } from "jest-axe";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { renderWithRouter } from "@/test/render";
import { createApiErrorFromResponseBody } from "@/lib/api-errors";
import { getEntityOperation, postEntityOperation } from "@/lib/api/entity-operations.api";
import { formatTimestamp } from "@/lib/format";
import type { LedgerBook } from "@/types";
import type { EntityOperationScope } from "@/types/entity-operations";
import type { IntercompanyRateFixing } from "@/types/intercompany-rate-fixings";
import { EntityRateFixings } from "./accounting-screen.entity-rate-fixings";

vi.mock("@/lib/api/entity-operations.api", async () => ({
  ...await vi.importActual<typeof import("@/lib/api/entity-operations.api")>("@/lib/api/entity-operations.api"),
  getEntityOperation: vi.fn(), postEntityOperation: vi.fn()
}));

// Synthetic fixtures: one approved SOFR fixing and one date whose first value awaits a second person.
const book: LedgerBook = { ledgerBookId: "book-a", fundProfileId: "group", fundStructureNodeId: "entity-a", fundStructureNodeKind: "Entity",
  displayName: "Entity A primary", baseCurrency: "USD", createdAt: "2026-01-01", updatedAt: "2026-01-01", accountingBasis: "Primary", accountingPolicyId: "operating", accountingPolicyVersion: "v1" };
const scope: EntityOperationScope = { book, books: [book], graph: null,
  capabilities: { canManageAgreements: true, canManageLedger: true, canManageLifecycle: true, canViewLedger: true } };
const readOnly: EntityOperationScope = { ...scope, capabilities: { ...scope.capabilities, canManageLedger: false } };
const value = (rateFraction: string, proposedBy: string, digest: string) => ({ rateFraction, sourceReference: `publisher:${rateFraction}`,
  justification: "Published overnight rate", proposedBy, proposedAt: "2026-05-28T13:00:00Z", valueDigest: digest });
const approved: IntercompanyRateFixing = { fixingId: "fixing-approved", tenantId: "tenant-a", indexReference: "SOFR", fixingDate: "2026-05-28", version: 2,
  approved: value("0.04250000000000000000000000001", "operator-a", "a".repeat(64)), pending: null,
  events: [{ eventId: "event-1", requestId: "request-1", action: "Propose", actor: "operator-a", recordedAt: "2026-05-28T13:00:00Z", valueDigest: "a".repeat(64) },
    { eventId: "event-2", requestId: "request-2", action: "Approve", actor: "operator-b", recordedAt: "2026-05-28T14:00:00Z", valueDigest: "a".repeat(64) }] };
const pending: IntercompanyRateFixing = { fixingId: "fixing-pending", tenantId: "tenant-a", indexReference: "SOFR", fixingDate: "2026-05-29", version: 1,
  approved: null, pending: value("0.0431", "operator-a", "b".repeat(64)),
  events: [{ eventId: "event-3", requestId: "request-3", action: "Propose", actor: "operator-a", recordedAt: "2026-05-29T13:00:00Z", valueDigest: "b".repeat(64) }] };

function loadRange() {
  fireEvent.change(screen.getByLabelText("Fixing index reference"), { target: { value: "sofr" } });
  fireEvent.change(screen.getByLabelText("Fixing dates from"), { target: { value: "2026-05-01" } });
  fireEvent.change(screen.getByLabelText("Fixing dates to (inclusive)"), { target: { value: "2026-05-31" } });
  fireEvent.click(screen.getByRole("button", { name: "Load fixings" }));
}

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(getEntityOperation).mockResolvedValue([approved, pending]);
});

describe("Reviewed intercompany rate fixings", () => {
  it("lists exact approved and pending values for the requested range with history and no mutation for read-only operators", async () => {
    const { container } = renderWithRouter(<EntityRateFixings scope={readOnly} />);
    expect(screen.getByText("Read only")).toBeVisible();
    expect(screen.getByText(/Only approved fixings set floating intercompany accrual rates\. A missing fixing is never inferred.*nothing is fetched from a market feed/)).toBeVisible();
    loadRange();
    const table = await screen.findByRole("region", { name: "Retained rate fixings" });
    expect(getEntityOperation).toHaveBeenCalledWith("/api/ledger/intercompany/rate-fixings?indexReference=sofr&from=2026-05-01&to=2026-05-31", expect.any(Object));
    expect(within(table).getByText("0.04250000000000000000000000001")).toBeVisible();
    expect(within(table).getByText("Approved")).toBeVisible();
    expect(within(table).getByText("Pending review · not used for accrual")).toBeVisible();
    expect(within(table).getByText("History for SOFR 2026-05-28")).toBeVisible();
    expect(within(table).getByText(`Approve · operator-b · ${formatTimestamp("2026-05-28T14:00:00Z")}`)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Review pending value/ })).not.toBeInTheDocument();
    expect(screen.queryByRole("form", { name: "Propose a fixing value" })).not.toBeInTheDocument();
    expect(postEntityOperation).not.toHaveBeenCalled();
    expect((await axe(container)).violations).toEqual([]);
  });

  it("withdraws a loaded range when the request changes", async () => {
    renderWithRouter(<EntityRateFixings scope={readOnly} />);
    loadRange();
    await screen.findByRole("region", { name: "Retained rate fixings" });
    fireEvent.change(screen.getByLabelText("Fixing dates to (inclusive)"), { target: { value: "2026-04-30" } });
    expect(screen.queryByRole("region", { name: "Retained rate fixings" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Load fixings" })).toBeDisabled();
  });

  it("proposes an exact decimal string against the loaded version, reusing the identity only for an identical retry", async () => {
    vi.mocked(postEntityOperation).mockRejectedValueOnce(new Error("Network interrupted")).mockResolvedValue({ ...approved, version: 3 });
    const { container } = renderWithRouter(<EntityRateFixings scope={scope} />);
    loadRange();
    await screen.findByRole("region", { name: "Retained rate fixings" });
    fireEvent.change(screen.getByLabelText("Proposed index reference"), { target: { value: "SOFR" } });
    fireEvent.change(screen.getByLabelText("Proposed fixing date"), { target: { value: "2026-05-28" } });
    fireEvent.change(screen.getByLabelText("Rate fraction (annual)"), { target: { value: "0.04260000000000000000000000001" } });
    fireEvent.change(screen.getByLabelText("Fixing source reference"), { target: { value: "publisher:correction" } });
    fireEvent.change(screen.getByLabelText("Fixing justification"), { target: { value: "Publisher restated the rate" } });
    expect(screen.getByText("Corrects the loaded record at version 2.")).toBeVisible();
    expect(screen.getByText(/0\.0425 means 4\.25%/)).toBeVisible();
    fireEvent.click(screen.getByRole("button", { name: "Propose fixing value" }));
    await screen.findByText(/Network interrupted/);
    fireEvent.click(screen.getByRole("button", { name: "Propose fixing value" }));
    await screen.findByText(/sets no accrual rate until a different person approves it/);
    const [first, second] = vi.mocked(postEntityOperation).mock.calls;
    expect(first[0]).toBe("/api/ledger/intercompany/rate-fixings");
    expect(first[1]).toEqual({ requestId: expect.any(String), indexReference: "SOFR", fixingDate: "2026-05-28", rateFraction: "0.04260000000000000000000000001",
      sourceReference: "publisher:correction", justification: "Publisher restated the rate", expectedVersion: 2 });
    expect((second[1] as { requestId: string }).requestId).toBe((first[1] as { requestId: string }).requestId);
    // A confirmed command is finished; a new date is a new request against version 0.
    fireEvent.change(screen.getByLabelText("Proposed fixing date"), { target: { value: "2026-05-30" } });
    expect(screen.getByText(/proposed as a new date \(version 0\)/)).toBeVisible();
    fireEvent.click(screen.getByRole("button", { name: "Propose fixing value" }));
    await waitFor(() => expect(postEntityOperation).toHaveBeenCalledTimes(3));
    const third = vi.mocked(postEntityOperation).mock.calls[2][1] as { requestId: string; expectedVersion: number };
    expect(third.expectedVersion).toBe(0);
    expect(third.requestId).not.toBe((first[1] as { requestId: string }).requestId);
    // Each confirmed proposal reloads the retained range.
    await waitFor(() => expect(getEntityOperation).toHaveBeenCalledTimes(3));
    await screen.findByRole("region", { name: "Retained rate fixings" });
    expect((await axe(container)).violations).toEqual([]);
  });

  it("approves exactly the pending value and surfaces the server's self-approval refusal", async () => {
    vi.mocked(postEntityOperation).mockRejectedValueOnce(createApiErrorFromResponseBody("/api/ledger/intercompany/rate-fixings/fixing-pending/decisions", 409,
      JSON.stringify({ error: "A fixing must be approved by someone other than its proposer." })));
    renderWithRouter(<EntityRateFixings scope={scope} />);
    loadRange();
    await screen.findByRole("region", { name: "Retained rate fixings" });
    expect(screen.queryByRole("button", { name: "Review pending value for 2026-05-28" })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Review pending value for 2026-05-29" }));
    const review = screen.getByRole("region", { name: "Pending fixing review" });
    expect(within(review).getByText(/cannot approve it; the server refuses a self-approval/)).toBeVisible();
    fireEvent.click(within(review).getByRole("button", { name: "Approve pending value" }));
    expect(await screen.findByText(/someone other than/)).toBeVisible();
    expect(postEntityOperation).toHaveBeenCalledWith("/api/ledger/intercompany/rate-fixings/fixing-pending/decisions",
      { requestId: expect.any(String), action: "Approve", expectedVersion: 1, expectedValueDigest: "b".repeat(64), reason: null }, expect.any(Object));
  });

  it("requires a reason to reject the pending value", async () => {
    vi.mocked(postEntityOperation).mockResolvedValue({ ...pending, version: 2, pending: null });
    renderWithRouter(<EntityRateFixings scope={scope} />);
    loadRange();
    await screen.findByRole("region", { name: "Retained rate fixings" });
    fireEvent.click(screen.getByRole("button", { name: "Review pending value for 2026-05-29" }));
    const reject = screen.getByRole("button", { name: "Reject pending value" });
    expect(reject).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Rejection reason"), { target: { value: "Source does not match publisher" } });
    fireEvent.click(reject);
    await screen.findByText(/Pending fixing value rejected/);
    expect(postEntityOperation).toHaveBeenCalledWith("/api/ledger/intercompany/rate-fixings/fixing-pending/decisions",
      { requestId: expect.any(String), action: "Reject", expectedVersion: 1, expectedValueDigest: "b".repeat(64), reason: "Source does not match publisher" }, expect.any(Object));
  });
});
