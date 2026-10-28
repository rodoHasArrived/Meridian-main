import { act, fireEvent, screen } from "@testing-library/react";
import { axe } from "jest-axe";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { renderWithRouter } from "@/test/render";
import { ApiError } from "@/lib/api-errors";
import { createFactorPaydownDraft, prepareFactorPaydown } from "@/lib/api/factor-paydown.api";
import type { FactorPaydownPreview } from "@/types/factor-paydown";
import { FactorPaydownPanel } from "./accounting-screen.factor-paydown-panel";

vi.mock("@/lib/api/factor-paydown.api", () => ({ prepareFactorPaydown: vi.fn(), createFactorPaydownDraft: vi.fn() }));
const props = { periodVersion: 3, bookId: "book-1", periodId: "period-1", fundProfileId: "fund-1" };
const preview: FactorPaydownPreview = {
  preparationFingerprint: "reviewed-hash", components: { "lot.principal": "123.45", "lot.basisRelieved": "120.00", "lot.realizedGain": "3.45" },
  instruction: { scope: { accountingBasis: "Primary", ledgerBookId: "book-1", periodId: "period-1", fundProfileId: "fund-1", securityId: "security-1", bookPositionId: "position-1" },
    currency: "USD", priorFactor: "0.9876543210987654321098765432", newFactor: "0.9", principalPaydown: "123.45",
    lots: [{ expectedLot: { lotId: "lot-A" }, principal: "123.45", transactionBasisAfter: "900.00", functionalBasisAfter: "900.00" }] }
};
function fillForm() {
  const inputs = {
    "Security ID": "security-1", "Book position ID": "position-1", "Paydown effective date": "2026-10-28",
    "Prior factor": "0.9876543210987654321098765432", "New factor": "0.9", "Factor observed at (UTC)": "2026-10-28T12:00:00Z",
    "Factor source system": "custodian", "Factor source reference": "factor-1", "Factor source SHA-256": "a".repeat(64),
    "Cash account path": "Assets:Cash", "Carrying account path": "Assets:MBS", "Realized gain account path": "Income:RealizedGain", "Realized loss account path": "Expense:RealizedLoss",
    "Retained factor evidence JSON": '[{"evidenceId":"retained-factor-evidence"}]'
  };
  Object.entries(inputs).forEach(([label, value]) => fireEvent.change(screen.getByLabelText(label), { target: { value } }));
}
async function prepare() {
  fillForm(); fireEvent.click(screen.getByRole("button", { name: "Prepare factor paydown" }));
  await screen.findByRole("heading", { name: "Authoritative paydown preview" });
}
function confirm() {
  fireEvent.change(screen.getByLabelText("Paydown journal description"), { target: { value: "Reviewed October principal repayment" } });
  fireEvent.click(screen.getByRole("checkbox", { name: /I reviewed the factor evidence/ }));
}
beforeEach(() => {
  vi.resetAllMocks();
  vi.mocked(prepareFactorPaydown).mockResolvedValue(preview);
  vi.mocked(createFactorPaydownDraft).mockResolvedValue({ spine: { scope: preview.instruction.scope }, candidate: { journalEntryId: "draft-1", hasBlockingIssues: false, issues: [] } });
});
describe("Factor-paydown operator workflow", () => {
  it("keeps factor precision and requires reviewed confirmation before creating a draft", async () => {
    const { container } = renderWithRouter(<FactorPaydownPanel {...props} />);
    await prepare();
    expect(prepareFactorPaydown).toHaveBeenCalledWith(expect.objectContaining({ priorFactor: "0.9876543210987654321098765432", newFactor: "0.9", expectedPeriodVersion: 3 }), expect.any(Object));
    expect(screen.getByText("123.45 USD")).toBeVisible();
    expect(screen.getByRole("rowheader", { name: "lot-A" })).toBeVisible();
    expect(screen.getByRole("button", { name: "Create paydown review draft" })).toBeDisabled();
    confirm(); fireEvent.click(screen.getByRole("button", { name: "Create paydown review draft" }));
    await screen.findByText("draft-1");
    expect(createFactorPaydownDraft).toHaveBeenCalledWith(expect.objectContaining({ priorFactor: "0.9876543210987654321098765432" }), "reviewed-hash", "Reviewed October principal repayment", expect.any(Object));
    expect(screen.getByRole("link", { name: "Open paydown journal review" })).toHaveAttribute("href", "/accounting/journal-entries?fundProfileId=fund-1&ledgerBookId=book-1&periodId=period-1&journalEntryId=draft-1");
    expect(screen.getByRole("status")).toHaveTextContent("Approval and posting require the governed asset workflow and remain unavailable here");
    expect(screen.getByRole("button", { name: "Create paydown review draft" })).toBeDisabled();
    expect((await axe(container)).violations).toEqual([]);
  });
  it("discards the preview on edits and prevents late responses from restoring it", async () => {
    renderWithRouter(<FactorPaydownPanel {...props} />);
    await prepare();
    fireEvent.change(screen.getByLabelText("New factor"), { target: { value: "0.8" } });
    expect(screen.queryByRole("heading", { name: "Authoritative paydown preview" })).not.toBeInTheDocument();
    let resolve!: (value: FactorPaydownPreview) => void;
    vi.mocked(prepareFactorPaydown).mockReturnValueOnce(new Promise((done) => { resolve = done; }));
    fireEvent.click(screen.getByRole("button", { name: "Prepare factor paydown" }));
    const signal = vi.mocked(prepareFactorPaydown).mock.calls.at(-1)![1]!.signal!;
    fireEvent.change(screen.getByLabelText("Security ID"), { target: { value: "another-security" } });
    expect(signal.aborted).toBe(true);
    await act(async () => { resolve(preview); });
    expect(screen.queryByRole("heading", { name: "Authoritative paydown preview" })).not.toBeInTheDocument();
  });
  it("aborts and discards preparation across book and tab changes", async () => {
    const view = renderWithRouter(<FactorPaydownPanel {...props} />);
    await prepare();
    view.rerender(<FactorPaydownPanel {...props} bookId="book-2" />);
    expect(screen.getByLabelText("Security ID")).toHaveValue("");
    expect(screen.queryByRole("heading", { name: "Authoritative paydown preview" })).not.toBeInTheDocument();
    view.rerender(<FactorPaydownPanel {...props} active={false} />);
    expect(screen.queryByRole("region", { name: "Factor paydown" })).not.toBeInTheDocument();
    view.rerender(<FactorPaydownPanel {...props} />);
    expect(screen.getByLabelText("Security ID")).toHaveValue("");
  });
  it("rejects malformed evidence and missing scope before requesting authority", async () => {
    const view = renderWithRouter(<FactorPaydownPanel {...props} />);
    fillForm();
    fireEvent.change(screen.getByLabelText("Retained factor evidence JSON"), { target: { value: "[]" } });
    fireEvent.click(screen.getByRole("button", { name: "Prepare factor paydown" }));
    await screen.findByRole("alert"); expect(prepareFactorPaydown).not.toHaveBeenCalled();
    view.rerender(<FactorPaydownPanel {...props} periodId={null} />);
    expect(screen.getByRole("status")).toHaveTextContent("Select a ledger book and period");
    expect(screen.queryByRole("button", { name: "Prepare factor paydown" })).not.toBeInTheDocument();
  });
  it("shows server refusal and offers a fresh preparation", async () => {
    vi.mocked(prepareFactorPaydown).mockRejectedValueOnce(new Error("Factor observation is stale"));
    renderWithRouter(<FactorPaydownPanel {...props} />);
    fillForm(); fireEvent.click(screen.getByRole("button", { name: "Prepare factor paydown" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Factor observation is stale");
    expect(screen.queryByRole("heading", { name: "Authoritative paydown preview" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Prepare factor paydown" })).toBeEnabled();
  });
  it("requires a new preview after the server rejects stale reviewed authority", async () => {
    vi.mocked(createFactorPaydownDraft).mockRejectedValueOnce(new ApiError({ path: "/draft", status: 409,
      responseBody: '{"code":"FACTOR_PAYDOWN_PREVIEW_STALE"}', detail: "Preparation changed" }));
    renderWithRouter(<FactorPaydownPanel {...props} />);
    await prepare(); confirm(); fireEvent.click(screen.getByRole("button", { name: "Create paydown review draft" }));
    await screen.findByRole("alert");
    expect(screen.queryByRole("heading", { name: "Authoritative paydown preview" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Prepare factor paydown" })).toBeEnabled();
  });
  it("retains the exact request on an uncertain write and retries without replacing it", async () => {
    vi.mocked(createFactorPaydownDraft).mockRejectedValueOnce(new Error("Connection interrupted"));
    renderWithRouter(<FactorPaydownPanel {...props} />);
    await prepare(); confirm(); fireEvent.click(screen.getByRole("button", { name: "Create paydown review draft" }));
    await screen.findByRole("button", { name: "Retry same review draft" });
    expect(screen.getByLabelText("New factor")).toBeDisabled();
    expect(screen.getByLabelText("Paydown journal description")).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: "Retry same review draft" }));
    await screen.findByText("draft-1");
    expect(vi.mocked(createFactorPaydownDraft).mock.calls[0].slice(0, 3)).toEqual(vi.mocked(createFactorPaydownDraft).mock.calls[1].slice(0, 3));
  });
});
