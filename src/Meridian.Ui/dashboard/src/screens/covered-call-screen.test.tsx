import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import * as coveredCallApi from "@/lib/api/covered-call";
import { CoveredCallScreen } from "@/screens/covered-call-screen";
import { COVERED_CALL_CHAIN_DETAIL_PANEL_ID } from "@/screens/covered-call-screen.view-model";
import type { CoveredCallChainPreview } from "@/types/covered-call";

vi.mock("@/lib/api/covered-call", () => ({
  startCoveredCallBacktest: vi.fn(),
  getCoveredCallRunStatus: vi.fn(),
  getCoveredCallRunResult: vi.fn(),
  cancelCoveredCallRun: vi.fn(),
  previewCoveredCallChain: vi.fn(),
  listCoveredCallRuns: vi.fn()
}));

const chainPreview: CoveredCallChainPreview = {
  underlyingSymbol: "SPY",
  asOf: "2024-01-01",
  underlyingPrice: 500,
  totalContractsScanned: 2,
  filtersPassed: 1,
  candidates: [
    {
      strike: 505,
      expiration: "2024-02-16",
      daysToExpiration: 32,
      bid: 2.41,
      ask: 2.58,
      delta: 0.31,
      impliedVolatility: 0.22,
      openInterest: 1040,
      volume: 122,
      meetsAllFilters: true,
      rejectReason: null
    },
    {
      strike: 510,
      expiration: "2024-02-16",
      daysToExpiration: 32,
      bid: 1.71,
      ask: 1.95,
      delta: 0.42,
      impliedVolatility: null,
      openInterest: 84,
      volume: 12,
      meetsAllFilters: false,
      rejectReason: "Open interest below minimum"
    }
  ]
};

describe("CoveredCallScreen", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(coveredCallApi.previewCoveredCallChain).mockResolvedValue(chainPreview);
    vi.mocked(coveredCallApi.listCoveredCallRuns).mockResolvedValue([]);
  });

  it("renders chain preview rows with keyboard selection and a linked detail panel", async () => {
    render(<CoveredCallScreen />);

    fireEvent.change(screen.getByLabelText(/Min strike/i), { target: { value: "500" } });

    await waitFor(() => {
      expect(coveredCallApi.previewCoveredCallChain).toHaveBeenCalled();
    });

    const passingRow = await screen.findByRole("row", {
      name: "Inspect SPY 505.00 call expiring 2024-02-16. Status Pass."
    });
    const rejectedRow = await screen.findByRole("row", {
      name: "Inspect SPY 510.00 call expiring 2024-02-16. Status Open interest below minimum."
    });

    expect(passingRow).toHaveAttribute("aria-controls", COVERED_CALL_CHAIN_DETAIL_PANEL_ID);
    expect(passingRow).toHaveAttribute("aria-selected", "true");
    expect(rejectedRow).toHaveAttribute("aria-controls", COVERED_CALL_CHAIN_DETAIL_PANEL_ID);
    expect(rejectedRow).toHaveAttribute("aria-expanded", "false");

    rejectedRow.focus();
    fireEvent.keyDown(rejectedRow, { key: "Enter" });

    expect(rejectedRow).toHaveAttribute("aria-selected", "true");
    expect(rejectedRow).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("region", {
      name: "Selected covered-call candidate: SPY 510.00 call expiring 2024-02-16"
    })).toBeInTheDocument();
    expect(screen.getByText("This contract is excluded by the current filter set: Open interest below minimum.")).toBeInTheDocument();
  });
});
