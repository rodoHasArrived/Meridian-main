import { act, fireEvent, screen } from "@testing-library/react";
import { axe } from "jest-axe";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { renderWithRouter } from "@/test/render";
import { ApiError } from "@/lib/api-errors";
import { createCanonicalAmortizationDraft, prepareCanonicalAmortization, type CanonicalAmortizationPreview } from "@/lib/api/canonical-amortization.api";
import { CanonicalAmortizationPanel } from "./accounting-screen.canonical-amortization-panel";
vi.mock("@/lib/api/canonical-amortization.api", () => ({ createCanonicalAmortizationDraft: vi.fn(), prepareCanonicalAmortization: vi.fn() }));
const props = { bookId: "book-1", periodId: "period-1", periodVersion: 4, fundProfileId: "fund-1" };
const scope = { ledgerBookId: "book-1", periodId: "period-1", fundProfileId: "fund-1", accountingBasis: "Primary" as const };
const preview: CanonicalAmortizationPreview = {
  scope, preparationFingerprint: "retained-hash",
  instruction: { asOfDate: "2026-11-01", calculationVersion: "canonical-lot-amortization-v5", expectedLot: { taxLotRecordId: "lot-1", ledgerBookId: "book-1", lotId: "Treasury acquisition", version: 7, acquisition: { functionalCurrency: "USD", faceValueTerms: { amortizationMethod: "ConstantYield", effectiveYield: "0.031234567890123456789" } } }, security: { securityId: "security-1", version: 3 } },
  projection: { transactionCostBasis: "999.1234567890123456789", functionalCostBasis: "999.1234567890123456789", transactionMovement: "1.234567890123456789", functionalMovement: "1.234567890123456789" },
  lines: [{ accountId: "Assets:Bonds", debit: "1.234567890123456789", credit: "0", currency: "USD" }, { accountId: "Income:Amortization", debit: "0", credit: "1.234567890123456789", currency: "USD" }]
};
function fill() {
  Object.entries({ "Canonical tax lot ID": "lot-1", "Amortization occurrence date": "2026-11-01", "Amortization asset account path": "Assets:Bonds", "Amortization income account path": "Income:Amortization" }).forEach(([label, value]) => fireEvent.change(screen.getByLabelText(label), { target: { value } }));
  for (const prefix of ["Reviewed Security Master evidence", "Reviewed amortization occurrence evidence"]) {
    fireEvent.click(screen.getByText(prefix));
    Object.entries({ "Evidence ID": "retained-1", "Evidence URI": "document://retained-1", "Subject ID": "e94b3ae7-d745-4788-ad97-39557212ae99", "Evidence version": "1", "Effective date": "2026-11-01", "Content SHA-256": "a".repeat(64), "Source system": "custodian", "Source reference": "statement-1", "Reviewed by": "reviewer", "Reviewed at (UTC)": "2026-11-01T12:00:00Z", "Retained by": "custody", "Retained at (UTC)": "2026-11-01T12:00:00Z" }).forEach(([label, value]) => fireEvent.change(screen.getByLabelText(`${prefix}: ${label}`), { target: { value } }));
  }

}
async function prepare() { fill(); fireEvent.click(screen.getByRole("button", { name: "Preview amortization occurrence" })); await screen.findByRole("heading", { name: "Review calculated occurrence" }); }
function confirm() { fireEvent.change(screen.getByLabelText("Amortization journal description"), { target: { value: "Reviewed November amortization" } }); fireEvent.click(screen.getByRole("checkbox", { name: /I reviewed the retained schedule/ })); }
beforeEach(() => {
  vi.resetAllMocks(); vi.mocked(prepareCanonicalAmortization).mockResolvedValue(preview);
  vi.mocked(createCanonicalAmortizationDraft).mockResolvedValue({ spine: { scope }, candidate: { journalEntryId: "draft-7", hasBlockingIssues: false, issues: [] } });
});
describe("Canonical amortization journey", () => {
  it("configures policies, reviews exact server amounts, confirms and opens the exact governed journal", async () => {
    const { container } = renderWithRouter(<CanonicalAmortizationPanel {...props} />);
    fireEvent.click(screen.getByRole("checkbox", { name: /Amortize remaining face/ }));
    await prepare();
    expect(prepareCanonicalAmortization).toHaveBeenCalledWith(expect.objectContaining({ amortizeRemainingFace: true, automaticYieldToWorst: false, expectedPeriodVersion: 4, taxLotRecordId: "lot-1" }), expect.any(Object));
    expect(screen.getAllByText("999.1234567890123456789")).toHaveLength(2);
    expect(screen.getByRole("button", { name: "Create amortization review draft" })).toBeDisabled();
    confirm(); fireEvent.click(screen.getByRole("button", { name: "Create amortization review draft" }));
    const link = await screen.findByRole("link", { name: "Open amortization journal review" });
    expect(link).toHaveAttribute("href", "/accounting/journal-entries?fundProfileId=fund-1&ledgerBookId=book-1&periodId=period-1&journalEntryId=draft-7");
    expect(createCanonicalAmortizationDraft).toHaveBeenCalledWith(expect.objectContaining({ taxLotRecordId: "lot-1" }), "retained-hash", "Reviewed November amortization", expect.any(Object));
    expect((await axe(container)).violations).toEqual([]);
  });
  it("invalidates on input edits, ignores late previews and discards on scope changes", async () => {
    const view = renderWithRouter(<CanonicalAmortizationPanel {...props} />); await prepare();
    fireEvent.change(screen.getByLabelText("Canonical tax lot ID"), { target: { value: "lot-2" } });
    expect(screen.queryByRole("heading", { name: "Review calculated occurrence" })).not.toBeInTheDocument();
    let resolve!: (result: CanonicalAmortizationPreview) => void;
    vi.mocked(prepareCanonicalAmortization).mockReturnValueOnce(new Promise(done => { resolve = done; }));
    fireEvent.click(screen.getByRole("button", { name: "Preview amortization occurrence" }));
    fireEvent.change(screen.getByLabelText("Amortization occurrence date"), { target: { value: "2026-11-02" } });
    await act(async () => resolve(preview));
    expect(screen.queryByRole("heading", { name: "Review calculated occurrence" })).not.toBeInTheDocument();
    view.rerender(<CanonicalAmortizationPanel {...props} bookId="book-2" />);
    expect(screen.getByLabelText("Canonical tax lot ID")).toHaveValue("");
  });
  it("shows missing scope and permission denial without promoting a preview", async () => {
    const view = renderWithRouter(<CanonicalAmortizationPanel {...props} periodId={null} />);
    expect(screen.getByRole("status")).toHaveTextContent("Select an authoritative ledger book");
    view.rerender(<CanonicalAmortizationPanel {...props} />);
    vi.mocked(prepareCanonicalAmortization).mockRejectedValueOnce(new ApiError({ path: "/prepare", status: 403, detail: "Ledger management permission required" }));
    fill(); fireEvent.click(screen.getByRole("button", { name: "Preview amortization occurrence" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Permission denied");
    expect(createCanonicalAmortizationDraft).not.toHaveBeenCalled();
  });
  it("keeps one exact reviewed request for uncertain retry and requires fresh review after stale authority", async () => {
    vi.mocked(createCanonicalAmortizationDraft).mockRejectedValueOnce(new Error("Connection interrupted"));
    renderWithRouter(<CanonicalAmortizationPanel {...props} />); await prepare(); confirm();
    fireEvent.click(screen.getByRole("button", { name: "Create amortization review draft" }));
    await screen.findByText(/draft outcome is uncertain/);
    expect(screen.getByLabelText("Canonical tax lot ID")).toBeDisabled();
    vi.mocked(createCanonicalAmortizationDraft).mockRejectedValueOnce(new ApiError({ path: "/drafts", status: 409, responseBody: '{"code":"AMORTIZATION_PREVIEW_STALE"}', detail: "Changed" }));
    fireEvent.click(screen.getByRole("button", { name: "Retry same amortization draft" }));
    await screen.findByText("Changed");
    expect(vi.mocked(createCanonicalAmortizationDraft).mock.calls[0][0]).toBe(vi.mocked(createCanonicalAmortizationDraft).mock.calls[1][0]);
    expect(screen.queryByRole("heading", { name: "Review calculated occurrence" })).not.toBeInTheDocument();
  });
});
