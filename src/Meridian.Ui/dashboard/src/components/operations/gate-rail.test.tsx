import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { GateRail } from "./gate-rail";

const gates = [
  { key: "BrokerIngest", label: "Broker ingest", status: "Passed" },
  { key: "SecurityMaster", label: "Security master", status: "Passed" },
  { key: "LedgerPosting", label: "Ledger posting", status: "InProgress" },
  { key: "Reconciliation", label: "Reconciliation", status: "ReviewRequired" },
  { key: "Approval", label: "Approval", status: "NotStarted" },
];

describe("GateRail", () => {
  it("renders one node per gate with its label", () => {
    const { container } = render(<GateRail gates={gates} />);
    expect(container.querySelectorAll(".mds-gate")).toHaveLength(gates.length);
    expect(screen.getByText("Broker ingest")).toBeInTheDocument();
    expect(screen.getByText("Approval")).toBeInTheDocument();
  });

  it("tints nodes by status and marks passed gates with a check glyph", () => {
    const { container } = render(<GateRail gates={gates} />);
    expect(container.querySelectorAll(".mds-gate--ready")).toHaveLength(2);
    expect(container.querySelector(".mds-gate--review")).not.toBeNull();
    expect(container.querySelector(".mds-gate--info")).not.toBeNull();
    expect(screen.getAllByText("✓").length).toBeGreaterThan(0);
  });

  it("exposes an accessible label combining gate and humanized status", () => {
    render(<GateRail gates={gates} />);
    expect(screen.getByLabelText("Ledger posting: In Progress")).toBeInTheDocument();
  });

  it("renders nothing catastrophic for an empty gate list", () => {
    const { container } = render(<GateRail gates={[]} />);
    expect(container.querySelectorAll(".mds-gate")).toHaveLength(0);
  });
});
