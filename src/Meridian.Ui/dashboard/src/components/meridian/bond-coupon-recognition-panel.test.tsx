import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { axe } from "jest-axe";
import { BondCouponRecognitionPanel } from "./bond-coupon-recognition-panel";
import type { BondCouponRecognitionPreview, BondCouponRecognitionRequest } from "@/types/bond-workflow.types";

function response(request: BondCouponRecognitionRequest): BondCouponRecognitionPreview {
  return { fingerprint: "coupon-fingerprint", earnedDelta: "-5", settledGross: "0", remainingSignedBalance: "-5", hasChanges: true,
    instruction: { modelVersion: "bond-coupon-recognition-v1", scope: { tenantId: "tenant", companyId: "company", accountId: "account", bookId: "book", baseCurrency: "USD" }, request, eventId: "canonical-income", lines: [{ accountId: "ContraIncome", debit: "5", credit: "0", currency: "USD" }, { accountId: "InterestPayable", debit: "0", credit: "5", currency: "USD" }] } };
}
describe("BondCouponRecognitionPanel", () => {
  it("requires review and prepares only the request/fingerprint, retaining signed coupon legs", async () => {
    const preview = vi.fn(async (_book: string, _account: string, request: BondCouponRecognitionRequest) => response(request));
    const prepare = vi.fn(async (_book: string, _account: string, body: { request: BondCouponRecognitionRequest; expectedFingerprint: string }) => ({ preparation: { preview: response(body.request) }, projection: { spine: { eventId: "canonical-income", currency: "USD", eventKind: "Income" }, wasReplay: false } }));
    const { container } = render(<BondCouponRecognitionPanel bookId="book" accountId="account" lotId="lot" throughDate="2026-11-12" onLockChange={vi.fn()} preview={preview} prepare={prepare} />);
    fireEvent.click(screen.getByText("Coupon accrual and collection for governed review"));
    fireEvent.change(screen.getByLabelText("Coupon accounting period ID"), { target: { value: "period" } });
    fireEvent.click(screen.getByRole("button", { name: "Preview coupon recognition" })); await screen.findByRole("region", { name: "Coupon recognition preview" });
    expect(preview.mock.calls[0][2].cashAllocations).toEqual([]); expect(screen.getByText("InterestPayable")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Prepare for review" })).toBeDisabled();
    fireEvent.click(screen.getByRole("checkbox", { name: /I reviewed the coupon accrual/ })); fireEvent.click(screen.getByRole("button", { name: "Prepare for review" }));
    await screen.findByRole("status"); expect(prepare.mock.calls[0][2]).toEqual({ request: preview.mock.calls[0][2], expectedFingerprint: "coupon-fingerprint" });
    expect(screen.getByText(/No journal was posted and no payment was created/)).toBeInTheDocument();
    expect((await axe(container)).violations).toEqual([]);
  });
  it("requires a posted receipt and rationale together for governed reversal", async () => {
    const preview = vi.fn(async (_book: string, _account: string, request: BondCouponRecognitionRequest) => response(request));
    render(<BondCouponRecognitionPanel bookId="book" accountId="account" lotId="lot" throughDate="2026-11-12" onLockChange={vi.fn()} preview={preview} />);
    fireEvent.change(screen.getByLabelText("Coupon accounting period ID"), { target: { value: "period" } });
    fireEvent.change(screen.getByLabelText("Posted coupon receipt ID to reverse"), { target: { value: "posted-receipt" } });
    fireEvent.click(screen.getByRole("button", { name: "Preview coupon recognition" })); await screen.findByRole("alert"); expect(preview).not.toHaveBeenCalled();
    fireEvent.change(screen.getByLabelText("Coupon reversal rationale"), { target: { value: "Correct retained source evidence" } });
    fireEvent.click(screen.getByRole("button", { name: "Preview coupon recognition" })); await screen.findByRole("region", { name: "Coupon recognition preview" });
    expect(preview.mock.calls[0][2]).toMatchObject({ reversesReceiptId: "posted-receipt", correctionRationale: "Correct retained source evidence" });
  });
  it("clears a reviewed fingerprint on edits and retries an uncertain preparation with the same identity", async () => {
    const preview = vi.fn(async (_book: string, _account: string, request: BondCouponRecognitionRequest) => response(request));
    const prepare = vi.fn().mockRejectedValueOnce(new Error("Response lost")).mockImplementation(async (_book: string, _account: string, body: { request: BondCouponRecognitionRequest }) => ({ preparation: { preview: response(body.request) }, projection: { spine: { eventId: "canonical-income", currency: "USD", eventKind: "Income" }, wasReplay: true } }));
    render(<BondCouponRecognitionPanel bookId="book" accountId="account" lotId="lot" throughDate="2026-11-12" onLockChange={vi.fn()} preview={preview} prepare={prepare} />);
    fireEvent.change(screen.getByLabelText("Coupon accounting period ID"), { target: { value: "period" } }); fireEvent.click(screen.getByRole("button", { name: "Preview coupon recognition" })); await screen.findByRole("region", { name: "Coupon recognition preview" });
    fireEvent.change(screen.getByLabelText("Coupon accounting period ID"), { target: { value: "period-2" } }); expect(screen.queryByRole("region", { name: "Coupon recognition preview" })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Preview coupon recognition" })); await screen.findByRole("region", { name: "Coupon recognition preview" });
    fireEvent.click(screen.getByRole("checkbox", { name: /I reviewed the coupon accrual/ })); fireEvent.click(screen.getByRole("button", { name: "Prepare for review" })); await screen.findByRole("alert");
    expect(screen.getByLabelText("Coupon accounting period ID")).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: "Retry same coupon preparation" })); await screen.findByText("Coupon Income source prepared for governed review");
    expect(prepare.mock.calls[1][2]).toEqual(prepare.mock.calls[0][2]);
  });
});
