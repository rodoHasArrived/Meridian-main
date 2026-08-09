import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import * as api from "@/lib/api";
import { MarginControlCenterScreen } from "@/screens/margin-control-center-screen";
import { renderWithRouter } from "@/test/render";
import type { MarginControlCenter } from "@/types";

vi.mock("@/lib/api", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api")>("@/lib/api");
  return {
    ...actual,
    getMarginControlCenter: vi.fn(),
    certifyMarginSnapshot: vi.fn()
  };
});

const center: MarginControlCenter = {
  generatedAtUtc: "2026-07-18T23:00:00Z",
  providerCount: 2,
  accountCount: 1,
  provisionalAccountCount: 0,
  endOfDayCertificationCandidateCount: 1,
  authorityNote: "Provider-reported figures are authoritative.",
  nextAction: "Review complete end-of-day evidence before certification.",
  primeSummaries: [
    {
      providerId: "ib-flex",
      accountCount: 1,
      totalEquity: 200000,
      providerMaintenanceMargin: 32000,
      providerExcessLiquidity: 168000,
      criticalAccountCount: 0
    },
    {
      providerId: "alpaca",
      accountCount: 0,
      totalEquity: 0,
      providerMaintenanceMargin: null,
      providerExcessLiquidity: null,
      criticalAccountCount: 0
    }
  ],
  alerts: [
    {
      severity: "Warning",
      providerId: "ib-flex",
      accountId: "U12345",
      code: "MARGIN_SHADOW_VARIANCE",
      message: "Provider and shadow maintenance margin differ.",
      suggestedAction: "Review position contributions and provider methodology."
    }
  ],
  accounts: [
    {
      providerId: "ib-flex",
      accountId: "U12345",
      asOf: "2026-07-18T22:00:00Z",
      snapshotPhase: "EndOfDayCandidate",
      certificationState: "AwaitingOperatorCertification",
      currency: "USD",
      marginRegime: "RegT",
      cash: 50000,
      equity: 200000,
      buyingPower: 300000,
      providerInitialMargin: 50000,
      providerMaintenanceMargin: 32000,
      providerExcessLiquidity: 168000,
      providerMarginLoan: 0,
      shadowModelName: "Meridian Reg T diagnostic shadow",
      shadowInitialMargin: 48000,
      shadowMaintenanceMargin: 30000,
      shadowExcessLiquidity: 170000,
      maintenanceVariance: 2000,
      riskLevel: "Warning",
      activityComplete: true,
      restrictions: [],
      optionLifecycleEventCount: 2,
      borrowPositionCount: 1,
      taxLotCount: 3,
      evidencePath: "accounting/statements/ib-flex/U12345/canonical-evidence.json",
      positionContributions: [
        {
          symbol: "AAPL",
          quantity: 100,
          marketValue: 20000,
          shadowInitialMargin: 10000,
          shadowMaintenanceMargin: 5000,
          borrowStatus: "Available",
          borrowRate: 0.5,
          taxLotCount: 3,
          optionLifecycleEventCount: 2,
          securityId: null,
          securityMasterSource: "ProviderStatementSymbolUnresolved"
        }
      ]
    }
  ]
};

describe("MarginControlCenterScreen", () => {
  it("keeps provider authority visible and certifies an eligible EOD snapshot", async () => {
    vi.mocked(api.getMarginControlCenter).mockResolvedValue(center);
    vi.mocked(api.certifyMarginSnapshot).mockResolvedValue({
      providerId: "ib-flex",
      accountId: "U12345",
      asOf: "2026-07-18T22:00:00Z",
      evidencePath: "accounting/statements/ib-flex/U12345/canonical-evidence.json",
      note: "Reviewed provider statement and position contributions.",
      certifiedBy: "operator-1",
      certifiedAtUtc: "2026-07-18T23:05:00Z",
      status: "Certified"
    });

    renderWithRouter(<MarginControlCenterScreen />);

    expect(await screen.findByRole("heading", { name: "Margin Control Center" })).toBeInTheDocument();
    expect(screen.getByText(/Provider-reported figures are authoritative/)).toBeInTheDocument();
    expect(screen.getByText("Multi-prime rollup")).toBeInTheDocument();
    expect(screen.getByText("MARGIN_SHADOW_VARIANCE")).toBeInTheDocument();
    expect(screen.getByText("Meridian Reg T diagnostic shadow")).toBeInTheDocument();
    expect(screen.getByText("AAPL")).toBeInTheDocument();
    expect(screen.getByText("Unresolved provider symbol")).toBeInTheDocument();

    const user = userEvent.setup();
    await user.type(screen.getByLabelText("Certification note"), "Reviewed provider statement and position contributions.");
    await user.click(screen.getByRole("button", { name: "Certify EOD snapshot" }));

    await waitFor(() => {
      expect(api.certifyMarginSnapshot).toHaveBeenCalledWith({
        providerId: "ib-flex",
        accountId: "U12345",
        asOf: "2026-07-18T22:00:00Z",
        evidencePath: "accounting/statements/ib-flex/U12345/canonical-evidence.json",
        note: "Reviewed provider statement and position contributions."
      });
      expect(api.getMarginControlCenter).toHaveBeenCalledTimes(2);
    });
  });
});
