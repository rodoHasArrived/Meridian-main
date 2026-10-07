import { act, fireEvent, screen, waitFor, within } from "@testing-library/react";
import { axe } from "jest-axe";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createConsolidationDrafts, previewConsolidation } from "@/lib/consolidation-api";
import { renderWithRouter } from "@/test/render";
import type { ConsolidationRequest, ConsolidationView } from "@/types/consolidation";
import { ConsolidationPanel } from "./accounting-screen.consolidation-panel";

vi.mock("@/lib/consolidation-api", () => ({ previewConsolidation: vi.fn(), createConsolidationDrafts: vi.fn() }));
const request: ConsolidationRequest = {
  organizationId: "00000000-0000-0000-0000-000000000001", ownershipRootId: "00000000-0000-0000-0000-000000000002",
  eliminationBookId: "00000000-0000-0000-0000-000000000003", periodId: "00000000-0000-0000-0000-000000000004", asOf: "2026-09-30"
};
const source = {
  ledgerBookId: "source-book-a", journalEntryId: "source-journal-a", lineId: "source-line-a", entityId: "entity-a", counterpartyId: "entity-b",
  accountPath: "Assets:Intercompany Receivable", debit: 120, credit: 0, effectiveDate: "2026-09-29", drillThrough: "/unsupported/source/route"
};
function response(overrides: Partial<ConsolidationView> = {}): ConsolidationView {
  return {
    request, fundProfileId: "group-fund", currency: "USD", scopeLimitation: "Exactly two directly 100% owned entities; one currency; Primary basis.",
    entityIds: ["entity-a", "entity-b"], ownershipLinkIds: ["effective-ownership-link"], sourceFingerprint: "sources-v1", blockers: [],
    balances: [{ accountPath: source.accountPath, accountType: "Asset", grossBalance: 120, proposedEliminations: -100, postedEliminations: 0, consolidatedBalance: 120, previewBalance: 20, sources: [source] }],
    matches: [{ postingEntityId: "entity-a", counterpartyId: "entity-b", receivable: 120, payable: 100, matchedAmount: 100, unmatchedReceivable: 20, unmatchedPayable: 0, sources: [source] }],
    drafts: [], ...overrides
  };
}
function fillScope() {
  fireEvent.change(screen.getByLabelText("Organization ID"), { target: { value: request.organizationId } });
  fireEvent.change(screen.getByLabelText("Ownership root ID"), { target: { value: request.ownershipRootId } });
  fireEvent.change(screen.getByLabelText("Ownership as of"), { target: { value: request.asOf } });
}
function renderPanel() {
  return renderWithRouter(<ConsolidationPanel initialBookId={request.eliminationBookId} initialPeriodId={request.periodId} />);
}
async function preview() {
  fillScope();
  fireEvent.click(screen.getByRole("button", { name: "Preview consolidation" }));
  await screen.findByRole("heading", { name: "Group balances" });
}

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(previewConsolidation).mockResolvedValue(response());
  vi.mocked(createConsolidationDrafts).mockResolvedValue(response({ drafts: [{ journalEntryId: "draft-1", status: "Draft", requiresRenewedReview: false, adjustsJournalEntryId: null, lines: [] }] }));
});

describe("Accounting consolidation", () => {
  it("keeps gross, proposed, posted, consolidated, and unmatched balances distinct with retained source evidence", async () => {
    const { container } = renderPanel();
    expect(screen.getByRole("button", { name: "Create review drafts" })).toBeDisabled();
    await preview();
    expect(previewConsolidation).toHaveBeenCalledWith(request, expect.objectContaining({ signal: expect.any(AbortSignal) }));
    const balance = within(screen.getByRole("region", { name: "Group balance table" }));
    expect(balance.getAllByRole("cell").slice(0, 5).map((item) => item.textContent)).toEqual(["120.00", "(100.00)", "0.00", "120.00", "20.00"]);
    const matches = within(screen.getByRole("region", { name: "Reciprocal match table" }));
    expect(matches.getByRole("rowheader")).toHaveTextContent("entity-a → entity-b");
    expect(matches.getByRole("columnheader", { name: "Unmatched receivable" })).toBeInTheDocument();
    fireEvent.click(balance.getByText("Sources for Assets:Intercompany Receivable (1)"));
    expect(balance.getByText("source-journal-a")).toBeVisible();
    expect(balance.getByText("source-line-a")).toBeVisible();
    expect(screen.queryByRole("link", { name: /source-journal-a/ })).not.toBeInTheDocument();
    expect((await axe(container)).violations).toEqual([]);
  });

  it("creates review drafts and hands off to the existing book-scoped workflow without posting", async () => {
    renderPanel();
    await preview();
    fireEvent.click(screen.getByRole("button", { name: "Create review drafts" }));
    await screen.findByText("draft-1");
    expect(createConsolidationDrafts).toHaveBeenCalledTimes(1);
    expect(createConsolidationDrafts).toHaveBeenCalledWith(request, expect.any(Object));
    expect(screen.getByText("Draft", { exact: true })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Open journal approval workflow" })).toHaveAttribute("href", `/accounting/journal-entries?fundProfileId=group-fund&ledgerBookId=${request.eliminationBookId}`);
    const balance = within(screen.getByRole("region", { name: "Group balance table" }));
    expect(balance.getAllByRole("cell")[3]).toHaveTextContent("120.00");
  });

  it("shows renewed-review and correction lineage while only posted amounts change consolidated balances", async () => {
    vi.mocked(previewConsolidation).mockResolvedValue(response({
      balances: [{ ...response().balances[0], postedEliminations: -80, consolidatedBalance: 40, proposedEliminations: -20 }],
      drafts: [{ journalEntryId: "correction-draft", status: "Draft", requiresRenewedReview: true, adjustsJournalEntryId: "posted-original", lines: [] }]
    }));
    renderPanel();
    await preview();
    expect(screen.getByText("Renewed review required")).toBeInTheDocument();
    expect(screen.getByText("posted-original")).toBeInTheDocument();
    const cells = within(screen.getByRole("region", { name: "Group balance table" })).getAllByRole("cell");
    expect(cells.slice(0, 5).map((item) => item.textContent)).toEqual(["120.00", "(20.00)", "(80.00)", "40.00", "20.00"]);
  });

  it("invalidates the preview when scope changes, and fails closed on server blockers and errors", async () => {
    renderPanel();
    await preview();
    fireEvent.change(screen.getByLabelText("Ownership as of"), { target: { value: "2026-10-01" } });
    expect(screen.queryByRole("heading", { name: "Group balances" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Create review drafts" })).toBeDisabled();
    vi.mocked(previewConsolidation).mockResolvedValueOnce(response({ blockers: ["Both entities must have the same functional currency."] }));
    fireEvent.click(screen.getByRole("button", { name: "Preview consolidation" }));
    await screen.findByText("Both entities must have the same functional currency.");
    expect(screen.getByRole("button", { name: "Create review drafts" })).toBeDisabled();
    vi.mocked(previewConsolidation).mockRejectedValueOnce(new Error("Ownership service unavailable"));
    fireEvent.click(screen.getByRole("button", { name: "Preview consolidation" }));
    await screen.findByText("Ownership service unavailable");
    expect(screen.queryByRole("heading", { name: "Group balances" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Create review drafts" })).toBeDisabled();
  });

  it("aborts and discards an in-flight response when the selected ledger context changes", async () => {
    let finish: (view: ConsolidationView) => void = () => {};
    vi.mocked(previewConsolidation).mockReturnValueOnce(new Promise((resolve) => { finish = resolve; }));
    const { rerender } = renderPanel();
    fillScope();
    fireEvent.click(screen.getByRole("button", { name: "Preview consolidation" }));
    const signal = vi.mocked(previewConsolidation).mock.calls[0][1]?.signal;
    rerender(<ConsolidationPanel initialBookId="new-book" initialPeriodId="new-period" />);
    expect(signal?.aborted).toBe(true);
    await act(async () => finish(response()));
    expect(screen.queryByRole("heading", { name: "Group balances" })).not.toBeInTheDocument();
    expect(screen.getByLabelText("Elimination book ID")).toHaveValue("new-book");
    expect(screen.getByRole("button", { name: "Create review drafts" })).toBeDisabled();
  });

  it("serializes draft requests and requires a fresh preview after a failed mutation", async () => {
    let fail: (error: Error) => void = () => {};
    vi.mocked(createConsolidationDrafts).mockReturnValueOnce(new Promise((_resolve, reject) => { fail = reject; }));
    renderPanel();
    await preview();
    fireEvent.click(screen.getByRole("button", { name: "Create review drafts" }));
    expect(screen.getByRole("button", { name: "Creating review drafts…" })).toBeDisabled();
    expect(screen.getByLabelText("Organization ID")).toBeDisabled();
    await act(async () => fail(new Error("Source balances changed; preview again.")));
    await waitFor(() => expect(screen.getByRole("button", { name: "Create review drafts" })).toBeDisabled());
    expect(screen.queryByRole("heading", { name: "Group balances" })).not.toBeInTheDocument();
    expect(createConsolidationDrafts).toHaveBeenCalledTimes(1);
  });
});
