import type { MarginControlAccount, MarginControlCenter } from "@/types";

const accounts: MarginControlAccount[] = [
  account("DEMO-USD", "USD", 64_000, 58_000, 36_000, 42_000),
  account("DEMO-EUR", "EUR", 45_000, 39_000, -5_000, 1_000),
  account("DEMO-UNAVAILABLE", "USD", null, 12_000, null, null)
];

export const analyticalMarginControl: MarginControlCenter = {
  generatedAtUtc: "2026-05-29T12:00:00Z",
  accounts,
  // No mixed-currency prime totals are implied by this demonstration.
  primeSummaries: [],
  alerts: [{ severity: "Critical", providerId: "synthetic-statement", accountId: "DEMO-EUR",
    code: "DISPLAYED_EXCESS_DEFICIT", message: "Synthetic provider excess is below zero.",
    suggestedAction: "Inspect the retained snapshot and its scope." }],
  providerCount: 1,
  accountCount: accounts.length,
  provisionalAccountCount: accounts.length,
  endOfDayCertificationCandidateCount: 0,
  authorityNote: "Provider-reported figures are authoritative for the supplied snapshot. These demonstration records are synthetic.",
  nextAction: "Compare the supplied provider evidence with Meridian diagnostic estimates."
};

function account(accountId: string, currency: string, providerMaintenanceMargin: number | null,
  shadowMaintenanceMargin: number | null, providerExcessLiquidity: number | null,
  shadowExcessLiquidity: number | null): MarginControlAccount {
  return {
    providerId: "synthetic-statement",
    accountId,
    currency,
    asOf: "2026-05-29T11:45:00Z",
    snapshotPhase: "Provisional",
    certificationState: "NotCertified",
    marginRegime: "RegT",
    cash: 20_000,
    equity: 100_000,
    buyingPower: 0,
    providerInitialMargin: null,
    providerMaintenanceMargin,
    providerExcessLiquidity,
    providerMarginLoan: null,
    shadowModelName: "Meridian simplified diagnostic estimate",
    shadowInitialMargin: null,
    shadowMaintenanceMargin,
    shadowExcessLiquidity,
    maintenanceVariance: providerMaintenanceMargin == null || shadowMaintenanceMargin == null
      ? null : providerMaintenanceMargin - shadowMaintenanceMargin,
    riskLevel: providerExcessLiquidity != null && providerExcessLiquidity < 0 ? "Critical" : "Warning",
    activityComplete: false,
    restrictions: ["Synthetic evidence — certification unavailable"],
    positionContributions: [],
    optionLifecycleEventCount: 0,
    borrowPositionCount: 0,
    taxLotCount: 0,
    evidencePath: `synthetic/statements/${accountId}/snapshot.json`
  };
}
