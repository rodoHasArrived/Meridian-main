import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { MarginDisagreementView, marginCurrencyGroups } from "./margin-disagreement-view";
import type { MarginControlAccount } from "@/types";

const account: MarginControlAccount = { providerId: "Broker", accountId: "A", asOf: "2026-11-12", snapshotPhase: "IntradayProvisional", certificationState: "StaleEvidence", currency: "USD", marginRegime: "RegT", cash: 0, equity: 100, buyingPower: 0, providerInitialMargin: null, providerMaintenanceMargin: 120, providerExcessLiquidity: -20, providerMarginLoan: null, shadowModelName: "Simplified model", shadowInitialMargin: null, shadowMaintenanceMargin: 25, shadowExcessLiquidity: 75, maintenanceVariance: 95, riskLevel: "Critical", activityComplete: false, restrictions: ["Restricted"], positionContributions: [], optionLifecycleEventCount: 0, borrowPositionCount: 0, taxLotCount: 0, evidencePath: "retained/source.json" };
describe("margin disagreement", () => {
  it("separates currencies, preserves unavailable values and exposes evidence through keyboard selection", async () => {
    const eur = { ...account, accountId: "B", currency: "EUR", providerMaintenanceMargin: null, providerExcessLiquidity: null };
    expect(marginCurrencyGroups([account, eur]).map(([currency, rows]) => [currency, rows.length])).toEqual([["EUR", 1], ["USD", 1]]);
    render(<MarginDisagreementView accounts={[account, eur]} />);
    expect(within(screen.getByRole("table", { name: "EUR margin comparison" })).getAllByText("Not reported")).toHaveLength(2);
    expect(screen.getByText("-20 USD")).toBeInTheDocument();
    const user = userEvent.setup();
    screen.getByRole("button", { name: "Broker · A" }).focus();
    await user.keyboard("{Enter}");
    expect(screen.getByRole("button", { name: "Broker · A" })).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByLabelText("Selected margin account")).toHaveTextContent("Simplified model");
    expect(screen.getByLabelText("Selected margin account")).toHaveTextContent("retained/source.json");
    expect(screen.getByLabelText("Selected margin account")).toHaveTextContent("StaleEvidence");
  });
});
