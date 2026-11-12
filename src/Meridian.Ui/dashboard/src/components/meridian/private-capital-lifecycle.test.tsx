import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { describe, expect, it } from "vitest";
import type { CapitalCommitment } from "@/types/family-office.types";
import { PrivateCapitalLifecycle } from "./private-capital-lifecycle";
import { groupPrivateCapital, hasPrivateCapitalBalances } from "./private-capital-lifecycle.view-model";

const vehicle = (overrides: Partial<CapitalCommitment> = {}): CapitalCommitment => ({
  commitmentId: "fund-a", entityId: "entity-a", vehicleName: "Growth Fund", strategy: "Growth", currency: "USD",
  commitmentAmount: 100, calledAmount: 80, unfundedAmount: 35, distributedAmount: 15, currentNav: 90,
  vintageYear: 2024, sourceSystem: "manager-statement", sourceDocumentId: "statement-a", asOfDate: "2026-10-31",
  valuationDate: "2026-09-30", evidenceCompleteness: "Complete", reconciliationStatus: "NotReconciled",
  balancesAvailable: true, snapshotId: "snapshot-a", sourceRevision: 2, balanceStatus: "Provisional",
  ...overrides
});

describe("private-capital lifecycle", () => {
  it("keeps supplied unfunded and distributions distinct, and never scales across currencies", () => {
    const view = groupPrivateCapital([vehicle(), vehicle({ commitmentId: "fund-b", currency: "EUR", commitmentAmount: 9000 })], "vintage", "USD");
    expect(view.maximum).toBe(100);
    expect(view.groups[0].rows[0].unfundedAmount).toBe(35);
    expect(view.groups[0].rows[0].distributedAmount).toBe(15);
    expect(hasPrivateCapitalBalances(vehicle({ balancesAvailable: false }))).toBe(false);
    expect(hasPrivateCapitalBalances(vehicle({ commitmentAmount: 0, calledAmount: 0, unfundedAmount: 0, distributedAmount: 0, currentNav: 0 }))).toBe(true);
  });

  it("changes currency and vehicle selection, preserving status and source evidence", async () => {
    const user = userEvent.setup();
    render(<PrivateCapitalLifecycle commitments={[vehicle(), vehicle({ commitmentId: "fund-eur", vehicleName: "Credit Fund", currency: "EUR", sourceDocumentId: "statement-eur" })]} asOfDate="2026-11-01" />);
    await user.selectOptions(screen.getByLabelText("Lifecycle currency"), "USD");
    expect(screen.getByRole("button", { name: "Growth Fund" })).toHaveAttribute("aria-pressed", "true");
    expect(within(screen.getByRole("region", { name: "Selected private-capital vehicle" })).getByText("statement-a")).toBeInTheDocument();
    await user.selectOptions(screen.getByLabelText("Lifecycle currency"), "EUR");
    expect(screen.queryByRole("button", { name: "Growth Fund" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Credit Fund" })).toHaveAttribute("aria-pressed", "true");
    expect(within(screen.getByRole("region", { name: "Selected private-capital vehicle" })).getByText("statement-eur")).toBeInTheDocument();
    await user.selectOptions(screen.getByLabelText("Group vehicles by"), "strategy");
    expect(screen.getByRole("rowheader", { name: "Growth" })).toBeInTheDocument();
  });

  it("shows unavailable balances rather than zero and exposes a usable accessible table", async () => {
    const { container } = render(<PrivateCapitalLifecycle commitments={[vehicle({ balancesAvailable: false })]} asOfDate="2026-11-01" />);
    expect(screen.getAllByText("Unavailable").length).toBeGreaterThanOrEqual(5);
    expect(screen.queryByText("0 USD")).not.toBeInTheDocument();
    expect(screen.getByText(/numeric placeholders do not represent zero/)).toBeInTheDocument();
    expect((await axe(container)).violations).toEqual([]);
  });
});
