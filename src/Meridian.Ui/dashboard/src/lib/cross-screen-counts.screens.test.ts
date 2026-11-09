import { describe, expect, it } from "vitest";
import { buildTrustStripState } from "@/app-shell.trust-strip";
import { applyCrossScreenCounts, selectCrossScreenCounts } from "@/lib/cross-screen-counts";
import { resolveDevFixture } from "@/lib/dev-fixtures";
import { WORKSTATION_API_ENDPOINTS } from "@/lib/workstation-endpoints";
import { buildAccountingReconciliationContinuityStatus } from "@/screens/accounting-screen.workflow-continuity";
import { buildSettingsScreenViewModel } from "@/screens/settings-screen.view-model";
import type {
  AccountingWorkspaceResponse,
  DataWorkspaceResponse,
  SystemOverviewResponse,
  TradingWorkspaceResponse
} from "@/types";
import type { WorkstationCrossScreenCounts } from "@/types/cross-screen-counts";

// A3 acceptance: one fixture set, and every screen that shows a cross-screen count shows the same
// value, through the same calls the screens make.
const trading = resolveDevFixture<TradingWorkspaceResponse>(WORKSTATION_API_ENDPOINTS.trading)!;
const data = resolveDevFixture<DataWorkspaceResponse>(WORKSTATION_API_ENDPOINTS.data)!;
const accounting = resolveDevFixture<AccountingWorkspaceResponse>(WORKSTATION_API_ENDPOINTS.accounting)!;
const overview = resolveDevFixture<SystemOverviewResponse>(WORKSTATION_API_ENDPOINTS.systemStatus)!;
const shared = resolveDevFixture<WorkstationCrossScreenCounts>(WORKSTATION_API_ENDPOINTS.crossScreenCounts)!;

function metricValue(metrics: { id: string; value: string }[], id: string): string | undefined {
  return metrics.find((metric) => metric.id === id)?.value;
}

describe("cross-screen counts agree across Trading, Settings and Data", () => {
  it("shows one provider-health count on Data, Settings and the Trading trust strip", () => {
    const dataHeadline = metricValue(
      applyCrossScreenCounts(data.metrics, selectCrossScreenCounts({ shared, providers: data.providers })),
      "providers"
    );
    const settings = buildSettingsScreenViewModel({ session: null, overview, crossScreenCounts: shared });
    const settingsValue = settings.systemItems.find((item) => item.label === "Providers healthy")?.value;
    // Trading does not load the Data workspace; its trust strip reads the shell's shared counts.
    const tradingStrip = buildTrustStripState({
      loading: false,
      bootstrapFailed: false,
      usingDevelopmentFixtures: true,
      workspaceErrors: {},
      session: null,
      data: null,
      crossScreenCounts: shared
    }).items.find((item) => item.id === "providers")?.value;

    const healthy = data.providers.filter((provider) => provider.status === "Healthy").length;
    const expected = `${healthy}/${data.providers.length}`;
    expect(dataHeadline).toBe(expected);
    expect(settingsValue?.replace(/\s/g, "")).toBe(expected);
    expect(tradingStrip).toBe(`${expected} healthy`);
    // The overview's separate "providers online" tally is no longer what Settings shows.
    expect(`${overview.providersOnline}/${overview.providersTotal}`).not.toBe(expected);
  });

  it("shows one open-order count on the Trading headline, its completeness card and the shared counts", () => {
    const values = selectCrossScreenCounts({ shared, openOrders: trading.openOrders });
    const headline = metricValue(applyCrossScreenCounts(trading.metrics, values), "orders");
    expect(headline).toBe(String(trading.openOrders.length));
    expect(values.openOrders).toBe(trading.openOrders.length);
    expect(shared.openOrders?.count).toBe(trading.openOrders.length);
  });

  it("shows one open-break count on the Accounting headline, its continuity step and the shared counts", () => {
    const values = selectCrossScreenCounts({ shared, breakQueue: accounting.breakQueue });
    const headline = metricValue(applyCrossScreenCounts(accounting.metrics, values), "breaks");
    const continuity = buildAccountingReconciliationContinuityStatus(accounting).label;
    expect(headline).toBe(String(values.openBreaks));
    expect(shared.openBreaks?.count).toBe(values.openBreaks);
    expect(continuity).toContain(String(values.openBreaks));
  });
});
