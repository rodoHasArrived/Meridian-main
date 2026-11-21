import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it, vi } from "vitest";
import { BondDiscoveryPanel } from "./bond-discovery-panel";
import type { BondDiscoveryResult } from "@/lib/api/bond-discovery.api";
import type { DataUploadPreviewResult } from "@/types";
import { createApiErrorFromResponseBody } from "@/lib/api-errors";

const result: BondDiscoveryResult = {
  provider: "alpaca", providerLabel: "Alpaca", available: true, unavailableReason: null,
  canStage: true, query: "912", observedAtUtc: "2026-11-13T00:00:00Z",
  candidates: [{ symbol: "912TEST", name: "Synthetic Treasury", assetType: "FixedIncome", source: "alpaca",
    currency: "USD", country: "US", exchange: "OTC", figi: "BBGTEST", compositeFigi: null }]
};
const preview: DataUploadPreviewResult = {
  uploadId: "UP-BOND", templateId: "asset-information", templateLabel: "Asset information",
  fileName: "provider-bond-candidate.csv", fileSizeBytes: 250, contentType: "text/csv", uploadedBy: "reviewer",
  uploadedAtUtc: result.observedAtUtc, retainedPath: "workstation/data-uploads/UP-BOND/provider-bond-candidate.csv",
  parsedRowCount: 1, previewRowCount: 1, headers: [], previewRows: [], issues: [], status: "ReadyForReview",
  nextAction: "Review retained source evidence."
};
function renderPanel(discover = vi.fn().mockResolvedValue(result), stage = vi.fn().mockResolvedValue(preview)) {
  render(<MemoryRouter><BondDiscoveryPanel api={{ discover, stage }} /></MemoryRouter>);
  return { discover, stage };
}
async function lookup() {
  fireEvent.change(screen.getByLabelText("Bond symbol or issuer"), { target: { value: "912" } });
  fireEvent.click(screen.getByRole("button", { name: "Look up provider bonds" }));
  await screen.findByRole("button", { name: "Review 912TEST" });
}

describe("provider bond discovery", () => {
  it("looks up a provider candidate, reviews missing terms, and stages retained evidence", async () => {
    const { discover, stage } = renderPanel(); await lookup();
    expect(discover).toHaveBeenCalledWith("912", expect.objectContaining({ signal: expect.any(AbortSignal) }));
    fireEvent.click(screen.getByRole("button", { name: "Review 912TEST" }));
    expect(screen.getByText("Unavailable from discovery; terms review required")).toBeInTheDocument();
    const button = screen.getByRole("button", { name: "Stage candidate import" });
    expect(button).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Review justification"), { target: { value: "Reviewed identity against source" } });
    fireEvent.click(screen.getByRole("checkbox")); fireEvent.click(button);
    await screen.findByText("Candidate import staged for review");
    expect(stage).toHaveBeenCalledWith(result.candidates[0], result.observedAtUtc, "Reviewed identity against source", expect.objectContaining({ signal: expect.any(AbortSignal) }));
    expect(screen.getByText(/UP-BOND/)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Open Data intake" })).toHaveAttribute("href", "/data/import");
    expect(button).toBeDisabled();
  });
  it("keeps staging unavailable for view permission", async () => {
    const { stage } = renderPanel(vi.fn().mockResolvedValue({ ...result, canStage: false })); await lookup();
    expect(screen.getByText(/staging requires Modify Security Master/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Review 912TEST" }));
    fireEvent.change(screen.getByLabelText("Review justification"), { target: { value: "Reviewed" } });
    fireEvent.click(screen.getByRole("checkbox"));
    expect(screen.getByRole("button", { name: "Stage candidate import" })).toBeDisabled(); expect(stage).not.toHaveBeenCalled();
  });
  it("shows unavailable providers without fabricating candidates", async () => {
    renderPanel(vi.fn().mockResolvedValue({ ...result, available: false, candidates: [], unavailableReason: "Credentials unavailable" }));
    fireEvent.change(screen.getByLabelText("Bond symbol or issuer"), { target: { value: "912" } });
    fireEvent.click(screen.getByRole("button", { name: "Look up provider bonds" }));
    await screen.findByText("Provider bond lookup unavailable");
    expect(screen.getByText("Credentials unavailable")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Review 912TEST" })).not.toBeInTheDocument();
  });
  it("shows failed provider evidence as unavailable without displaying a no-match result", async () => {
    const failure = createApiErrorFromResponseBody("/api/reference-data/bonds/discovery", 503,
      JSON.stringify({ ...result, available: false, candidates: [], failureReason: "malformedPayload",
        unavailableReason: "Alpaca bond lookup could not be completed." }));
    const { stage } = renderPanel(vi.fn().mockRejectedValue(failure));
    fireEvent.change(screen.getByLabelText("Bond symbol or issuer"), { target: { value: "912" } });
    fireEvent.click(screen.getByRole("button", { name: "Look up provider bonds" }));
    await screen.findByRole("alert");
    expect(screen.queryByText(/No bond candidates returned/)).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Review 912TEST" })).not.toBeInTheDocument();
    expect(stage).not.toHaveBeenCalled();
  });
  it("shows completed empty provider evidence as no matches", async () => {
    renderPanel(vi.fn().mockResolvedValue({ ...result, candidates: [] }));
    fireEvent.change(screen.getByLabelText("Bond symbol or issuer"), { target: { value: "912" } });
    fireEvent.click(screen.getByRole("button", { name: "Look up provider bonds" }));
    await screen.findByText(/No bond candidates returned/);
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    expect(screen.queryByText("Provider bond lookup unavailable")).not.toBeInTheDocument();
  });
  it("discards delayed lookup results after query changes", async () => {
    let resolve!: (value: BondDiscoveryResult) => void;
    renderPanel(vi.fn().mockReturnValue(new Promise<BondDiscoveryResult>(done => { resolve = done; })));
    fireEvent.change(screen.getByLabelText("Bond symbol or issuer"), { target: { value: "912" } });
    fireEvent.click(screen.getByRole("button", { name: "Look up provider bonds" }));
    fireEvent.change(screen.getByLabelText("Bond symbol or issuer"), { target: { value: "other" } });
    await act(async () => resolve(result));
    await waitFor(() => expect(screen.queryByRole("button", { name: "Review 912TEST" })).not.toBeInTheDocument());
  });
});
