import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { BondScreeningPanel } from "./bond-screening-panel";
import type { BondScreeningPage, BondScreeningQuery } from "@/types/bond-workflow.types";

const page: BondScreeningPage = { rows: [{ reference: { securityId: "candidate-bond", issuerName: "Synthetic issuer", currency: "USD", lifecycle: { maturityDate: "2031-11-12", isCallable: false }, couponStructure: { kind: "Fixed" } }, ratings: [], gaps: ["No eligible retained agency rating"] }], nextCursor: "bound-page-cursor", gaps: [], sourcePagesRead: 1, referenceKnowledgeTimeVerified: false, limitations: ["Recorded cutoff is unverified for reference terms"] };
describe("BondScreeningPanel", () => {
  it("uses combined server filters, reuses the bound cutoff/cursor and exposes candidate selection", async () => {
    const candidate = vi.fn(); const lookup = vi.fn(async () => page);
    render(<BondScreeningPanel asOf="2026-11-12" currentSecurityId="selected" onCandidate={candidate} screen={lookup} />);
    fireEvent.change(screen.getByLabelText("Screen issuer name"), { target: { value: "Synthetic" } });
    fireEvent.change(screen.getByLabelText("Rating agency (source label)"), { target: { value: "Fitch" } });
    fireEvent.change(screen.getByLabelText("Accepted rating labels (comma separated)"), { target: { value: "AA, A+" } });
    fireEvent.click(screen.getByRole("button", { name: "Screen comparison bonds" })); await screen.findByRole("region", { name: "Bond screening results" });
    expect(lookup).toHaveBeenCalledWith(expect.objectContaining({ issuer: "Synthetic", ratingAgency: "Fitch", acceptedRatings: ["AA", "A+"], pageSize: 25 }), expect.objectContaining({ allowDevelopmentFallback: false }));
    fireEvent.click(screen.getByRole("button", { name: "Add comparison candidate" })); expect(candidate).toHaveBeenCalledWith("candidate-bond");
    fireEvent.click(screen.getByRole("button", { name: "Load next screened page" })); await waitFor(() => expect(lookup).toHaveBeenCalledTimes(2));
    expect(lookup).toHaveBeenLastCalledWith(expect.objectContaining({ knownAtUtc: expect.any(String), cursor: "bound-page-cursor" }), expect.any(Object));
    expect(screen.getByText("Recorded cutoff is unverified for reference terms")).toBeInTheDocument();
  });
  it("invalidates an in-flight result when an agency or other filter changes", async () => {
    let resolve!: () => void; const lookup = vi.fn((_query: BondScreeningQuery) => new Promise<BondScreeningPage>((complete) => { resolve = () => complete(page); }));
    render(<BondScreeningPanel asOf="2026-11-12" currentSecurityId="selected" onCandidate={vi.fn()} screen={lookup} />);
    fireEvent.click(screen.getByRole("button", { name: "Screen comparison bonds" })); await waitFor(() => expect(lookup).toHaveBeenCalledOnce());
    fireEvent.change(screen.getByLabelText("Rating agency (source label)"), { target: { value: "Moody's" } }); await act(async () => resolve());
    expect(screen.queryByRole("region", { name: "Bond screening results" })).not.toBeInTheDocument();
  });
});
