import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { axe } from "jest-axe";
import { BondSettlementPanel } from "./bond-settlement-panel";
import { ApiError } from "@/lib/api-errors";
import type { BondSettlementResult } from "@/types/bond-settlement.types";

const result: BondSettlementResult = {
  isBlocked: false, blockers: [], settlement: {
    securityId: "bond", currency: "USD", modelVersion: "bond-analytics-v1",
    context: { asOf: "2026-11-13T00:00:00Z", settlementDate: "2026-11-16", securityVersion: 7 },
    yieldConvention: "NominalPeriodic", originalFace: "100000", bookedFactor: "1",
    cleanPricePer100: "99.123456789012345678", accruedInterestPer100: "1.25",
    cleanConsideration: "99123.46", purchasedAccruedInterest: "1250.00", fees: "25.00",
    dirtyConsideration: "100398.46", observedDirtyCash: "100398.46",
    sourceEvidenceId: "evidence-1", cashAccountId: "cash", accruedInterestAccountId: "interest", feesAccountId: "fees",
    inputs: [{ kind: "SecurityTerms", key: "bond", version: 7, contentSha256: "a".repeat(64) }]
  }
};
function fill() {
  for (const [label, value] of [
    ["Trade settlement date", "2026-11-16"], ["Settlement clean price per 100 face", "99.123456789012345678"],
    ["Original face amount", "100000"], ["Separately expensed fees", "25.00"],
    ["Observed dirty cash (including fees)", "100398.46"], ["Retained cash evidence ID", "evidence-1"],
    ["Cash ledger account ID", "cash"], ["Purchased accrued-interest account ID", "interest"],
    ["Fee expense account ID (required for fees)", "fees"]
  ]) fireEvent.change(screen.getByLabelText(label!), { target: { value } });
}

describe("Bond settlement holding workflow", () => {
  it("prepares exact entered economics and exposes separated consideration and calculation evidence", async () => {
    const prepare = vi.fn().mockResolvedValue(result);
    render(<BondSettlementPanel securityId="bond" termsVersion={7} prepare={prepare} />);
    fill(); fireEvent.click(screen.getByRole("button", { name: "Prepare settlement" }));
    expect(await screen.findByText("Settlement reconciled for review")).toBeInTheDocument();
    expect(prepare).toHaveBeenCalledWith(expect.objectContaining({
      analytics: expect.objectContaining({ securityId: "bond", cleanPrice: "99.123456789012345678", context: expect.objectContaining({ securityVersion: 7 }) }),
      observedDirtyCash: "100398.46", sourceEvidenceId: "evidence-1", fees: "25.00"
    }), expect.objectContaining({ allowDevelopmentFallback: false }));
    expect(screen.getByRole("region", { name: "Prepared settlement review" })).toHaveTextContent("Purchased accrued interest");
    expect(screen.getByText(/No journal or lot was created/)).toBeInTheDocument();
    expect(screen.getByText(/SHA-256/)).toHaveTextContent("a".repeat(64));
    fireEvent.change(screen.getByLabelText("Original face amount"), { target: { value: "200000" } });
    expect(screen.queryByText("Settlement reconciled for review")).not.toBeInTheDocument();
  });

  it("shows reconciliation blockers without a usable settlement", async () => {
    const prepare = vi.fn().mockResolvedValue({ ...result, isBlocked: true, blockers: [{ code: "SettlementNotReconciled", reason: "Observed cash does not match dirty consideration." }] });
    render(<BondSettlementPanel securityId="bond" termsVersion={7} prepare={prepare} />);
    fill(); fireEvent.click(screen.getByRole("button", { name: "Prepare settlement" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Observed cash does not match");
    expect(screen.queryByRole("region", { name: "Prepared settlement review" })).not.toBeInTheDocument();
  });

  it.each([403, 503])("reports permission/service refusal %s and allows retry", async (status) => {
    const prepare = vi.fn().mockRejectedValueOnce(new ApiError({ path: "/api/settlement", status, detail: status === 403 ? "Permission denied" : "Settlement service unavailable" })).mockResolvedValue(result);
    render(<BondSettlementPanel securityId="bond" termsVersion={7} prepare={prepare} />);
    fill(); fireEvent.click(screen.getByRole("button", { name: "Prepare settlement" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Settlement preparation unavailable");
    fireEvent.click(screen.getByRole("button", { name: "Prepare settlement" }));
    expect(await screen.findByText("Settlement reconciled for review")).toBeInTheDocument();
  });

  it("refuses missing fee account and overlapping ledger accounts before preparation", async () => {
    const prepare = vi.fn(); render(<BondSettlementPanel securityId="bond" termsVersion={7} prepare={prepare} />);
    fill(); fireEvent.change(screen.getByLabelText("Fee expense account ID (required for fees)"), { target: { value: "" } });
    fireEvent.click(screen.getByRole("button", { name: "Prepare settlement" }));
    expect(screen.getByRole("alert")).toHaveTextContent("Supply a fee expense account");
    fireEvent.change(screen.getByLabelText("Fee expense account ID (required for fees)"), { target: { value: "cash" } });
    fireEvent.click(screen.getByRole("button", { name: "Prepare settlement" }));
    expect(screen.getByRole("alert")).toHaveTextContent("must be distinct"); expect(prepare).not.toHaveBeenCalled();
  });

  it("ignores delayed results after a holding changes", async () => {
    let resolve!: (value: BondSettlementResult) => void;
    const prepare = vi.fn<typeof import("@/lib/api/bond-settlement.api").prepareBondSettlement>(() => new Promise<BondSettlementResult>((done) => { resolve = done; }));
    const view = render(<BondSettlementPanel securityId="bond" termsVersion={7} prepare={prepare} />);
    fill(); fireEvent.click(screen.getByRole("button", { name: "Prepare settlement" }));
    view.rerender(<BondSettlementPanel securityId="other" termsVersion={2} prepare={prepare} />);
    resolve(result);
    await waitFor(() => expect(screen.getByLabelText("Settlement terms version")).toHaveValue("2"));
    expect(screen.queryByText("Settlement reconciled for review")).not.toBeInTheDocument();
    expect(prepare.mock.calls[0]![1]!.signal!.aborted).toBe(true);
  });

  it("has accessible form names and no basic accessibility violations", async () => {
    const { container } = render(<BondSettlementPanel securityId="bond" termsVersion={7} />);
    expect((await axe(container)).violations).toEqual([]);
  });
});
