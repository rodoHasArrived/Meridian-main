import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { ReadinessPanel } from "./readiness-panel";

describe("ReadinessPanel", () => {
  it("renders title, detail, score, and the state badge", () => {
    render(
      <ReadinessPanel
        state="ReviewRequired"
        score="86 / 100"
        title="Reconciliation"
        detail="3 open exceptions above tolerance."
      />,
    );
    expect(screen.getByText("Reconciliation")).toBeInTheDocument();
    expect(screen.getByText("3 open exceptions above tolerance.")).toBeInTheDocument();
    expect(screen.getByText("86 / 100")).toBeInTheDocument();
    expect(screen.getByText("Review")).toBeInTheDocument();
  });

  it("tints the panel by the resolved severity", () => {
    const { container } = render(<ReadinessPanel state="Blocked" title="Ledger" />);
    expect(container.querySelector(".mds-rpanel--blocked")).not.toBeNull();
  });

  it("renders footer actions and body children", () => {
    render(
      <ReadinessPanel state="Ready" title="Approval" actions={<button>Approve</button>}>
        <span>lane body</span>
      </ReadinessPanel>,
    );
    expect(screen.getByRole("button", { name: "Approve" })).toBeInTheDocument();
    expect(screen.getByText("lane body")).toBeInTheDocument();
  });
});
