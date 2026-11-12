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
});
