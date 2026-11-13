import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { FixedIncomeCapabilityReadiness } from "./data-screen.fixed-income-capabilities";
import type { CapabilityMatrixRowModel } from "./data-screen.capability-matrix.view-model";

const rows: CapabilityMatrixRowModel[] = [
  {
    providerId: "ibkr", declaredCount: 1, cells: [], fixedIncomeTasks: [{
      task: "Terms", adapterSupport: "AdapterDeclared", detail: "Contract details require convention review", connections: [{
        connectionId: "owned-connection", displayName: "Owned TWS", enabled: true, configurationState: "Configured",
        entitlementState: "Unknown", freshnessState: "Unknown", verificationState: "live-untested",
        observationAsOf: null, verifiedAt: null, evidence: [{ kind: "ConnectionCertification", detail: "General connection certification does not verify terms" }]
      }]
    }]
  },
  { providerId: "fred", declaredCount: 1, cells: [], fixedIncomeTasks: [{ task: "Curves", adapterSupport: "AdapterDeclared", detail: "Construction method required", connections: [] }] }
];

describe("fixed-income task readiness", () => {
  it("shows retained connection evidence without presenting unknown inputs as ready", () => {
    render(<FixedIncomeCapabilityReadiness rows={rows} />);
    expect(screen.getByRole("table", { name: "ibkr fixed-income task readiness" })).toBeTruthy();
    expect(screen.getByRole("rowheader", { name: "Terms" })).toBeTruthy();
    expect(screen.getAllByRole("columnheader")).toHaveLength(6);
    expect(screen.getByText("Owned TWS")).toBeTruthy();
    expect(screen.getByText("owned-connection")).toBeTruthy();
    expect(screen.getByText("live-untested")).toBeTruthy();
    expect(screen.getAllByText("Unknown")).toHaveLength(2);
    fireEvent.click(screen.getByText("Inspect evidence"));
    expect(screen.getByText(/General connection certification does not verify terms/)).toBeTruthy();
    fireEvent.change(screen.getByLabelText("Provider"), { target: { value: "fred" } });
    expect(screen.getByRole("table", { name: "fred fixed-income task readiness" })).toBeTruthy();
    expect(screen.getByText("No configured connection")).toBeTruthy();
    expect(screen.getByText("Construction method required")).toBeTruthy();
  });

  it("retains all eight task rows and their accessible headings in the shared table", () => {
    const taskNames = ["Terms", "EvaluatedPrices", "ExecutableQuotes", "Trades", "Curves", "Fixings", "Factors", "LifecycleNotices"];
    const provider: CapabilityMatrixRowModel = {
      providerId: "ibkr", declaredCount: 1, cells: [],
      fixedIncomeTasks: taskNames.map((task) => ({ task, adapterSupport: "Unsupported", detail: `${task} evidence unavailable`, connections: [] }))
    };
    render(<FixedIncomeCapabilityReadiness rows={[provider]} />);
    expect(screen.getAllByRole("rowheader").map((header) => header.textContent)).toEqual([
      "Terms", "Evaluated prices", "Executable quotes", "Trades", "Curves", "Fixings", "Factors", "Lifecycle notices"
    ]);
    expect(screen.getAllByText("No configured connection")).toHaveLength(8);
    expect(screen.getAllByText("Coverage details")).toHaveLength(8);
  });
});
