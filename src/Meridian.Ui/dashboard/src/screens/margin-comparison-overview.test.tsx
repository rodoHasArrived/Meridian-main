import { fireEvent, render, screen, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { axe } from "jest-axe";
import { MarginComparisonOverview } from "./margin-comparison-overview";
import type { MarginControlAccount } from "@/types";

const account = { providerId: "synthetic-broker", accountId: "A", currency: "USD", providerMaintenanceMargin: 100,
  shadowMaintenanceMargin: 80, providerInitialMargin: 200, shadowInitialMargin: 190,
  providerExcessLiquidity: -10, asOf: "2026-11-12T10:00:00Z", snapshotPhase: "Provisional", certificationState: "NotCertified", riskLevel: "Critical"
} as MarginControlAccount;

describe("margin comparison overview", () => {
  it("shows source labels, deficits and currency-specific comparisons without changing authority", async () => {
    const select = vi.fn();
    const { container } = render(<MarginComparisonOverview accounts={[account, { ...account, accountId: "B", currency: "EUR", providerMaintenanceMargin: null, providerExcessLiquidity: null }]} selectedId={null} onSelect={select} />);
    expect(screen.getByText(/Broker figures remain authoritative/)).toBeInTheDocument();
    expect(within(screen.getByRole("group", { name: "Margin comparisons in USD" })).getByText("-$10.00 · Deficit")).toBeInTheDocument();
    expect(within(screen.getByRole("group", { name: "Margin comparisons in EUR" })).getAllByText(/Not reported/).length).toBeGreaterThan(0);
    fireEvent.change(screen.getByLabelText("Margin comparison requirement"), { target: { value: "initial" } });
    expect(screen.getByText("Broker $200.00")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /synthetic-broker · A/ }));
    expect(select).toHaveBeenCalledWith(account);
    expect((await axe(container)).violations).toEqual([]);
  });
});
