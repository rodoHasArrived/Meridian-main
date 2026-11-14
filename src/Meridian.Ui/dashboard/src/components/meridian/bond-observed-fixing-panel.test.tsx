import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { axe } from "jest-axe";
import { BondObservedFixingPanel } from "./bond-observed-fixing-panel";
import type { BondObservedFixingReceipt } from "@/lib/api/bond-observed-fixings.api";
import type { SecurityRateObservation } from "@/lib/api/security-rates.api";

const observation: SecurityRateObservation = { provider: "fred", seriesId: "SOFR", value: "3.123456789012345678", originalValue: "3.123456789012345678",
  unit: "Percent", sourceUnits: "Percent", observationDate: "2026-01-15", publishedAt: null, vintageDate: "2026-01-16",
  vintageEndDate: "9999-12-31", retrievedAt: "2026-01-16T12:00:00Z", transformation: "lin", kind: "ObservedFixing", currency: "USD" };
const receipt: BondObservedFixingReceipt = { receiptId: "fixing-receipt", fixing: { securityId: "bond", securityVersion: 7, indexName: "SOFR",
  resetDate: "2026-01-15", rateFraction: "0.03123456789012345678", observation, recordedBy: "operator", justification: "Reviewed exact reset" },
  recordedAt: "2026-02-15T00:00:00Z", contentSha256: "fixing-hash" };

describe("BondObservedFixingPanel", () => {
  it("retains exact provider evidence against the current security/reset and explicitly selects its immutable receipt", async () => {
    const retain = vi.fn().mockResolvedValue(receipt);
    const onSelect = vi.fn();
    const { container } = render(<BondObservedFixingPanel securityId="bond" securityVersion={7} selectedReceiptIds={[]}
      onSelect={onSelect} onRemove={vi.fn()} loadObservations={vi.fn().mockResolvedValue([observation])}
      loadRetained={vi.fn().mockResolvedValue([])} retain={retain} />);
    fireEvent.change(screen.getByLabelText("Contractual reset / fixing date"), { target: { value: "2026-01-15" } });
    fireEvent.click(screen.getByRole("button", { name: "Load observed fixing evidence" }));
    await screen.findByRole("option", { name: /fred · SOFR · 2026-01-15/ });
    fireEvent.change(screen.getByLabelText("Verified observed fixing"), { target: { value: "0" } });
    expect(screen.getByText(/original 3.123456789012345678 Percent/)).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Observed fixing review justification"), { target: { value: "Reviewed exact reset" } });
    expect(screen.getByRole("button", { name: "Retain and select observed fixing" })).toBeDisabled();
    fireEvent.click(screen.getByRole("checkbox"));
    fireEvent.click(screen.getByRole("button", { name: "Retain and select observed fixing" }));
    await screen.findByText(/receipt fixing-receipt; SHA-256 fixing-hash/);
    expect(retain).toHaveBeenCalledWith("bond", { expectedSecurityVersion: 7, resetDate: "2026-01-15", observation, justification: "Reviewed exact reset" }, expect.anything());
    expect(onSelect).toHaveBeenCalledWith(receipt);
    expect((await axe(container)).violations).toEqual([]);
  });

  it("keeps retained receipts selectable when the source is offline", async () => {
    const onSelect = vi.fn();
    render(<BondObservedFixingPanel securityId="bond" securityVersion={7} selectedReceiptIds={[]}
      onSelect={onSelect} onRemove={vi.fn()} loadObservations={vi.fn().mockRejectedValue(new Error("source offline"))}
      loadRetained={vi.fn().mockResolvedValue([receipt])} retain={vi.fn()} />);
    fireEvent.change(screen.getByLabelText("Contractual reset / fixing date"), { target: { value: "2026-01-15" } });
    fireEvent.click(screen.getByRole("button", { name: "Load observed fixing evidence" }));
    fireEvent.click(await screen.findByRole("button", { name: "Use this observed fixing" }));
    expect(onSelect).toHaveBeenCalledWith(receipt);
    expect(screen.getByText("source offline")).toBeInTheDocument();
  });
});
